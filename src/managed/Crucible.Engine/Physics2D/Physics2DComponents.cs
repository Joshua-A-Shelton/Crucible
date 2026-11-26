using System.Numerics;
using Crucible.Ecs;
using Crucible.Physics;
using Crucible.Serialization;

namespace Crucible.Physics2D;

// The 2D physics contract's components (ADR 0093), declared in C++
// (crucible/Physics2D.h); these halves add saving and the defaults a
// component starts with when it's added, as 3D's do.

/// <summary>An entity simulated by 2D physics; it needs a <see cref="Collider2D"/> (and a Transform2D) too.</summary>
[System.ComponentModel.Category("Physics/2D"), System.ComponentModel.Description("Moves the entity by 2D physics: dynamic, kinematic, or static.")]
[Serialized]
[Crucible.Scenes.ComponentIcon("weight")]
public partial struct RigidBody2D
{
    /// <summary>A body's pose as of the last tick, in the world (its Transform2D is interpolated).</summary>
    public static BodyPose2D Pose(World world, Entity entity) => Api.BodyPose(world.Live, entity);

    public RigidBody2D()
    {
        Kind = BodyKind.Dynamic;
        Mass = 1;
        GravityScale = 1;
        Layers = 1;
        Mask = uint.MaxValue;
    }
}

/// <summary>The shape 2D physics sees, in the entity's space.</summary>
[System.ComponentModel.Category("Physics/2D"), System.ComponentModel.Description("A 2D shape for physics and queries: a box, circle, capsule, polygon, chain, or tilemap.")]
[Serialized]
[Crucible.Scenes.ComponentIcon("shapes")]
public partial struct Collider2D
{
    /// <summary>Debug lines along every 2D collider's outline, this frame (while editing too).</summary>
    public static void DrawColliders(World world, Vector4 color) => Api.DrawColliders2D(world.Live, color);

    public Collider2D()
    {
        Shape = ShapeKind2D.Box;
        Size = Vector2.One;
        Friction = 0.5f;
    }
}

/// <summary>Joins its entity's body to another's, or (Other none) to the world (ADR 0094).</summary>
[System.ComponentModel.Category("Physics/2D"), System.ComponentModel.Description("Connects two 2D bodies: a hinge, a distance, a slider, a weld.")]
[Serialized]
[Crucible.Scenes.ComponentIcon("link-2")]
public partial struct Joint2D;

/// <summary>
/// A capsule the game moves with <see cref="Move"/> (ADR 0094), sliding, stepping, and
/// falling as 3D's does; its entity's position is its feet.
/// </summary>
[System.ComponentModel.Category("Physics/2D"), System.ComponentModel.Description("Walks a 2D character over the ground and up steps and slopes.")]
[Serialized]
[Crucible.Scenes.ComponentIcon("footprints")]
public partial struct CharacterController2D
{
    /// <summary>Moves a character by a displacement over a time; where it ended up, and whether it's on the ground.</summary>
    public static CharacterMove2D Move(World world, Entity character, Vector2 displacement, float seconds) =>
        Api.MoveCharacter2D(world.Live, character, displacement, seconds);

    /// <summary>Sets a character's speed up (a jump), off the ground from its next move.</summary>
    public static void Launch(World world, Entity character, float upwardSpeed) =>
        Api.LaunchCharacter2D(world.Live, character, upwardSpeed);

    public CharacterController2D()
    {
        Radius = 0.4f;
        Height = 1;
        StepHeight = 0.3f;
        SlopeLimit = 0.785f; // 45 degrees
        Layers = 1;
        Mask = uint.MaxValue;
        GravityScale = 1;
        StepSmoothing = 0.1f;
    }
}

/// <summary>A 2D shape that strikes (ADR 0096), swept as 3D's <see cref="Crucible.Physics.Hitbox"/> is.</summary>
[System.ComponentModel.Category("Physics/2D"), System.ComponentModel.Description("A 2D shape that finds what it overlaps, for attacks and triggers.")]
[Serialized]
[Crucible.Scenes.ComponentIcon("swords")]
public partial struct Hitbox2D
{
    public Hitbox2D()
    {
        Shape = ShapeKind2D.Circle;
        Size = new Vector2(0.25f, 0);
        Mask = uint.MaxValue;
        Enabled = true;
    }
}

/// <summary>
/// 2D physics queries (ADR 0093), as 3D's (<see cref="Crucible.Physics.Physics"/>): on the
/// last tick's world, filtered by a <see cref="QueryFilter"/>.
/// </summary>
public static class Physics2D
{
    /// <summary>The first hit along a ray (<paramref name="direction"/> normalized here), or one whose <c>Hit</c> is false.</summary>
    public static RayHit2D Raycast(World world, Vector2 from, Vector2 direction, float distance, QueryFilter? filter = null) =>
        Api.Raycast(world.Live, from, Vector2.Normalize(direction), distance, filter ?? QueryFilter.Default);

    /// <summary>The first hit of a box, circle, or capsule swept along a ray; one touching at its start hits at 0.</summary>
    public static RayHit2D ShapeCast(World world, QueryShape2D shape, Vector2 from, float angle, Vector2 direction,
        float distance, QueryFilter? filter = null) =>
        Api.ShapeCast(world.Live, shape, from, angle, Vector2.Normalize(direction), distance, filter ?? QueryFilter.Default);

    /// <summary>Every hit of a box, circle, or capsule swept along a ray, nearest first, each entity once, as many as fit; returns how many there are.</summary>
    public static int ShapeCastAll(World world, QueryShape2D shape, Vector2 from, float angle, Vector2 direction,
        float distance, Span<RayHit2D> into, QueryFilter? filter = null) =>
        (int)Api.ShapeCastAll(world.Live, shape, from, angle, Vector2.Normalize(direction), distance,
            filter ?? QueryFilter.Default, into);

    /// <summary>What 2D hitboxes touched this frame (ADR 0096), by timestamp, as many as fit; returns how many there are.</summary>
    public static int HitboxHits(World world, Span<HitboxHit2D> into) => (int)Api.HitboxHits(world.Live, into);

    /// <summary>The entities whose 2D colliders overlap a shape, each once, as many as fit; returns how many there are.</summary>
    public static int Overlap(World world, QueryShape2D shape, Vector2 at, float angle, Span<Entity> into,
        QueryFilter? filter = null) =>
        (int)Api.Overlap(world.Live, shape, at, angle, filter ?? QueryFilter.Default, into);

    /// <summary>The contact events of every tick since the last frame, as many as fit; returns how many there are.</summary>
    public static int Contacts(World world, Span<ContactEvent2D> into) => (int)Api.Contacts(world.Live, into);
}
