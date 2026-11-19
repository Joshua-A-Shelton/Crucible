using System.Runtime.InteropServices;
using Crucible.Binary;
using Crucible.Importers;
using Crucible.Serialization;
using ImportersApi = Crucible.Importers.Api;

namespace Crucible.Assets.Importers;

/// <summary>A texture's import settings, kept in its sidecar (ADR 0042).</summary>
[Serialized]
public partial class TextureImportSettings
{
    /// <summary>What it's for, which picks the format: Color is BC7 sRGB, NormalMap BC5, and so on.</summary>
    public TextureUsage Usage = TextureUsage.Color;

    /// <summary>A full mip chain, filtered in linear light with a Kaiser-windowed sinc.</summary>
    public bool Mipmaps = true;

    /// <summary>Filter across the edges as if the texture tiles; off for sprites and UI.</summary>
    public bool Wrap = true;

    /// <summary>Keep alpha-tested surfaces (foliage, fences) from thinning out in smaller mips.</summary>
    public bool PreserveAlphaCoverage;

    public float AlphaCutoff = 0.5f;

    /// <summary>BC7 rate-distortion trade-off: higher makes smaller files at some quality; 0 turns it off.</summary>
    public float RdoLambda = 1.0f;

    /// <summary>BC7 encoder effort, 0 (fast) to 4 (best).</summary>
    public int Quality = 2;

    /// <summary>How many of its pixels make a meter where a sprite shows it (ADR 0104).</summary>
    public float PixelsPerUnit = 16;

    /// <summary>How a sprite sheet is cut into frames (ADR 0107): a grid of cells, or rectangles drawn one by one.</summary>
    [Header("Sprite Sheet")]
    public SpriteSlicing Slicing = SpriteSlicing.Grid;

    /// <summary>A grid cell's size in pixels; 0 makes the whole texture one frame.</summary>
    public int CellWidth;

    public int CellHeight;

    /// <summary>Pixels around the grid, left out.</summary>
    public int Margin;

    /// <summary>Pixels between cells, left out.</summary>
    public int Spacing;

    /// <summary>The frames, with <see cref="SpriteSlicing.Rectangles"/>.</summary>
    public List<SpriteFrameSettings> Frames = [];

    /// <summary>Frame animations, each imported as a .crspriteclip of the sheet ("Hero.png / Walk").</summary>
    public List<SpriteClipSettings> Clips = [];
}

/// <summary>How a sprite sheet is cut into frames (ADR 0107).</summary>
public enum SpriteSlicing
{
    /// <summary>Cells of one size, numbered left to right, top to bottom.</summary>
    Grid,

    /// <summary>Rectangles of any size, numbered as listed.</summary>
    Rectangles,
}

/// <summary>A frame of a sprite sheet, in pixels from its top-left corner.</summary>
[Serialized]
public partial class SpriteFrameSettings
{
    public int X;
    public int Y;
    public int Width;
    public int Height;
}

/// <summary>A frame animation: frames of the sheet, each shown for its duration.</summary>
[Serialized]
public partial class SpriteClipSettings
{
    public string Name = "";

    /// <summary>The duration frames added in the Sprite Sheet Editor start with, as frames a second.</summary>
    public float FramesPerSecond = 12;

    public List<SpriteClipFrameSettings> Frames = [];

    /// <summary>Start over at the end, played alone (a state machine's state says, in one).</summary>
    public bool Loop = true;
}

/// <summary>One frame of a clip: the sheet's frame by number, how long it shows, and the events at its start.</summary>
[Serialized]
public partial class SpriteClipFrameSettings
{
    public int Frame;

    /// <summary>Seconds.</summary>
    public float Duration = 0.1f;

    public List<string> Events = [];
}

