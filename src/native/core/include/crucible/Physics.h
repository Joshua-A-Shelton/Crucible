#pragma once

// The physics contract (ADR 0075): what any physics implementation honours,
// so a game's code and scenes don't change when the implementation does
// (Box3D, Jolt, or a game's own, ADR 0027). Three parts:
//
//   - Components, the data: RigidBody, Collider, Joint, CharacterController.
//   - Systems, the per-frame work, crossing the boundary once per tick:
//     "Physics.Sync" (PrePhysics) pushes new, removed, and moved bodies into
//     the simulation; "Physics.Step" (Physics) advances it; "Physics.WriteBack"
//     (PostPhysics) writes dynamic bodies back to LocalTransform. They're
//     gameplay systems: they run while the game plays, not while editing.
//   - A service, the calls: queries, contacts, character moves. The functions
//     below forward to the installed PhysicsService.
//
// An implementation (a native module, ADR 0074) registers the three systems
// under the same names, replacing the defaults, and installs its service.
// Until one does, the null physics stands in: nothing moves or collides,
// queries find nothing, characters move straight through everything.
//
// Units are meters, seconds, and kilograms; Y is up (ADR 0024).

#include <crucible/Ecs.h>
#include <crucible/Export.h>
#include <crucible/Expose.h>
#include <crucible/Math.h>
#include <crucible/RenderAssets.h>

#include <cstdint>
#include <span>
#include <string_view>

