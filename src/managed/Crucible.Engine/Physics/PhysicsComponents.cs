using Crucible.Serialization;

namespace Crucible.Physics;

// The physics contract's components (ADR 0075), declared in C++
// (crucible/Physics.h); these halves add saving.

/// <summary>An entity simulated by physics; it needs a <see cref="Collider"/> too.</summary>
[Serialized]
public partial struct RigidBody;

/// <summary>The shape physics sees, in the entity's space.</summary>
[Serialized]
public partial struct Collider;

/// <summary>Joins its entity's body to another's.</summary>
[Serialized]
public partial struct Joint;

/// <summary>A capsule the game moves with <c>Api.MoveCharacter</c>, sliding along what it hits.</summary>
[Serialized]
public partial struct CharacterController;
