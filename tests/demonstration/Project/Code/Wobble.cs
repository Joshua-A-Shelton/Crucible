using System.Numerics;
using Crucible.Ecs;
using Crucible.Loop;
using Crucible.Scenes;
using Crucible.Serialization;

namespace Demo;

// The M7 demonstration's game code (ADR 0077), copied into a new project:
// a component and a system, edited while playing to show hot reload.

/// <summary>Rocks its entity around Z, <see cref="Amount"/> radians each way, once every <see cref="Frames"/> frames.</summary>
[Component, Serialized]
public partial struct Wobble
{
    [Range(0, 1), Tooltip("Radians each way.")]
    public float Amount;

    [Range(1, 240), Tooltip("Frames for one rock.")]
    public float Frames;
}

/// <summary>Rocks every entity with a <see cref="Wobble"/>, by frame, so every run rocks alike.</summary>
[System(Phases.Update)]
public sealed class WobbleSystem : GameSystem
{
    private int _frame;
    private Query? _wobbling;

    public override void Start(FrameContext context)
    {
        _frame = 0;
        _wobbling = context.World.CreateQuery([Components.Of<Wobble>(), Components.Of<LocalTransform>()]);
    }

    public override void Run(FrameContext context)
    {
        ++_frame;
        Rock job = new() { Frame = _frame, Scale = 1f };
        _wobbling?.ForEachChunk(ref job);
    }

    public override void Stop(FrameContext context)
    {
        _wobbling?.Dispose();
        _wobbling = null;
    }

    private struct Rock : IChunkJob
    {
        public int Frame;
        public float Scale;

        public readonly void Execute(in ChunkView chunk)
        {
            Span<LocalTransform> transforms = chunk.Write<LocalTransform>();
            ReadOnlySpan<Wobble> wobbles = chunk.Read<Wobble>();
            for (int i = 0; i < transforms.Length; ++i)
            {
                float angle = wobbles[i].Amount * Scale * MathF.Sin(MathF.Tau * Frame / MathF.Max(wobbles[i].Frames, 1));
                transforms[i].Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, angle);
            }
        }
    }
}
