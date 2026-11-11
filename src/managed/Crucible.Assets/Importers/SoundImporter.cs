using System.Runtime.InteropServices;
using Crucible.Binary;
using Crucible.Importers;
using Crucible.Serialization;
using ImportersApi = Crucible.Importers.Api;

namespace Crucible.Assets.Importers;

/// <summary>A sound's import settings, kept in its sidecar (ADR 0083).</summary>
[Serialized]
public partial class SoundImportSettings
{
    /// <summary>
    /// How it's stored: Auto keeps an .ogg as Vorbis and makes WAV and FLAC
    /// PCM, or ADPCM past <see cref="AdpcmAfterSeconds"/>.
    /// </summary>
    public SoundImportEncoding Encoding = SoundImportEncoding.Auto;

    public float AdpcmAfterSeconds = 5;

    /// <summary>Music's tempo, so a crossfade can wait for a beat or a bar; 0 for none.</summary>
    public float BeatsPerMinute;

    public int BeatsPerBar = 4;

    /// <summary>Mix stereo down to one channel (spatial sounds are panned as mono anyway).</summary>
    public bool Mono;

    /// <summary>Scale it so its loudest sample reaches <see cref="NormalizePeak"/> (dBFS).</summary>
    public bool Normalize;

    public float NormalizePeak = -1;

    /// <summary>Cut what's quieter than <see cref="SilenceThreshold"/> (dBFS) from both ends.</summary>
    public bool TrimSilence;

    public float SilenceThreshold = -60;
}

/// <summary>
/// Imports WAV, FLAC, and Ogg Vorbis files as sounds (ADR 0083): the native
/// importer decodes and processes them, and this writes a <c>SOND</c>
/// binary file with an <c>INFO</c> section and the data (<c>PCMS</c>,
/// <c>ADPC</c>, or <c>OGGV</c>), as SoundFile.h lays it out.
/// </summary>
public sealed class SoundImporter : Importer<SoundImportSettings>
{
    public static readonly uint Kind = Tag.Of("SOND");
    public static readonly uint InfoSection = Tag.Of("INFO");
    public const uint KindVersion = 1; // as soundFileVersion
    public const string FileExtension = ".crsound";

    public override string Name => "Sound";

    public override int Version => 1;

    public override IReadOnlyList<string> Extensions => [".wav", ".flac", ".ogg"];

    public override string OutputExtension => FileExtension;

    public override void Import(ImportContext context, SoundImportSettings settings)
    {
        SoundSettings native = new()
        {
            Encoding = settings.Encoding,
            AdpcmAfterSeconds = Math.Max(settings.AdpcmAfterSeconds, 0),
            BeatsPerMinute = Math.Max(settings.BeatsPerMinute, 0),
            BeatsPerBar = (uint)Math.Max(settings.BeatsPerBar, 1),
            Mono = settings.Mono,
            Normalize = settings.Normalize,
            NormalizePeak = Math.Min(settings.NormalizePeak, 0),
            TrimSilence = settings.TrimSilence,
            SilenceThreshold = settings.SilenceThreshold,
        };
        SoundImportHandle sound = MeshImporter.Native(() => ImportersApi.ImportSound(context.ReadSource(), native));
        try
        {
            ImportedFileWriter output = context.CreateOutput(Kind, KindVersion);
            SoundInfo info = ImportersApi.SoundImportInfo(sound);
            output.Add(InfoSection, 1, MemoryMarshal.AsBytes(new Span<SoundInfo>(ref info)));
            byte[] data = new byte[ImportersApi.SoundDataSize(sound)];
            ImportersApi.CopySoundData(sound, data);
            output.Add(ImportersApi.SoundDataTag(sound), 1, data);
            for (uint i = 0; i < ImportersApi.SoundWarningCount(sound); ++i)
            {
                context.Warn(ImportersApi.SoundWarning(sound, i));
            }
        }
        finally
        {
            ImportersApi.DestroySoundImport(sound);
        }
    }
}
