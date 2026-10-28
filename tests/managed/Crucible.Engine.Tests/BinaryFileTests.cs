using Crucible.Binary;

namespace Crucible.Tests;

// Crucible's binary files from C# (ADR 0039), under JIT and NativeAOT.
// Writing compresses on the native job system, which other classes start and
// stop around themselves, so these don't run alongside them.
[NotInParallel("jobs")]
public class BinaryFileTests
{
    private static readonly uint _kind = Tag.Of("TEST");

    private static (byte[] Pattern, byte[] Noise) Contents()
    {
        byte[] pattern = new byte[200_000];
        for (int i = 0; i < pattern.Length; ++i)
        {
            pattern[i] = (byte)(i * 7 % 61);
        }
        byte[] noise = new byte[10_000];
        new Random(5).NextBytes(noise);
        return (pattern, noise);
    }

    private static string WriteSample(string name, Compression compression, uint kindVersion = 2)
    {
        (byte[] pattern, byte[] noise) = Contents();
        string path = Path.Combine(Path.GetTempPath(), $"crucible-{name}-{Environment.ProcessId}.bin");
        using ImportedFileWriter writer = new(_kind, kindVersion);
        writer.Add(Tag.Of("PATT"), 1, pattern).Add(Tag.Of("NOIS"), 1, noise, 256).Write(path, compression);
        return path;
    }

    [Test]
    public async Task WrittenFilesReadBack()
    {
        (byte[] pattern, byte[] noise) = Contents();
        string path = WriteSample("roundtrip", Compression.Fast);
        try
        {
            await Assert.That(new FileInfo(path).Length).IsLessThan(pattern.Length / 4 + noise.Length + 4096);
            using ImportedFile file = ImportedFile.Open(path, _kind, 2);
            await Assert.That(file.SectionCount).IsEqualTo(2);
            int patternIndex = file.Find(Tag.Of("PATT"));
            SectionInfo info = file.Section(patternIndex);
            await Assert.That(info.Codec).IsEqualTo(Codec.Zstd);
            await Assert.That(info.Size).IsEqualTo((ulong)pattern.Length);
            await Assert.That(file.Read(patternIndex).AsSpan().SequenceEqual(pattern)).IsTrue();
            await Assert.That(file.Read(file.Find(Tag.Of("NOIS"))).AsSpan().SequenceEqual(noise)).IsTrue();
            await Assert.That(file.Find(Tag.Of("NONE"))).IsEqualTo(-1);

            // The same bytes parse from memory.
            using ImportedFile parsed = ImportedFile.Parse(File.ReadAllBytes(path), _kind, 2);
            await Assert.That(parsed.Read(0).AsSpan().SequenceEqual(pattern)).IsTrue();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task StaleAndDamagedFilesThrowWithTheirStatus()
    {
        string path = WriteSample("damage", Compression.Fast);
        try
        {
            CrucibleException stale = Assert.Throws<CrucibleException>(() => ImportedFile.Open(path, _kind, 3));
            await Assert.That(stale.Status).IsEqualTo(Status.Unsupported);

            byte[] bytes = File.ReadAllBytes(path);
            bytes[^100] ^= 0x10; // inside the last section's payload
            using ImportedFile damaged = ImportedFile.Parse(bytes, _kind, 2);
            CrucibleException read = Assert.Throws<CrucibleException>(() => damaged.Read(1));
            await Assert.That(read.Status).IsEqualTo(Status.InvalidData);

            CrucibleException missing =
                Assert.Throws<CrucibleException>(() => ImportedFile.Open(path + ".missing", _kind, 2));
            await Assert.That(missing.Status).IsEqualTo(Status.NotFound);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task TagsAreTheLettersInFileOrder()
    {
        await Assert.That(Tag.Of("CRBN")).IsEqualTo(0x4E425243u);
        await Assert.That(Assert.Throws<ArgumentException>(() => Tag.Of("TOOLONG"))).IsNotNull();
    }
}
