using System.Numerics;
using Crucible.Animation;
using Crucible.Diagnostics;
using Crucible.Ecs;
using Crucible.Loop;
using Crucible.Scenes;
using Crucible.Serialization;

namespace Walker;

// The M9 demonstration's game code (ADR 0097): the Walker follows a route of
// waypoints round the level, root motion carrying it and this only turning
// it, and stops at the last.

/// <summary>A point on a route; <see cref="Next"/> is where to go after it (none: the route ends here).</summary>
[Component, Serialized]
public partial struct Waypoint
{
    [EntityWith(typeof(Waypoint)), Tooltip("The waypoint after this one; none ends the route.")]
    public Entity Next;
}

/// <summary>Turns its walking character toward each waypoint of a route in turn.</summary>
[Component, Serialized]
public partial struct RouteFollower
{
    [EntityWith(typeof(Waypoint)), Tooltip("The route's first waypoint.")]
    public Entity First;

    [Range(0.1f, 5), Tooltip("Metres, along the ground: this close, a waypoint is reached.")]
    public float Reach;

    [Range(0.1f, 10), Tooltip("Radians a second, at most.")]
    public float TurnRate;

    public RouteFollower()
    {
        Reach = 0.6f;
        TurnRate = 2;
    }
}

/// <summary>
/// While a <see cref="RouteFollower"/>'s Animator is in its Walk state, turns
/// it toward its waypoint, at most TurnRate, and takes the next waypoint
/// within Reach; at the last it sets the Animator's Arrived. On Stop it
/// reports how far each got, in the terminal, for the check (LevelChecks).
/// </summary>
[System(Phases.LateUpdate)]
public sealed class RouteSystem : GameSystem
{
    private readonly Dictionary<Entity, Progress> _progress = [];
    private readonly List<Entity> _followers = [];
    private Query? _query;
    private ulong _start;
    private ulong _lastFrame;
    private int _clamped;

    private sealed class Progress
    {
        public Entity Target;
        public int Reached;
        public int Count;
        public double ArrivedAt = -1;
        public float Lowest = float.MaxValue;
    }

    public override void Start(FrameContext context)
    {
        _progress.Clear();
        _query = context.World.CreateQuery([Components.Of<RouteFollower>(), Components.Of<LocalTransform>()]);
        _start = Crucible.Platform.Api.TimestampNow();
        _lastFrame = 0;
        _clamped = 0;
    }

    public override void Run(FrameContext context)
    {
        // Frames whose delta the loop clamped (ADR 0036) drop time, moving
        // later animation: the check runs such a run again.
        if (_lastFrame != 0 && context.FrameStartNs - _lastFrame > context.DeltaSeconds * 1e9 + 1e6)
        {
            ++_clamped;
        }
        _lastFrame = context.FrameStartNs;

        World world = context.World;
        _followers.Clear();
        Collect collect = new() { Found = _followers };
        _query?.ForEachChunk(ref collect);
        foreach (Entity entity in _followers)
        {
            RouteFollower follower = world.Read<RouteFollower>(entity);
            if (!_progress.TryGetValue(entity, out Progress? progress))
            {
                progress = new() { Target = follower.First, Count = Length(world, follower.First) };
                _progress[entity] = progress;
            }
            ref LocalTransform transform = ref world.Get<LocalTransform>(entity);
            progress.Lowest = MathF.Min(progress.Lowest, transform.Position.Y);
            if (progress.Target.Value == 0 || !world.Has(progress.Target, Components.Of<Waypoint>()))
            {
                continue;
            }

            Vector3 at = world.ComputeWorldTransform(progress.Target).Translation;
            Vector2 to = new(at.X - transform.Position.X, at.Z - transform.Position.Z);
            if (to.Length() <= follower.Reach)
            {
                ++progress.Reached;
                progress.Target = world.Read<Waypoint>(progress.Target).Next;
                if (progress.Target.Value == 0)
                {
                    progress.ArrivedAt = (Crucible.Platform.Api.TimestampNow() - _start) * 1e-9;
                    Animator.SetBool(world, entity, "Arrived", true);
                }
                continue;
            }
            if (Animator.CurrentState(world, entity) != Animator.FindState(world, entity, "Walk"))
            {
                continue;
            }
            // Yaw about up, from +Z toward +X, as the rotation turns +Z.
            Vector3 forward = Vector3.Transform(Vector3.UnitZ, transform.Rotation);
            float facing = MathF.Atan2(forward.X, forward.Z);
            float wanted = MathF.Atan2(to.X, to.Y);
            float turn = MathF.IEEERemainder(wanted - facing, 2 * MathF.PI);
            float most = follower.TurnRate * (float)context.DeltaSeconds;
            transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, facing + Math.Clamp(turn, -most, most));
        }
    }

    public override void Stop(FrameContext context)
    {
        World world = context.World;
        foreach ((Entity entity, Progress progress) in _progress)
        {
            Log.Info(FormattableString.Invariant(
                $"Walker.Route: {NameOf(world, entity)} reached {progress.Reached} of {progress.Count} waypoints, ") +
                (progress.ArrivedAt >= 0 ? FormattableString.Invariant($"arriving at {progress.ArrivedAt:0.000} s") : "not arriving") +
                FormattableString.Invariant($"; lowest at {progress.Lowest:0.000} m; {_clamped} frames clamped"));
        }
        _query?.Dispose();
    }

    private struct Collect : IChunkJob
    {
        public List<Entity> Found;

        public readonly void Execute(in ChunkView chunk) => Found.AddRange(chunk.Entities);
    }

    private static string NameOf(World world, Entity entity) =>
        world.Has(entity, Components.Of<EntityName>()) ? world.GetManaged<EntityName>(entity).Value : $"entity {entity.Value}";

    // The waypoints from `first` on (each once, should the route loop).
    private static int Length(World world, Entity first)
    {
        HashSet<Entity> seen = [];
        for (Entity at = first; at.Value != 0 && world.Has(at, Components.Of<Waypoint>()) && seen.Add(at);
             at = world.Read<Waypoint>(at).Next)
        {
        }
        return seen.Count;
    }
}
