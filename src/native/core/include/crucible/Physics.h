#pragma once

// The physics contract (ADR 0075): what any physics implementation honours,
// so a game's code and scenes don't change when the implementation does
// (Box3D, Jolt, or a game's own, ADR 0027). Three parts:
//
//   - Components, the data: RigidBody, Collider, Joint, CharacterController.
//   - Systems, the per-frame work, crossing the boundary once per tick:
//     "Physics.Sync" (PrePhysics) pushes new, removed, and moved bodies into
//     the simulation; "Physics.Step" (Physics) advances it; "Physics.WriteBack"
//     (PostPhysics) writes dynamic bodies back; and once a frame
//     "Physics.Interpolate" (Update) sets each dynamic body's LocalTransform
//     between its last two ticks, so motion is smooth at any frame rate. They're
//     gameplay systems: they run while the game plays, not while editing.
//   - A service, the calls: queries, contacts, character moves. The functions
//     below forward to the installed PhysicsService.
//
// An implementation (a native module, ADR 0074) registers the three systems
// under the same names, replacing the defaults, and installs its service.
// The engine's own is Box3D (ADR 0085); queries and character moves join it
// in later steps, and until then answer as the null physics does: queries
// find nothing, characters move straight through everything.
//
// Units are meters, seconds, and kilograms; Y is up (ADR 0024).

#include <crucible/Ecs.h>
#include <crucible/Export.h>
#include <crucible/Expose.h>
#include <crucible/Math.h>
#include <crucible/RenderAssets.h>
#include <crucible/Vector.h>

#include <cstdint>
#include <span>
#include <string_view>

namespace crucible::physics {

inline constexpr std::string_view syncSystemName = "Physics.Sync";
inline constexpr std::string_view stepSystemName = "Physics.Step";
inline constexpr std::string_view writeBackSystemName = "Physics.WriteBack";
inline constexpr std::string_view interpolateSystemName = "Physics.Interpolate";

// Collision layers: 32, as bits of a mask. A body or character is on the
// layers in its `layers` and collides with those in its `mask`; two collide
// when either's mask has a layer the other is on (Box3D's category and mask
// bits). Queries take a mask too. The project names the layers
// ("PhysicsLayers"); the inspector shows them by name.
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
    Mesh,    // a model's triangles, imported (its Collision setting), built at load, or made at run time; static
             // bodies only (Box3D collides meshes only there); scaled, not offset
    Convex,  // the convex hull of a model's mesh's vertices, likewise; scaled and offset
};

// An entity simulated by physics; it needs a Collider too (on itself).
struct CRUCIBLE_EXPOSE RigidBody
{
    BodyKind kind;
    CRUCIBLE_HINT_RANGE(0, 10000) float mass; // kilograms; dynamic bodies only
    Vector3 linearVelocity;                   // meters per second
    Vector3 angularVelocity;                  // radians per second, around each axis
    CRUCIBLE_HINT_RANGE(-2, 2) float gravityScale;
    CRUCIBLE_HINT_LAYERS uint32_t layers; // the layers it's on
    CRUCIBLE_HINT_LAYERS uint32_t mask;   // the layers it collides with
};

// The shape physics sees, in the entity's space.
struct CRUCIBLE_EXPOSE Collider
{
    ShapeKind shape;
    Vector3 size;   // see ShapeKind
    Vector3 offset; // from the entity's origin
    CRUCIBLE_HINT_RANGE(0, 2) float friction;
    CRUCIBLE_HINT_RANGE(0, 1) float restitution;         // bounciness
    bool isTrigger;                                      // reports contacts but doesn't collide
    CRUCIBLE_HINT_ASSET(".crmesh") assets::AssetId mesh; // Mesh and Convex: the model
    CRUCIBLE_HINT_MESH_OF(mesh) int32_t meshIndex;       // which of its meshes
    CRUCIBLE_HINT_TOOLTIP("Generate the collision data at load if the model has none generated")
    bool generateIfMissing; // Mesh and Convex: build from the model's triangles when not imported (ADR 0086)
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
    CRUCIBLE_HINT_ENTITY_WITH(Collider) ecs::Entity other; // the body it's joined to
    Vector3 anchor;                                        // in this entity's space
    Vector3 otherAnchor;                                   // in the other's
    Vector3 axis;                                          // in this entity's space
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
    CRUCIBLE_HINT_LAYERS uint32_t layers;             // the layers it's on
    CRUCIBLE_HINT_LAYERS uint32_t mask;               // the layers it collides with
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

// Two entities that started or stopped touching during this frame's ticks.
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
// The contact events of every tick since the last frame, as many as fit;
// returns how many there are.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE uint32_t contacts(ecs::World& world, std::span<ContactEvent> into);
// Moves a character by a displacement over a time, sliding and climbing,
// and writes its LocalTransform.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE CharacterMove moveCharacter(ecs::World& world, ecs::Entity character,
                                                            Vector3 displacement, float seconds);

// A dynamic body's pose as of the last tick (its LocalTransform is
// interpolated, up to a tick behind), in world space; found false for an
// entity without a body.
struct CRUCIBLE_EXPOSE BodyPose
{
    bool found;
    Vector3 position;
    Quaternion rotation;
};

CRUCIBLE_EXPORT CRUCIBLE_EXPOSE BodyPose bodyPose(const ecs::World& world, ecs::Entity entity);

// Adds debug lines (crucible/DebugLines.h) along every collider's outline,
// from the components, so it works while editing too.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE void drawColliders(const ecs::World& world, Vector4 color);

// --- Mesh and Convex colliders (ADR 0086) -----------------------------------

// Cooking, for the model importer: Box3D's collision data for one mesh, in
// its own format (COLM and COLH, MeshFile.h). Triangles face the way
// Crucible's meshes do (clockwise as seen from the front, in its left-handed
// space; meshes are one-sided); a hull keeps at most
// maxVertices (4 to 128) of the points'. False if it can't be made.
CRUCIBLE_EXPORT bool cookCollisionMesh(std::span<const Vector3> positions, std::span<const uint32_t> indices,
                                       Vector<uint8_t>& out);
CRUCIBLE_EXPORT bool cookCollisionHull(std::span<const Vector3> points, uint32_t maxVertices, Vector<uint8_t>& out);

// Built at run time, from geometry the game makes (generated, or changing):
// an ID a Collider's mesh takes (meshIndex 0), as an imported model's would;
// none if it can't be made. Destroyed, colliders using it lose their shape.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE assets::AssetId createCollisionMesh(std::span<const Vector3> positions,
                                                                    std::span<const uint32_t> indices);
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE assets::AssetId createCollisionHull(std::span<const Vector3> points,
                                                                    uint32_t maxVertices);
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE void destroyCollisionShape(assets::AssetId shape);

// Forgets a model's collision data read before (the editor after a
// reimport): colliders using it are rebuilt from the new. True if it was read.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE bool reloadCollisionAsset(assets::AssetId model);

// Frees the world's physics (before destroying it).
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE_INTERNAL void releasePhysics(const ecs::World& world);

// Registers the components and, unless an implementation registered its
// own first, Box3D's systems (ADR 0085). The engine calls it at start-up.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE_INTERNAL void registerPhysics();

} // namespace crucible::physics
