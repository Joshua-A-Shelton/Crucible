using System.Numerics;
using Crucible.Assets;
using Crucible.Audio;
using Crucible.Ecs;
using Crucible.Loop;
using Crucible.Scenes;
using AudioApi = Crucible.Audio.Api;

namespace Crucible.Samples.Metronome;

/// <summary>
/// Crucible's mixer, by ear (ADR 0082). Every sound is made in code:
/// <list type="bullet">
/// <item>a click every half second, louder on each bar's first beat, each
///   scheduled at its exact time (as footsteps will be), so the beat is even
///   whatever the frame rate;</item>
/// <item>a tone circling the listener, heard moving between left and right;</item>
/// <item>its bus low-passed for four seconds, then sent to the reverb for four;</item>
/// <item>music at 120 beats a minute, switching to the other chord every
///   eight seconds, on the bar, with a one-second crossfade.</item>
/// </list>
/// Options (<c>--game-arg key=value</c>): <c>seconds</c> (0, until closed)
/// and <c>check</c> (1: mix offline, clicks only, and check their spacing to
/// the frame; for a headless run).
/// </summary>
[System(Phases.Update)]
public sealed class MetronomeSystem : GameSystem
{
    private const int Rate = AudioMixer.MixRate;
    private const uint ToneBus = 2;
    private const ulong Beat = 500_000_000; // ns: 120 beats a minute

    private double _seconds;
    private bool _check;
    private ulong _start;
    private ulong _nextClick;
    private int _beat;
    private int _phase = -1; // which four-second stretch the effects are in
    private bool _secondChord;
    private Entity _tone;
    private readonly List<AssetId> _sounds = [];
    private readonly List<float> _mixed = [];
    private long _framesMixed;
    private readonly List<long> _expected = [];

    public override void Start(FrameContext context)
    {
        foreach ((string key, string value) in context.GameArguments)
        {
            switch (key)
            {
            case "seconds":
                _seconds = double.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                break;
            case "check":
                _check = value == "1";
                break;
            default:
                throw new ArgumentException($"Unknown option {key}: expected seconds or check.");
            }
        }
        // Now, not the frame's start: Start runs late in the first frame,
        // after the game has loaded, and clicks timed from the frame's start
        // would already be due.
        _start = Crucible.Platform.Api.TimestampNow();
        _nextClick = _start + 200_000_000;
        if (_check)
        {
            AudioApi.StartOfflineMixing(_start);
        }

        AssetId tone = Make(Tone(220, 1.0, 0.3f));
        Make(Click(1000, 0.6f)); // [1] the bar's first beat
        Make(Click(2000, 0.4f)); // [2] the others
        AssetId first = Make(Chord([220, 277.18, 329.63], 0.12f), tempo: 120);
        Make(Chord([196, 246.94, 293.66], 0.12f), tempo: 120); // [4]

        World world = context.World;
        Entity listener = world.CreateEntity([Components.Of<AudioListener>(), Components.Of<LocalTransform>(),
            Components.Of<WorldTransform>()]);
        world.Set(listener, LocalTransform.Identity);
        _tone = world.CreateEntity([Components.Of<AudioSource>(), Components.Of<LocalTransform>(),
            Components.Of<WorldTransform>()]);
        world.Set(_tone, LocalTransform.Identity);
        world.Set(_tone, new AudioSource
        {
            Clip = tone, Loop = true, PlayOnStart = true, Bus = ToneBus, MinDistance = 2, MaxDistance = 40,
        });

        if (_check)
        {
            // The clicks alone, so their onsets are easy to find.
            AudioApi.SetBusVolume(ToneBus, 0, 0);
            AudioApi.SetBusVolume(AudioMixer.MusicBus, 0, 0);
        }
        AudioApi.PlayMusic(first, new MusicTransition { CrossfadeSeconds = 0, Align = MusicAlign.Now });
        Console.WriteLine("Crucible.Metronome: clicks every half second, a tone circling you, music on the bar" +
                          (_check ? " (offline check)" : ""));
    }

