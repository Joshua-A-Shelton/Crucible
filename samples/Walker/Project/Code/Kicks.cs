using System.Numerics;
using Crucible.Animation;
using Crucible.Diagnostics;
using Crucible.Ecs;
using Crucible.Loop;
using Crucible.Physics;
using Crucible.Scenes;

namespace Walker;

// Step 7's sample (ADR 0096): the Walker's right foot carries a hitbox,
// active in its walk's Kick window; what it touches is logged with when, and
// a crate it touches is knocked away (a hitbox is a query, so it pushes
// nothing itself). The M9 demonstration (ADR 0097) reads how far into its
// owner's window each hit came, which doesn't depend on when the game
// started.

/// <summary>
/// Logs every hitbox hit with its time since the game started and since its
/// owner's latest window opened, and knocks a dynamic body it hits away from
/// the hitbox and up.
/// </summary>
[System(Phases.LateUpdate)]
public sealed class KickSystem : GameSystem
{
    private readonly HitboxHit[] _hits = new HitboxHit[32];
    private readonly AnimationEvent[] _events = new AnimationEvent[64];
    private readonly Dictionary<Entity, ulong> _opened = [];
    private ulong _start;

    public override void Start(FrameContext context)
    {
        _start = Crucible.Platform.Api.TimestampNow();
        _opened.Clear();
    }

    public override void Run(FrameContext context)
    {
        World world = context.World;
        int passed = Math.Min(Animator.Events(world, _events), _events.Length);
        for (int i = 0; i < passed; ++i)
        {
            if (_events[i].Kind == AnimationEventKind.WindowOpen)
            {
                _opened[_events[i].Entity] = _events[i].Timestamp;
            }
        }
        int count = Math.Min(Physics.HitboxHits(world, _hits), _hits.Length);
        for (int i = 0; i < count; ++i)
        {
            HitboxHit hit = _hits[i];
            double seconds = (hit.Timestamp - Math.Min(hit.Timestamp, _start)) * 1e-9;
            string into = _opened.TryGetValue(hit.Owner, out ulong opened)
                ? FormattableString.Invariant($", {((long)hit.Timestamp - (long)opened) * 1e-6:0.000} ms into its window")
                : "";
            Log.Info(FormattableString.Invariant(
                $"Walker.Kicks: {NameOf(world, hit.Hitbox)} hit {NameOf(world, hit.Target)} at {seconds:0.000} s{into}"));
            if (world.Has(hit.Target, Components.Of<RigidBody>()) && world.Read<RigidBody>(hit.Target).Kind == BodyKind.Dynamic)
            {
                // Away along the ground from where the foot touched, and up.
                Vector3 away = new(-hit.Normal.X, 0, -hit.Normal.Z);
                away = away.LengthSquared() > 1e-6f ? Vector3.Normalize(away) : Vector3.UnitZ;
                world.Get<RigidBody>(hit.Target).LinearVelocity = away * 3.5f + Vector3.UnitY * 3;
            }
        }
    }

    private static string NameOf(World world, Entity entity) =>
        world.Has(entity, Components.Of<EntityName>()) ? world.GetManaged<EntityName>(entity).Value : $"entity {entity.Value}";
}
