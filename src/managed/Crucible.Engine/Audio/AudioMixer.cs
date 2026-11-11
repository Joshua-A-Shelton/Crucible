using Crucible.Assets;

namespace Crucible.Audio;

/// <summary>
/// Crucible's mixer (ADR 0082): the scheduling delay, what it's doing, and
/// sounds made in code. The contract's calls (one-shots, buses, music) are
/// <see cref="Api"/>'s and work with any audio implementation.
/// </summary>
public static class AudioMixer
{
    // As crucible/Audio.h has them.
    public const int MixRate = 48000;
    public const int MaxVoices = 128;
    public const uint MasterBus = 0;
    public const uint MusicBus = 1;
    public const uint ReverbBus = 15;
    public const uint NoBus = 0xFFFFFFFF;

    /// <summary>
    /// Seconds after its timestamp a scheduled sound starts (50 ms by
    /// default): long enough that every sound is late by the same amount.
    /// </summary>
    public static float ScheduleDelay
    {
        get => Api.ScheduleDelay();
        set => Api.SetScheduleDelay(value);
    }

    /// <summary>Voices playing, late starts, estimated dropouts and latency.</summary>
    public static MixerStats Stats => Api.MixerStats();

    /// <summary>
    /// A sound made in code: <paramref name="samples"/> interleaved by
    /// channel, playable by its ID like an imported one.
    /// </summary>
    public static AssetId CreateSound(SoundInfo info, ReadOnlySpan<short> samples) => Api.CreateSound(info, samples);

    /// <summary>Frees a sound made in code, once nothing plays it.</summary>
    public static void DestroySound(AssetId sound) => Api.DestroySound(sound);
}