namespace crucible::physics {

inline constexpr std::string_view syncSystemName = "Physics.Sync";
inline constexpr std::string_view stepSystemName = "Physics.Step";
inline constexpr std::string_view writeBackSystemName = "Physics.WriteBack";

// Collision layers: 32, each body and character on one. Which layers
// collide is a matrix (setLayersCollide); queries take a mask of layers.
inline constexpr uint32_t layerCount = 32;
inline constexpr uint32_t allLayers = 0xFFFFFFFFu;

enum class CRUCIBLE_EXPOSE BodyKind : uint32_t
{
    Static,    // never moves: the level
    Kinematic, // moved by the game through its transform; pushes, isn't pushed
    Dynamic,   // moved by the simulation
};

enum class CRUCIBLE_EXPOSE ShapeKind : uint32_t
{
    Box,     // size: the full extents
    Sphere,  // size.x: the radius
    Capsule, // size.x: the radius; size.y: the height between the end caps' centers, along Y
    Mesh,    // a model's triangles (static bodies only)
    Convex,  // the convex hull of a model's vertices
};

// An entity simulated by physics; it needs a Collider too (on itself).
struct CRUCIBLE_EXPOSE RigidBody
{
    BodyKind kind;
    CRUCIBLE_HINT_RANGE(0, 10000) float mass; // kilograms; dynamic bodies only
    Vector3 linearVelocity;                   // meters per second
    Vector3 angularVelocity;                  // radians per second, around each axis
    CRUCIBLE_HINT_RANGE(-2, 2) float gravityScale;
    CRUCIBLE_HINT_RANGE(0, 31) uint32_t layer;
};

// The shape physics sees, in the entity's space.
struct CRUCIBLE_EXPOSE Collider
{
    ShapeKind shape;
    Vector3 size;   // see ShapeKind
    Vector3 offset; // from the entity's origin
    CRUCIBLE_HINT_RANGE(0, 2) float friction;
    CRUCIBLE_HINT_RANGE(0, 1) float restitution; // bounciness
    bool isTrigger;                              // reports contacts but doesn't collide
    assets::AssetId mesh;                        // Mesh and Convex: the model
    int32_t meshIndex;                           // which of its meshes
};

enum class CRUCIBLE_EXPOSE JointKind : uint32_t
{
    Fixed,      // no relative movement
    Hinge,      // turns around axis, within lowerLimit..upperLimit radians
    BallSocket, // turns any way around the anchor
    SwingTwist, // a ragdoll's joint: swings within swingLimit, twists around axis within twistLimit
    Distance,   // keeps the anchors lowerLimit..upperLimit meters apart
};

// Joins its entity's body to another's.
struct CRUCIBLE_EXPOSE Joint
{
    JointKind kind;
    ecs::Entity other;
    Vector3 anchor;      // in this entity's space
    Vector3 otherAnchor; // in the other's
    Vector3 axis;        // in this entity's space
    float lowerLimit;
    float upperLimit;
    CRUCIBLE_HINT_RANGE(0, 3.14159f) float swingLimit; // radians
    CRUCIBLE_HINT_RANGE(0, 3.14159f) float twistLimit; // radians
};

// A capsule moved by the game with moveCharacter, sliding along what it
// hits and climbing steps (ADR 0027); not a RigidBody.
struct CRUCIBLE_EXPOSE CharacterController
{
    CRUCIBLE_HINT_RANGE(0, 2) float radius;
    CRUCIBLE_HINT_RANGE(0, 4) float height; // between the end caps' centers
    CRUCIBLE_HINT_RANGE(0, 1) float stepHeight;
    CRUCIBLE_HINT_RANGE(0, 1.5708f) float slopeLimit; // radians; steeper is a wall
    CRUCIBLE_HINT_RANGE(0, 31) uint32_t layer;
};

// A shape for casts and overlaps: Box, Sphere, or Capsule, sized as a Collider.
struct CRUCIBLE_EXPOSE QueryShape
{
    ShapeKind kind;
    Vector3 size;
};

struct CRUCIBLE_EXPOSE RayHit
{
    bool hit;
    ecs::Entity entity;
    Vector3 point;
    Vector3 normal;
    float distance;
};

enum class CRUCIBLE_EXPOSE ContactKind : uint32_t
{
    Begin,
    End,
};

// Two entities that started or stopped touching in the last tick.
struct CRUCIBLE_EXPOSE ContactEvent
{
    ContactKind kind;
    bool trigger; // one of them is a trigger
    ecs::Entity a;
    ecs::Entity b;
    Vector3 point;
    Vector3 normal; // from a to b
};

struct CRUCIBLE_EXPOSE CharacterMove
{
    Vector3 position; // where it ended up
    bool grounded;    // standing on something walkable
    Vector3 groundNormal;
};

// What an implementation provides for the calls below. Each runs on the
// main thread, between phases or in a system.
struct PhysicsService
{
    RayHit (*raycast)(ecs::World& world, Vector3 from, Vector3 direction, float distance, uint32_t layers);
    RayHit (*shapeCast)(ecs::World& world, QueryShape shape, Vector3 from, Quaternion rotation, Vector3 direction,
                        float distance, uint32_t layers);
    uint32_t (*overlap)(ecs::World& world, QueryShape shape, Vector3 at, Quaternion rotation, uint32_t layers,
                        std::span<ecs::Entity> into);
    uint32_t (*contacts)(ecs::World& world, std::span<ContactEvent> into);
    CharacterMove (*moveCharacter)(ecs::World& world, ecs::Entity character, Vector3 displacement, float seconds);
};

// Replaces the service; the default is the null physics's. Main thread.
CRUCIBLE_EXPORT void installPhysics(const PhysicsService& service);
// The installed service.
CRUCIBLE_EXPORT const PhysicsService& physicsService();

// The first hit along a ray (direction normalized), on the given layers.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE RayHit raycast(ecs::World& world, Vector3 from, Vector3 direction, float distance,
                                               uint32_t layers);
// The first hit of a shape swept along a ray: hitboxes (ADR 0027).
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE RayHit shapeCast(ecs::World& world, QueryShape shape, Vector3 from, Quaternion rotation,
                                                 Vector3 direction, float distance, uint32_t layers);
// The entities whose colliders overlap the shape, as many as fit; returns how many there are.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE uint32_t overlap(ecs::World& world, QueryShape shape, Vector3 at, Quaternion rotation,
                                                 uint32_t layers, std::span<ecs::Entity> into);
// The last tick's contact events, as many as fit; returns how many there are.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE uint32_t contacts(ecs::World& world, std::span<ContactEvent> into);
// Moves a character by a displacement over a time, sliding and climbing,
// and writes its LocalTransform.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE CharacterMove moveCharacter(ecs::World& world, ecs::Entity character,
                                                            Vector3 displacement, float seconds);

// The layer matrix, which every implementation reads: by default every
// layer collides with every other.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE void setLayersCollide(uint32_t a, uint32_t b, bool collide);
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE bool layersCollide(uint32_t a, uint32_t b);

// Registers the components and, unless an implementation registered its
// own first, the null physics's systems. The engine calls it at start-up.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE_INTERNAL void registerPhysics();

} // namespace crucible::physics