/// <summary>
/// Imports PNG, JPEG, TGA, and Radiance HDR images as textures (ADR 0042):
/// the native importer decodes, makes mipmaps, and compresses; this writes
/// the result as a <c>TXTR</c> binary file with an <c>INFO</c> section and
/// one section per mip, <c>MIP0</c> to <c>MIPF</c>, and a <c>SPRT</c> section
/// with its pixels per unit for sprites (ADR 0104). Its sprite sheet's
/// clips become .crspriteclip sub-assets (ADR 0107). Names are placeholders
/// (docs/naming.md).
/// </summary>
public sealed class TextureImporter : Importer<TextureImportSettings>
{
    public static readonly uint Kind = Tag.Of("TXTR");
    public static readonly uint InfoSection = Tag.Of("INFO");
    public static readonly uint SpriteSection = Tag.Of("SPRT");
    public const uint KindVersion = 1;

    /// <summary>A frame animation's file (ADR 0107), as SpriteClipFile.h lays it out.</summary>
    public static readonly uint ClipKind = Tag.Of("SPCL");
    public const uint ClipVersion = 1; // as spriteClipFileVersion
    public const string ClipExtension = ".crspriteclip";

    public override string Name => "Texture";

    // 2: the SPRT section (ADR 0104); 3: sprite clips (ADR 0107).
    public override int Version => 3;

    public override IReadOnlyList<string> Extensions => [".png", ".jpg", ".jpeg", ".tga", ".hdr"];

    public override string OutputExtension => ".crtexture";

    /// <summary>The section holding a mip: MIP0 for the top level, then MIP1 … MIPF.</summary>
    public static uint MipSection(int mip) => Tag.Of($"MIP{mip:X}");

    protected override TextureImportSettings Defaults(string assetPath) =>
        assetPath.EndsWith(".hdr", StringComparison.OrdinalIgnoreCase)
            ? new TextureImportSettings { Usage = TextureUsage.Hdr }
            : new TextureImportSettings();

    public override void Import(ImportContext context, TextureImportSettings settings)
    {
        TextureSettings native = new()
        {
            Usage = settings.Usage,
            Mipmaps = settings.Mipmaps,
            Wrap = settings.Wrap,
            PreserveAlphaCoverage = settings.PreserveAlphaCoverage,
            AlphaCutoff = settings.AlphaCutoff,
            RdoLambda = Math.Max(0, settings.RdoLambda),
            Quality = (uint)Math.Clamp(settings.Quality, 0, 4),
        };
        TextureImportHandle texture;
        try
        {
            texture = ImportersApi.ImportTexture(context.ReadSource(), native);
        }
        catch (CrucibleException exception) when (exception.Status == Status.InvalidData)
        {
            throw new InvalidDataException(ImportersApi.LastImportError(), exception);
        }
        try
        {
            TextureInfo info = ImportersApi.TextureInfo(texture);
            ImportedFileWriter output = context.CreateOutput(Kind, KindVersion);
            output.Add(InfoSection, 1, MemoryMarshal.AsBytes(new ReadOnlySpan<TextureInfo>(in info)));
            float pixelsPerUnit = settings.PixelsPerUnit > 0 ? settings.PixelsPerUnit : 16;
            output.Add(SpriteSection, 1, MemoryMarshal.AsBytes(new ReadOnlySpan<float>(in pixelsPerUnit)));
            for (int mip = 0; mip < info.MipCount; ++mip)
            {
                byte[] bytes = new byte[ImportersApi.TextureMipSize(texture, (uint)mip)];
                ImportersApi.CopyTextureMip(texture, (uint)mip, bytes);
                output.Add(MipSection(mip), 1, bytes);
            }
            AddClips(context, settings, (int)info.Width, (int)info.Height);
        }
        finally
        {
            ImportersApi.DestroyTextureImport(texture);
        }
    }

