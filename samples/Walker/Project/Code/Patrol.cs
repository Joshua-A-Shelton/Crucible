using System.Numerics;
using Crucible.Ecs;
using Crucible.Loop;
using Crucible.Physics2D;
using Crucible.Scenes;
using Crucible.Serialization;

namespace Walker;

// Characters2D.crscene's game code (ADR 0094): a 2D character has no root
// motion, so something has to move it; a Patroller walks back and forth and
// jumps now and then.

/// <summary>Walks its 2D character back and forth and jumps every so often.</summary>
[Component, Serialized]
public partial struct Patroller
{
    [Range(0, 10), Tooltip("Meters a second.")]
    public float Speed;

    [Range(0, 30), Tooltip("Meters it walks each way, centered where it starts.")]
    public float Distance;

    [Range(0, 10), Tooltip("Seconds between jumps; 0 never jumps.")]
    public float Interval;

    public Patroller()
    {
        Speed = 1.5f;
        Distance = 6;
        Interval = 3;
    }
}

/// <summary>Moves every Patroller's character each frame (gravity included), turning at the ends of its walk or at a wall.</summary>
[System(Phases.Update)]
public sealed class PatrolSystem : GameSystem
{
    private const float JumpSpeed = 5;

    private Query? _patrols;
    private readonly Dictionary<Entity, (float Start, float Direction, float Clock)> _states = [];

    public override void Start(FrameContext context) =>
        _patrols = context.World.CreateQuery([Components.Of<Patroller>(), Components.Of<CharacterController2D>(),
            Components.Of<Transform2D>()]);

    public override void Run(FrameContext context)
    {
        Gather gather = new() { Found = [] };
        _patrols?.ForEachChunk(ref gather);
        World world = context.World;
        float seconds = (float)context.DeltaSeconds;
        foreach (Entity entity in gather.Found)
        {
            Patroller patrol = world.Read<Patroller>(entity);
            float x = world.Read<Transform2D>(entity).Position.X;
            if (!_states.TryGetValue(entity, out var state))
            {
                state = (x, 1, 0);
            }
            if ((x - state.Start) * state.Direction > patrol.Distance / 2)
            {
                state.Direction = -state.Direction;
            }
            state.Clock += seconds;
            if (patrol.Interval > 0 && state.Clock >= patrol.Interval)
            {
                state.Clock = 0;
                CharacterController2D.Launch(world, entity, JumpSpeed);
            }
            float step = state.Direction * patrol.Speed * seconds;
            CharacterMove2D move = CharacterController2D.Move(world, entity, new Vector2(step, 0), seconds);
            if (MathF.Abs(move.Position.X - x) < MathF.Abs(step) / 4)
            {
                state.Direction = -state.Direction; // a wall too high to step over
            }
            _states[entity] = state;
        }
    }

    public override void Stop(FrameContext context)
    {
        _patrols?.Dispose();
        _states.Clear();
    }

    private struct Gather : IChunkJob
    {
        public List<Entity> Found;

        public readonly void Execute(in ChunkView chunk) => Found.AddRange(chunk.Entities);
    }
}
