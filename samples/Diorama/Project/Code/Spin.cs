using System.Numerics;
using Crucible.Ecs;
using Crucible.Loop;
using Crucible.Scenes;
using Crucible.Serialization;

namespace Diorama;

/// <summary>
/// Turns an entity around Y (ADR 0072): <see cref="Start"/> radians at the
/// first frame, <see cref="PerFrame"/> more each frame, so every run shows
/// the same turn at the same frame.
/// </summary>
[Component, Serialized]
public partial struct Spin
{
    [Tooltip("The turn at the first frame, in radians.")]
    public float Start;

    [Range(-0.5f, 0.5f), Tooltip("Radians more each frame.")]
    public float PerFrame;
}

/// <summary>Turns every entity with a <see cref="Spin"/>.</summary>
[System(Phases.Update)]
public sealed class SpinSystem : GameSystem
{
    private int _frames;
    private Query? _spinning;

    public override void Start(FrameContext context)
    {
        _frames = 0;
        _spinning = context.World.CreateQuery([Components.Of<Spin>(), Components.Of<LocalTransform>()]);
    }

    public override void Run(FrameContext context)
    {
        ++_frames;
        Turn job = new() { Frames = _frames };
        _spinning?.ForEachChunk(ref job);
    }

    public override void Stop(FrameContext context)
    {
        _spinning?.Dispose();
        _spinning = null;
    }

    private struct Turn : IChunkJob
    {
        public int Frames;

        public readonly void Execute(in ChunkView chunk)
        {
            Span<LocalTransform> transforms = chunk.Write<LocalTransform>();
            ReadOnlySpan<Spin> spins = chunk.Read<Spin>();
            for (int i = 0; i < transforms.Length; ++i)
            {
                transforms[i].Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, spins[i].Start + spins[i].PerFrame * Frames);
            }
        }
    }
}
