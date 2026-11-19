using System.Runtime.InteropServices;
using Crucible.Assets;
using Crucible.Assets.Importers;
using Crucible.Binary;
using Crucible.Importers;

namespace Crucible.Tests.Assets;

// The texture importer (ADR 0042) through the asset database.
public class TextureImporterTests
{
    // An uncompressed 32-bit TGA, top row first: simple enough to write here.
    private static byte[] Tga(int width, int height, Func<int, int, (byte R, byte G, byte B, byte A)> texel)
    {
        byte[] file = new byte[18 + width * height * 4];
        file[2] = 2; // uncompressed true colour
        BitConverter.TryWriteBytes(file.AsSpan(12), (ushort)width);
        BitConverter.TryWriteBytes(file.AsSpan(14), (ushort)height);
        file[16] = 32;
        file[17] = 0x28; // 8 alpha bits, top-left origin
        for (int y = 0; y < height; ++y)
        {
            for (int x = 0; x < width; ++x)
            {
                (byte r, byte g, byte b, byte a) = texel(x, y);
                int at = 18 + (y * width + x) * 4;
                (file[at], file[at + 1], file[at + 2], file[at + 3]) = (b, g, r, a);
            }
        }
        return file;
    }

    private static byte[] Gradient(int size) => Tga(size, size, (x, y) => ((byte)(x * 255 / size), (byte)(y * 255 / size), 128, 255));

    private sealed class Scratch : IDisposable
    {
        public Project Project { get; } =
            Project.Create(Path.Combine(Path.GetTempPath(), "crucible-textures-" + Guid.NewGuid().ToString("N")), "Game");

        public AssetDatabase Open() => AssetDatabase.Open(Project, [new TextureImporter()]);

        public string PathOf(string assetPath) => Path.Combine(Project.AssetsFolder, assetPath);

        public void Dispose() => Directory.Delete(Project.Root, recursive: true);
    }

    private static TextureInfo Info(ImportedFile file) =>
        MemoryMarshal.Read<TextureInfo>(file.Read(file.Find(TextureImporter.InfoSection)));

    [Test]
    public async Task TheGeneratedLayoutsMatchTheImportersLibrary()
    {
        await Assert.That(Crucible.Assets.Interop.LayoutChecks.Verify()).IsNull();
    }

    [Test]
    public async Task AnImageBecomesATextureFileWithEveryMip()
    {
        using Scratch scratch = new();
        File.WriteAllBytes(scratch.PathOf("wall.tga"), Gradient(64));
        AssetDatabase database = scratch.Open();
        ImportResult result = database.ImportAll().Single();
        await Assert.That(result.Outcome).IsEqualTo(ImportOutcome.Imported);

        using ImportedFile file = ImportedFile.Open(result.OutputPath!, TextureImporter.Kind, TextureImporter.KindVersion);
        TextureInfo info = Info(file);
        await Assert.That((info.Format, info.Width, info.Height, info.MipCount, info.Srgb))
            .IsEqualTo((TextureFormat.Bc7, 64u, 64u, 7u, 1u));
        await Assert.That(file.SectionCount).IsEqualTo(9); // INFO, seven mips, and SPRT
        await Assert.That(file.Section(file.Find(TextureImporter.MipSection(0))).Size).IsEqualTo(16ul * 16 * 16);
        await Assert.That(file.Section(file.Find(TextureImporter.MipSection(6))).Size).IsEqualTo(16ul);
        await Assert.That(Tag.Of("MIPA")).IsEqualTo(TextureImporter.MipSection(10));
    }

    // Pixel art (ADR 0104): the source's bytes exactly, one level, and its pixels per unit.
    [Test]
    public async Task ASpriteTextureKeepsItsExactTexelsAndRecordsItsPixelsPerUnit()
    {
        using Scratch scratch = new();
        static (byte, byte, byte, byte) Texel(int x, int y) => ((byte)(x * 37 + 3), (byte)(y * 53 + 1), (byte)(x * y + 7), (byte)(x == 0 ? 0 : 255));
        File.WriteAllBytes(scratch.PathOf("hero.tga"), Tga(6, 3, Texel));
        AssetDatabase database = scratch.Open();
        database.ImportAll();
        string sidecar = scratch.PathOf("hero.tga.crmeta");
        File.WriteAllText(sidecar, File.ReadAllText(sidecar).Replace("\"Usage\": \"Color\"", "\"Usage\": \"Sprite\"")
            .Replace("\"PixelsPerUnit\": 16", "\"PixelsPerUnit\": 32"));
        database.Refresh();
        ImportResult result = database.ImportAll().Single();
        await Assert.That(result.Outcome).IsEqualTo(ImportOutcome.Imported).Because(result.Error ?? "");

        using ImportedFile file = ImportedFile.Open(result.OutputPath!, TextureImporter.Kind, TextureImporter.KindVersion);
        TextureInfo info = Info(file);
        await Assert.That((info.Format, info.Width, info.Height, info.MipCount, info.Srgb))
            .IsEqualTo((TextureFormat.Rgba8, 6u, 3u, 1u, 1u));
        byte[] texels = file.Read(file.Find(TextureImporter.MipSection(0))).ToArray();
        bool exact = true;
        for (int y = 0; y < 3; ++y)
        {
            for (int x = 1; x < 6; ++x) // a clear texel's color isn't kept
            {
                (byte r, byte g, byte b, byte a) = Texel(x, y);
                int at = (y * 6 + x) * 4;
                exact &= texels[at] == r && texels[at + 1] == g && texels[at + 2] == b && texels[at + 3] == a;
            }
        }
        await Assert.That(exact).IsTrue();
        float pixelsPerUnit = MemoryMarshal.Read<float>(file.Read(file.Find(TextureImporter.SpriteSection)));
        await Assert.That(pixelsPerUnit).IsEqualTo(32f);
    }