    /// <summary>
    /// The sheet's frames as its settings cut it: a grid's cells left to
    /// right, top to bottom (the whole texture without a cell size), or the
    /// rectangles as listed.
    /// </summary>
    public static List<SpriteFrameSettings> FramesOf(TextureImportSettings settings, int width, int height)
    {
        if (settings.Slicing == SpriteSlicing.Rectangles)
        {
            return settings.Frames;
        }
        if (settings.CellWidth <= 0 || settings.CellHeight <= 0)
        {
            return [new SpriteFrameSettings { Width = width, Height = height }];
        }
        int margin = Math.Max(settings.Margin, 0);
        int spacing = Math.Max(settings.Spacing, 0);
        int columns = Math.Max((width - 2 * margin + spacing) / (settings.CellWidth + spacing), 0);
        int rows = Math.Max((height - 2 * margin + spacing) / (settings.CellHeight + spacing), 0);
        List<SpriteFrameSettings> frames = [];
        for (int row = 0; row < rows; ++row)
        {
            for (int column = 0; column < columns; ++column)
            {
                frames.Add(new SpriteFrameSettings
                {
                    X = margin + column * (settings.CellWidth + spacing),
                    Y = margin + row * (settings.CellHeight + spacing),
                    Width = settings.CellWidth,
                    Height = settings.CellHeight,
                });
            }
        }
        return frames;
    }

    public static readonly uint ClipInfoSection = Tag.Of("INFO");
    public static readonly uint ClipFrameSection = Tag.Of("FRMS");
    public static readonly uint ClipEventSection = Tag.Of("EVNT");

    // Each clip baked to its frames' regions, so the runtime never slices.
    private static void AddClips(ImportContext context, TextureImportSettings settings, int width, int height)
    {
        if (settings.Clips.Count == 0)
        {
            return;
        }
        List<SpriteFrameSettings> frames = FramesOf(settings, width, height);
        for (int f = 0; f < frames.Count; ++f)
        {
            SpriteFrameSettings frame = frames[f];
            if (frame.X < 0 || frame.Y < 0 || frame.Width <= 0 || frame.Height <= 0 || frame.X + frame.Width > width ||
                frame.Y + frame.Height > height)
            {
                throw new InvalidDataException(
                    $"frame {f} ({frame.X}, {frame.Y}, {frame.Width}x{frame.Height}) isn't within the {width}x{height} texture");
            }
        }
        foreach (SpriteClipSettings clip in settings.Clips)
        {
            string name = clip.Name.Trim();
            if (name.Length == 0)
            {
                throw new InvalidDataException("a clip has no name");
            }
            if (clip.Frames.Count == 0)
            {
                throw new InvalidDataException($"clip {name} has no frames");
            }
            SpriteClipFrame[] baked = new SpriteClipFrame[clip.Frames.Count];
            List<ClipEvent> events = [];
            float start = 0;
            for (int i = 0; i < clip.Frames.Count; ++i)
            {
                SpriteClipFrameSettings frame = clip.Frames[i];
                if (frame.Frame < 0 || frame.Frame >= frames.Count)
                {
                    throw new InvalidDataException(
                        $"clip {name}: frame {frame.Frame} isn't one of the sheet's ({frames.Count})");
                }
                if (!(frame.Duration > 0))
                {
                    throw new InvalidDataException($"clip {name}: its frame {i} needs a duration over 0");
                }
                SpriteFrameSettings region = frames[frame.Frame];
                baked[i] = new SpriteClipFrame
                {
                    X = (uint)region.X,
                    Y = (uint)region.Y,
                    Width = (uint)region.Width,
                    Height = (uint)region.Height,
                    Start = start,
                    Duration = frame.Duration,
                };
                events.AddRange(frame.Events.Where(e => e.Trim().Length > 0)
                    .Select(e => new ClipEvent { Time = start, Name = Animation.AnimationEvent.NameOf(e.Trim()) }));
                start += frame.Duration;
            }
            SpriteClipInfo info = new()
            {
                Texture = context.Asset,
                Duration = start,
                FrameCount = (uint)baked.Length,
                Loop = clip.Loop ? 1u : 0u,
            };
            ImportedFileWriter output = context.AddSubAsset(name, ClipKind, ClipVersion, ClipExtension);
            output.Add(ClipInfoSection, 1, MemoryMarshal.AsBytes(new ReadOnlySpan<SpriteClipInfo>(in info)));
            output.Add(ClipFrameSection, 1, MemoryMarshal.AsBytes(baked.AsSpan()));
            if (events.Count > 0)
            {
                output.Add(ClipEventSection, 1, MemoryMarshal.AsBytes(events.ToArray().AsSpan()));
            }
        }
    }
}
