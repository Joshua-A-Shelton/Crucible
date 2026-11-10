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