    [Test]
    public async Task ChangingTheUsageReimportsInAnotherFormat()
    {
        using Scratch scratch = new();
        File.WriteAllBytes(scratch.PathOf("bumps.tga"), Gradient(32));
        AssetDatabase database = scratch.Open();
        database.ImportAll();
        string sidecar = scratch.PathOf("bumps.tga.crmeta");
        await Assert.That(File.ReadAllText(sidecar)).Contains("\"Usage\": \"Color\"");
        File.WriteAllText(sidecar, File.ReadAllText(sidecar).Replace("\"Usage\": \"Color\"", "\"Usage\": \"NormalMap\""));
        database.Refresh();
        ImportResult result = database.ImportAll().Single();
        await Assert.That(result.Outcome).IsEqualTo(ImportOutcome.Imported);
        using ImportedFile file = ImportedFile.Open(result.OutputPath!, TextureImporter.Kind, TextureImporter.KindVersion);
        await Assert.That(Info(file).Format).IsEqualTo(TextureFormat.Bc5);
        await Assert.That(Info(file).Srgb).IsEqualTo(0u);
    }

    [Test]
    public async Task HdrImagesDefaultToHdrUsage()
    {
        using Scratch scratch = new();
        // 4x4 of the value 2.0 in Radiance's format (see the native test).
        List<byte> hdr = [.. "#?RADIANCE\nFORMAT=32-bit_rle_rgbe\n\n-Y 4 +X 4\n"u8];
        for (int i = 0; i < 16; ++i)
        {
            hdr.AddRange([128, 128, 128, 130]);
        }
        File.WriteAllBytes(scratch.PathOf("sky.hdr"), [.. hdr]);
        AssetDatabase database = scratch.Open();
        await Assert.That(File.ReadAllText(scratch.PathOf("sky.hdr.crmeta"))).Contains("\"Usage\": \"Hdr\"");
        ImportResult result = database.ImportAll().Single();
        using ImportedFile file = ImportedFile.Open(result.OutputPath!, TextureImporter.Kind, TextureImporter.KindVersion);
        await Assert.That(Info(file).Format).IsEqualTo(TextureFormat.Rgba16F);
    }

    [Test]
    public async Task FailuresCarryTheImportersReason()
    {
        using Scratch scratch = new();
        File.WriteAllBytes(scratch.PathOf("odd.tga"), Gradient(30));
        ImportResult result = scratch.Open().ImportAll().Single();
        await Assert.That(result.Outcome).IsEqualTo(ImportOutcome.Failed);
        await Assert.That(result.Error).Contains("30x30 isn't a multiple of 4");
    }

    // Sprite sheets (ADR 0107): a grid cut left to right, top to bottom,
    // within its margin and between its spacing.
    [Test]
    public async Task AGridCutsCellsLeftToRightTopToBottom()
    {
        TextureImportSettings settings = new() { CellWidth = 16, CellHeight = 8, Margin = 2, Spacing = 1 };
        List<SpriteFrameSettings> frames = TextureImporter.FramesOf(settings, 2 + 16 + 1 + 16 + 1 + 16 + 2, 2 + 8 + 1 + 8 + 3);
        await Assert.That(frames.Count).IsEqualTo(6);
        await Assert.That((frames[1].X, frames[1].Y, frames[1].Width, frames[1].Height)).IsEqualTo((19, 2, 16, 8));
        await Assert.That((frames[3].X, frames[3].Y)).IsEqualTo((2, 11));
        // Without a cell size, the whole texture is one frame.
        frames = TextureImporter.FramesOf(new TextureImportSettings(), 40, 20);
        await Assert.That((frames.Single().Width, frames.Single().Height)).IsEqualTo((40, 20));
        // Rectangles are as listed.
        TextureImportSettings drawn = new() { Slicing = SpriteSlicing.Rectangles, Frames = [new() { X = 3, Y = 4, Width = 5, Height = 6 }] };
        await Assert.That(TextureImporter.FramesOf(drawn, 40, 20).Single().X).IsEqualTo(3);
    }

