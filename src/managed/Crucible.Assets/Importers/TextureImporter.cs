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
}

/// <summary>
/// Imports PNG, JPEG, TGA, and Radiance HDR images as textures (ADR 0042):
/// the native importer decodes, makes mipmaps, and compresses; this writes
/// the result as a <c>TXTR</c> binary file with an <c>INFO</c> section and
/// one section per mip, <c>MIP0</c> to <c>MIPF</c>. Names are placeholders
/// (docs/naming.md).
/// </summary>
public sealed class TextureImporter : Importer<TextureImportSettings>
{
    public static readonly uint Kind = Tag.Of("TXTR");
    public static readonly uint InfoSection = Tag.Of("INFO");
    public const uint KindVersion = 1;

    public override string Name => "Texture";

    public override int Version => 1;

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
            for (int mip = 0; mip < info.MipCount; ++mip)
            {
                byte[] bytes = new byte[ImportersApi.TextureMipSize(texture, (uint)mip)];
                ImportersApi.CopyTextureMip(texture, (uint)mip, bytes);
                output.Add(MipSection(mip), 1, bytes);
            }
        }
        finally
        {
            ImportersApi.DestroyTextureImport(texture);
        }
    }
}
