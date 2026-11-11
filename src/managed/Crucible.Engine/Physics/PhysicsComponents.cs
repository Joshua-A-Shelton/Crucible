using Crucible.Serialization;

namespace Crucible.Physics;

// The physics contract's components (ADR 0075), declared in C++
// (crucible/Physics.h); these halves add saving and the defaults a
// component starts with when it's added (all zeros would be a body on no
// layer, colliding with nothing).

/// <summary>An entity simulated by physics; it needs a <see cref="Collider"/> too.</summary>
[Serialized]
public partial struct RigidBody
{
    /// <summary>
    /// A body's pose as of the last tick, in world space: its transform is
    /// interpolated between ticks, up to a tick behind. <c>Found</c> is false
    /// without a body.
    /// </summary>
    public static BodyPose Pose(Ecs.World world, Ecs.Entity entity) => Api.BodyPose(world.Live, entity);

    public RigidBody()
    {
        Kind = BodyKind.Dynamic;
        Mass = 1;
        GravityScale = 1;
        Layers = 1;
        Mask = uint.MaxValue;
    }
}

/// <summary>The shape physics sees, in the entity's space.</summary>
[Serialized]
public partial struct Collider
{
    /// <summary>Debug lines along every collider's outline, this frame (while editing too).</summary>
    public static void DrawColliders(Ecs.World world, System.Numerics.Vector4 color) =>
        Api.DrawColliders(world.Live, color);

    public Collider()
    {
        Shape = ShapeKind.Box;
        Size = System.Numerics.Vector3.One;
        Friction = 0.5f;
    }
}

/// <summary>Joins its entity's body to another's.</summary>
[Serialized]
public partial struct Joint;

/// <summary>A capsule the game moves with <c>Api.MoveCharacter</c>, sliding along what it hits.</summary>
[Serialized]
public partial struct CharacterController
{
    public CharacterController()
    {
        Radius = 0.4f;
        Height = 1;
        StepHeight = 0.3f;
        SlopeLimit = 0.785f; // 45 degrees
        Layers = 1;
        Mask = uint.MaxValue;
    }
}
