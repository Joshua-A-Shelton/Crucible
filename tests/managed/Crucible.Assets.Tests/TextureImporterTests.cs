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
        await Assert.That(file.SectionCount).IsEqualTo(8); // INFO and seven mips
        await Assert.That(file.Section(file.Find(TextureImporter.MipSection(0))).Size).IsEqualTo(16ul * 16 * 16);
        await Assert.That(file.Section(file.Find(TextureImporter.MipSection(6))).Size).IsEqualTo(16ul);
        await Assert.That(Tag.Of("MIPA")).IsEqualTo(TextureImporter.MipSection(10));
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
}
