using System.Numerics;
using Crucible.Ecs;
using Crucible.Physics;
using Crucible.Serialization;

namespace Crucible.Physics2D;

// The 2D physics contract's components (ADR 0093), declared in C++
// (crucible/Physics2D.h); these halves add saving and the defaults a
// component starts with when it's added, as 3D's do.

/// <summary>An entity simulated by 2D physics; it needs a <see cref="Collider2D"/> (and a Transform2D) too.</summary>
[Serialized]
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
[Serialized]
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

    /// <summary>The entities whose 2D colliders overlap a shape, each once, as many as fit; returns how many there are.</summary>
    public static int Overlap(World world, QueryShape2D shape, Vector2 at, float angle, Span<Entity> into,
        QueryFilter? filter = null) =>
        (int)Api.Overlap(world.Live, shape, at, angle, filter ?? QueryFilter.Default, into);

    /// <summary>The contact events of every tick since the last frame, as many as fit; returns how many there are.</summary>
    public static int Contacts(World world, Span<ContactEvent2D> into) => (int)Api.Contacts(world.Live, into);
}