    public override void Run(FrameContext context)
    {
        ulong now = context.FrameStartNs;
        double elapsed = (now - _start) / 1e9;

        // Clicks due by now, each at its own time: it plays that long plus
        // the schedule delay later, so the beat is exact.
        while (_nextClick <= now)
        {
            AssetId click = _sounds[_beat % 4 == 0 ? 1 : 2];
            AudioApi.PlayOneShot(click, _nextClick, Vector3.Zero, 1, AudioMixer.MasterBus);
            _expected.Add((long)Math.Round((_nextClick - _start + AudioMixer.ScheduleDelay * 1e9) * Rate / 1e9));
            _nextClick += Beat;
            ++_beat;
        }

        // The tone circles the listener every eight seconds, 4 m away.
        float angle = (float)(elapsed / 8 * Math.Tau);
        context.World.Set(_tone, LocalTransform.Identity with
        {
            Position = new Vector3(MathF.Sin(angle) * 4, 0, MathF.Cos(angle) * 4),
        });

        // Low-pass for four seconds, then the reverb for four.
        int phase = (int)(elapsed / 4);
        if (phase != _phase)
        {
            _phase = phase;
            bool muffled = phase % 2 == 0;
            AudioApi.SetBusEffects(ToneBus, new BusEffects
            {
                LowPassHz = muffled ? 500 : 0, ReverbSend = muffled ? 0 : 0.6f, DuckedBy = AudioMixer.NoBus,
            });
            if (!_check)
            {
                Console.WriteLine($"Crucible.Metronome: the tone is {(muffled ? "low-passed" : "in the reverb")}");
            }
            // Every eight seconds the other chord, from the next bar.
            if (phase > 0 && phase % 2 == 0)
            {
                _secondChord = !_secondChord;
                AudioApi.PlayMusic(_sounds[_secondChord ? 4 : 3],
                    new MusicTransition { CrossfadeSeconds = 1, Align = MusicAlign.Bar });
            }
        }

        if (_check)
        {
            // Offline, as far as the frame has come, as a device would ask.
            long target = (long)Math.Round(elapsed * Rate);
            if (target > _framesMixed)
            {
                float[] block = new float[(target - _framesMixed) * 2];
                AudioApi.MixOffline(block);
                _mixed.AddRange(block);
                _framesMixed = target;
            }
        }

        if (_seconds > 0 && elapsed >= _seconds)
        {
            context.QuitRequested = true;
        }
    }

    public override void Stop(FrameContext context)
    {
        if (_check)
        {
            Report();
            AudioApi.StopMixing();
        }
        else
        {
            MixerStats stats = AudioMixer.Stats;
            Console.WriteLine($"Crucible.Metronome: {stats.LateStarts} late starts, " +
                              $"about {stats.Underruns} dropouts (estimated)");
        }
        foreach (AssetId sound in _sounds)
        {
            AudioMixer.DestroySound(sound);
        }
    }

    // Each click's onset against the frame it was scheduled for.
    private void Report()
    {
        List<long> onsets = [];
        for (long frame = 0; frame < _mixed.Count / 2; ++frame)
        {
            if (Math.Abs(_mixed[(int)frame * 2]) > 0.05f && (onsets.Count == 0 || frame - onsets[^1] > Rate / 4))
            {
                onsets.Add(frame);
            }
        }
        int heard = Math.Min(onsets.Count, _expected.Count);
        long worst = 0;
        for (int i = 0; i < heard; ++i)
        {
            worst = Math.Max(worst, Math.Abs(onsets[i] - _expected[i]));
        }
        MixerStats stats = AudioMixer.Stats;
        Console.WriteLine($"Crucible.Metronome: {heard} clicks, at most {worst} frames from their times, " +
                          $"{stats.LateStarts} late");
    }

    private AssetId Make(short[] samples, float tempo = 0)
    {
        AssetId id = AudioMixer.CreateSound(new SoundInfo
        {
            SampleRate = Rate, Channels = 1, FrameCount = (uint)samples.Length, Encoding = SoundEncoding.Pcm16,
            BeatsPerMinute = tempo, BeatsPerBar = 4,
        }, samples);
        _sounds.Add(id);
        return id;
    }

    // A click: a tone dying away over 30 ms, starting at full level.
    private static short[] Click(double hertz, float level)
    {
        short[] samples = new short[Rate * 30 / 1000];
        for (int i = 0; i < samples.Length; ++i)
        {
            double t = (double)i / Rate;
            samples[i] = (short)(level * 32767 * Math.Exp(-t / 0.006) * Math.Cos(Math.Tau * hertz * t));
        }
        return samples;
    }

    // A soft sawtooth, a whole number of cycles so it loops without a click.
    private static short[] Tone(double hertz, double seconds, float level)
    {
        short[] samples = new short[(int)(Rate * seconds)];
        for (int i = 0; i < samples.Length; ++i)
        {
            double cycle = hertz * i / Rate % 1.0;
            samples[i] = (short)(level * 32767 * (2 * cycle - 1) * 0.6);
        }
        return samples;
    }

    // A chord of sines, one bar long (two seconds at 120), looping cleanly.
    private static short[] Chord(double[] hertz, float level)
    {
        short[] samples = new short[Rate * 2];
        for (int i = 0; i < samples.Length; ++i)
        {
            double t = (double)i / Rate;
            double sum = hertz.Sum(h => Math.Sin(Math.Tau * Math.Round(h * 2) / 2 * t));
            samples[i] = (short)(level * 32767 * sum / hertz.Length * 2);
        }
        return samples;
    }
}