    private static (AssetDatabase Database, AssetEntry Sheet) SpriteSheet(Scratch scratch,
        Action<TextureImportSettings> configure)
    {
        File.WriteAllBytes(scratch.PathOf("hero.tga"), Tga(32, 16, (x, y) => ((byte)x, (byte)y, 0, 255)));
        AssetDatabase database = scratch.Open();
        database.ImportAll();
        AssetEntry sheet = database.Find("hero.tga")!;
        TextureImportSettings settings = new() { Usage = TextureUsage.Sprite, Mipmaps = false, CellWidth = 8, CellHeight = 8 };
        configure(settings);
        database.SetSettings(sheet.Id, (System.Text.Json.Nodes.JsonObject)System.Text.Json.Nodes.JsonNode.Parse(
            Crucible.Serialization.Json.Serialize(settings))!);
        return (database, sheet);
    }

    [Test]
    public async Task ASheetsClipsBecomeSubAssetsWithTheirFramesRegionsAndEvents()
    {
        using Scratch scratch = new();
        (AssetDatabase database, AssetEntry sheet) = SpriteSheet(scratch, settings => settings.Clips =
        [
            new SpriteClipSettings
            {
                Name = "Walk",
                Frames = [new() { Frame = 1, Duration = 0.1f, Events = ["Step"] }, new() { Frame = 6, Duration = 0.25f }],
            },
            new SpriteClipSettings { Name = "Idle", Loop = false, Frames = [new() { Frame = 0 }] },
        ]);
        ImportResult result = database.ImportAll().Single();
        await Assert.That(result.Outcome).IsEqualTo(ImportOutcome.Imported).Because(result.Error ?? "");
        AssetEntry[] clips = [.. database.SubAssetsOf(sheet.Id)];
        await Assert.That(string.Join(" ", clips.Select(clip => clip.SubAssetName).Order())).IsEqualTo("Idle Walk");
        AssetEntry walk = clips.Single(clip => clip.SubAssetName == "Walk");
        using ImportedFile file = ImportedFile.Open(database.ImportedFile(walk.Id)!, TextureImporter.ClipKind, TextureImporter.ClipVersion);
        SpriteClipInfo info = MemoryMarshal.Read<SpriteClipInfo>(file.Read(file.Find(TextureImporter.ClipInfoSection)));
        await Assert.That((info.Texture, info.FrameCount, info.Loop)).IsEqualTo((sheet.Id, 2u, 1u));
        await Assert.That(info.Duration).IsEqualTo(0.35f).Within(1e-6f);
        SpriteClipFrame[] frames = MemoryMarshal.Cast<byte, SpriteClipFrame>(file.Read(file.Find(TextureImporter.ClipFrameSection))).ToArray();
        // Frame 6 of a 4x2 grid of 8x8 cells: the second row's third.
        await Assert.That((frames[1].X, frames[1].Y, frames[1].Width, frames[1].Start)).IsEqualTo((16u, 8u, 8u, 0.1f));
        ClipEvent[] events = MemoryMarshal.Cast<byte, ClipEvent>(file.Read(file.Find(TextureImporter.ClipEventSection))).ToArray();
        await Assert.That((events.Single().Time, events.Single().Name)).IsEqualTo((0f, Crucible.Animation.AnimationEvent.NameOf("Step")));
    }

    [Test]
    public async Task ClipsNamingFramesTheSheetHasntFailTheImport()
    {
        using Scratch scratch = new();
        (AssetDatabase database, _) = SpriteSheet(scratch, settings => settings.Clips =
            [new SpriteClipSettings { Name = "Walk", Frames = [new() { Frame = 8 }] }]);
        ImportResult result = database.ImportAll().Single();
        await Assert.That(result.Outcome).IsEqualTo(ImportOutcome.Failed);
        await Assert.That(result.Error).Contains("clip Walk: frame 8 isn't one of the sheet's (8)");

        using Scratch other = new();
        (database, _) = SpriteSheet(other, settings =>
        {
            settings.Slicing = SpriteSlicing.Rectangles;
            settings.Frames = [new() { X = 30, Y = 0, Width = 8, Height = 8 }];
            settings.Clips = [new SpriteClipSettings { Name = "Walk", Frames = [new() { Frame = 0 }] }];
        });
        result = database.ImportAll().Single();
        await Assert.That(result.Error).Contains("frame 0 (30, 0, 8x8) isn't within the 32x16 texture");
    }
}
