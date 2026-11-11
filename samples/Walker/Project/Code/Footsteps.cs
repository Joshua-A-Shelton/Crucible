using System.Numerics;
using Crucible.Animation;
using Crucible.Assets;
using Crucible.Audio;
using Crucible.Ecs;
using Crucible.Loop;
using Crucible.Scenes;
using Crucible.Serialization;
using AudioApi = Crucible.Audio.Api;

namespace Walker;

// The M8 demonstration's game code (ADR 0084): the Walker's footsteps,
// each scheduled at the moment its foot was planted, and its walk turned
// into a circle.

/// <summary>A footstep sound for each foot the Walker plants (its LeftFoot and RightFoot events).</summary>
[Component, Serialized]
public partial struct Footsteps
{
    [Tooltip("The footstep sound.")]
    [AssetKind(".crsound")]
    public AssetId Sound;

    [Range(0, 2)]
    public float Volume;
}

/// <summary>Turns its entity as root motion carries it, so it walks round a circle.</summary>
[Component, Serialized]
public partial struct Circle
{
    [Range(0.5f, 20), Tooltip("Metres.")]
    public float Radius;
}

/// <summary>
/// Plays a footstep at the planted foot for every LeftFoot and RightFoot
/// event, at the event's own time, so the steps are as even as the walk
/// whatever the frame rate. With <c>--game-arg check=1</c> (headless) it
/// mixes offline and checks each step's onset against its time; with
/// <c>seconds=N</c> it quits after N seconds.
/// </summary>
[System(Phases.LateUpdate)]
public sealed class FootstepSystem : GameSystem
{
    private readonly AnimationEvent[] _events = new AnimationEvent[64];
    private readonly uint _left = AnimationEvent.NameOf("LeftFoot");
    private readonly uint _right = AnimationEvent.NameOf("RightFoot");
    private bool _check;
    private double _seconds;
    private ulong _start;
    private readonly List<float> _mixed = [];
    private readonly List<ulong> _steps = [];
    private ulong _lastFrame;
    private int _clamped;

    public override void Start(FrameContext context)
    {
        _check = context.GameArguments.GetValueOrDefault("check") == "1";
        _seconds = double.Parse(context.GameArguments.GetValueOrDefault("seconds") ?? "0",
            System.Globalization.CultureInfo.InvariantCulture);
        // Now, not the frame's start: Start runs late in the first frame.
        _start = Crucible.Platform.Api.TimestampNow();
        if (_check)
        {
            AudioApi.StartOfflineMixing(_start);
        }
    }

    public override void Run(FrameContext context)
    {
        // Frames whose delta the loop clamped (ADR 0036): longer than four
        // ticks, their extra time is dropped by design, moving later events.
        if (_lastFrame != 0 && context.FrameStartNs - _lastFrame > context.DeltaSeconds * 1e9 + 1e6)
        {
            ++_clamped;
        }
        _lastFrame = context.FrameStartNs;
        World world = context.World;
        int count = Math.Min(Animator.Events(world, _events), _events.Length);
        for (int i = 0; i < count; ++i)
        {
            AnimationEvent step = _events[i];
            if (step.Kind != AnimationEventKind.Event || (step.Name != _left && step.Name != _right) ||
                !world.Has(step.Entity, Components.Of<Footsteps>()))
            {
                continue;
            }
            Footsteps footsteps = world.Read<Footsteps>(step.Entity);
            int joint = Animator.FindJoint(world, step.Entity, step.Name == _left ? "LeftFoot" : "RightFoot");
            Vector3 foot = Animator.JointModelTransform(world, step.Entity, joint).Translation;
            Vector3 at = world.Has(step.Entity, Components.Of<WorldTransform>())
                ? Vector3.Transform(foot, world.Read<WorldTransform>(step.Entity).Matrix)
                : foot;
            AudioApi.PlayOneShot(footsteps.Sound, step.Timestamp, at, footsteps.Volume, AudioMixer.MasterBus);
            _steps.Add(step.Timestamp);
        }

        double elapsed = (Crucible.Platform.Api.TimestampNow() - _start) / 1e9;
        if (_check)
        {
            // Offline, as far as now, as a device would ask.
            long target = (long)Math.Round(elapsed * AudioMixer.MixRate);
            long done = _mixed.Count / 2;
            if (target > done)
            {
                float[] block = new float[(target - done) * 2];
                AudioApi.MixOffline(block);
                _mixed.AddRange(block);
            }
        }
        if (_seconds > 0 && elapsed >= _seconds)
        {
            context.QuitRequested = true;
        }
    }

    public override void Stop(FrameContext context)
    {
        if (!_check)
        {
            return;
        }
        // Mix on past the last step, then find each onset after its time.
        float[] tail = new float[AudioMixer.MixRate / 2 * 2];
        AudioApi.MixOffline(tail);
        _mixed.AddRange(tail);
        long worst = 0;
        int heard = 0;
        foreach (ulong step in _steps)
        {
            long expected = (long)Math.Round((step - _start + AudioMixer.ScheduleDelay * 1e9) * AudioMixer.MixRate / 1e9);
            long onset = Onset(Math.Max(expected - AudioMixer.MixRate / 10, 0));
            if (onset >= 0)
            {
                ++heard;
                worst = Math.Max(worst, Math.Abs(onset - expected));
            }
        }
        MixerStats stats = AudioMixer.Stats;
        string times = string.Join(",", _steps.Select(step => ((step - _steps[0]) / 1e6).ToString("0.000",
            System.Globalization.CultureInfo.InvariantCulture)));
        Console.WriteLine($"Crucible.Walker: {heard} of {_steps.Count} footsteps heard, at most {worst} frames " +
                          $"from their times, {stats.LateStarts} late; steps at ms {times}; {_clamped} frames clamped");
        AudioApi.StopMixing();
    }

    // The first frame from `from` loud enough to be a step starting.
    private long Onset(long from)
    {
        for (long frame = from; frame < _mixed.Count / 2; ++frame)
        {
            if (Math.Abs(_mixed[(int)frame * 2]) > 0.002f || Math.Abs(_mixed[(int)frame * 2 + 1]) > 0.002f)
            {
                return frame;
            }
        }
        return -1;
    }
}

/// <summary>Turns each walker with a <see cref="Circle"/> by the distance root motion moved it over the radius.</summary>
[System(Phases.LateUpdate)]
public sealed class CircleSystem : GameSystem
{
    private Query? _circling;

    public override void Start(FrameContext context) =>
        _circling = context.World.CreateQuery([Components.Of<Circle>(), Components.Of<LocalTransform>()]);

    public override void Run(FrameContext context)
    {
        Turn job = new() { World = context.World };
        _circling?.ForEachChunk(ref job);
    }

    private struct Turn : IChunkJob
    {
        public World World;

        public readonly void Execute(in ChunkView chunk)
        {
            ReadOnlySpan<Entity> entities = chunk.Entities;
            ReadOnlySpan<Circle> circles = chunk.Read<Circle>();
            Span<LocalTransform> transforms = chunk.Write<LocalTransform>();
            for (int i = 0; i < entities.Length; ++i)
            {
                float moved = Animator.LastRootMotion(World, entities[i]).Translation.Length();
                float turn = moved / MathF.Max(circles[i].Radius, 0.1f);
                transforms[i].Rotation = Quaternion.Normalize(
                    Quaternion.Concatenate(Quaternion.CreateFromAxisAngle(Vector3.UnitY, turn), transforms[i].Rotation));
            }
        }
    }

    public override void Stop(FrameContext context) => _circling?.Dispose();
}
