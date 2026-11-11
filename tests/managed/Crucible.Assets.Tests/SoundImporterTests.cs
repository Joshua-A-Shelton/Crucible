using System.Runtime.InteropServices;
using Crucible.Assets;
using Crucible.Assets.Importers;
using Crucible.Binary;
using Crucible.Importers;

namespace Crucible.Tests;

// The Sound importer (ADR 0083): WAV, FLAC, and OGG into a .crsound, its
// settings in the sidecar.
public class SoundImporterTests
{
    private sealed class Scratch : IDisposable
    {
        public Project Project { get; } =
            Project.Create(Path.Combine(Path.GetTempPath(), "crucible-sounds-" + Guid.NewGuid().ToString("N")), "Game");

        public string Add(string name)
        {
            string sounds = Environment.GetEnvironmentVariable("CRUCIBLE_SOUNDS")
                ?? throw new InvalidOperationException("CRUCIBLE_SOUNDS isn't set: run these tests through CTest");
            string path = Path.Combine(Project.AssetsFolder, "Sounds", name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.Copy(Path.Combine(sounds, name), path);
            return path;
        }

        public AssetDatabase Open() => AssetDatabase.Open(Project, [new SoundImporter()]);

        public void Dispose() => Directory.Delete(Project.Root, recursive: true);
    }

    private static SoundInfo InfoOf(string path, out uint dataTag)
    {
        using ImportedFile file = ImportedFile.Open(path, SoundImporter.Kind, SoundImporter.KindVersion);
        SoundInfo info = MemoryMarshal.Read<SoundInfo>(file.Read(file.Find(SoundImporter.InfoSection)));
        dataTag = file.Section(1).Tag;
        return info;
    }

    [Test]
    public async Task EachFormatImportsAsASound()
    {
        using Scratch scratch = new();
        scratch.Add("tone.wav");
        scratch.Add("tone.flac");
        scratch.Add("tone.ogg");
        AssetDatabase database = scratch.Open();
        ImportResult[] results = [.. database.ImportAll().OrderBy(result => result.Path, StringComparer.Ordinal)];
        await Assert.That(results.Length).IsEqualTo(3);
        await Assert.That(results.All(result => result.Error is null)).IsTrue();
        await Assert.That(results.All(result => result.OutputPath!.EndsWith(SoundImporter.FileExtension))).IsTrue();

        SoundInfo flac = InfoOf(results[0].OutputPath!, out uint flacData);
        await Assert.That(flac.Channels).IsEqualTo(2u);
        await Assert.That(flacData).IsEqualTo(Tag.Of("PCMS"));
        SoundInfo ogg = InfoOf(results[1].OutputPath!, out uint oggData);
        await Assert.That(ogg.Encoding).IsEqualTo(SoundEncoding.Vorbis);
        await Assert.That(oggData).IsEqualTo(Tag.Of("OGGV"));
        SoundInfo wav = InfoOf(results[2].OutputPath!, out _);
        await Assert.That(wav.SampleRate).IsEqualTo(48000u);
        await Assert.That(wav.FrameCount).IsEqualTo(48000u);
    }

    [Test]
    public async Task SidecarSettingsChangeTheImport()
    {
        using Scratch scratch = new();
        string path = scratch.Add("tone.wav");
        File.WriteAllText(path + Sidecar.Extension, """
            {
              "Guid": "2b0c8a3e-6f1d-4c5b-9a7e-1d2c3b4a5f60",
              "Importer": "Sound",
              "Settings": { "Encoding": "Adpcm", "TrimSilence": true, "SilenceThreshold": -40, "BeatsPerMinute": 120 }
            }
            """);
        ImportResult result = scratch.Open().ImportAll().Single();
        await Assert.That(result.Error).IsNull();
        SoundInfo info = InfoOf(result.OutputPath!, out uint data);
        await Assert.That(info.Encoding).IsEqualTo(SoundEncoding.ImaAdpcm);
        await Assert.That(data).IsEqualTo(Tag.Of("ADPC"));
        await Assert.That(info.FrameCount).IsLessThan(25000u); // the silence either side cut
        await Assert.That(info.BeatsPerMinute).IsEqualTo(120f);
    }
}
