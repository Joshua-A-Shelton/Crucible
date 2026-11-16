#pragma once

// The 2D physics contract (ADR 0093), the same shape as the 3D one
// (crucible/Physics.h, ADR 0075) for entities with a Transform2D:
//   - Components, the data: RigidBody2D, Collider2D, Joint2D,
//     CharacterController2D (ADR 0094).
//   - Systems, by name: "Physics2D.Sync" (PrePhysics) makes, changes, and
//     removes bodies to match; "Physics2D.Step" (Physics) advances it;
//     "Physics2D.WriteBack" (PostPhysics) writes dynamic bodies back; and
//     once a frame "Physics2D.Interpolate" (Update) sets each dynamic body's
//     Transform2D between its last two ticks. Gameplay systems, like 3D's.
//   - A service, the calls: queries, contacts, and character moves. The
//     functions below forward to the installed Physics2DService.
//
// The engine's own is Box2D (Box2dPhysics.cpp). Bodies live in the XY plane:
// a Transform2D's position and angle; its depth is left alone. Units are
// meters, seconds, and kilograms; Y is up. Layers and query filters are 3D's
// (the project's 32 named layers, physics::QueryFilter).

#include <crucible/Ecs.h>
#include <crucible/Export.h>
#include <crucible/Expose.h>
#include <crucible/Math.h>
#include <crucible/Physics.h>

#include <cstdint>
#include <span>
#include <string_view>

namespace crucible::physics2d {

inline constexpr std::string_view syncSystemName = "Physics2D.Sync";
inline constexpr std::string_view stepSystemName = "Physics2D.Step";
inline constexpr std::string_view writeBackSystemName = "Physics2D.WriteBack";
inline constexpr std::string_view interpolateSystemName = "Physics2D.Interpolate";

// 2D gravity (meters per second squared).
inline constexpr Vector2 gravity{0, -9.81f};

// The most points a polygon collider has (Box2D's limit).
inline constexpr int32_t maxPolygonPoints = 8;

enum class CRUCIBLE_EXPOSE ShapeKind2D : uint32_t
{
    Box,     // size: the full extents
    Circle,  // size.x: the radius
    Capsule, // size.x: the radius; size.y: the length between the end caps' centers, along Y
    Polygon, // points[0..pointCount): their convex hull (3 to 8 points)
};

// An entity simulated by 2D physics; it needs a Collider2D too (on itself).
struct CRUCIBLE_EXPOSE RigidBody2D
{
    physics::BodyKind kind;
    CRUCIBLE_HINT_RANGE(0, 10000) float mass; // kilograms; dynamic bodies only
    Vector2 linearVelocity;                   // meters per second
    float angularVelocity;                    // radians per second, +X toward +Y
    CRUCIBLE_HINT_RANGE(-2, 2) float gravityScale;
    CRUCIBLE_HINT_LAYERS uint32_t layers; // the layers it's on
    CRUCIBLE_HINT_LAYERS uint32_t mask;   // the layers it collides with
};

// The shape 2D physics sees, in the entity's space.
struct CRUCIBLE_EXPOSE Collider2D
{
    ShapeKind2D shape;
    Vector2 size;   // see ShapeKind2D
    Vector2 offset; // from the entity's origin
    CRUCIBLE_HINT_RANGE(0, 2) float friction;
    CRUCIBLE_HINT_RANGE(0, 1) float restitution; // bounciness
    bool isTrigger;                              // reports contacts but doesn't collide
    Vector2 points[maxPolygonPoints];            // Polygon: its points, in the entity's space
    CRUCIBLE_HINT_RANGE(0, 8) int32_t pointCount;
};

enum class CRUCIBLE_EXPOSE JointKind2D : uint32_t
{
    Fixed,    // no relative movement
    Hinge,    // turns about Z, within lowerLimit..upperLimit radians
    Distance, // keeps the anchors lowerLimit..upperLimit meters apart
    Slider,   // moves along axis, within lowerLimit..upperLimit meters, without turning
};

// Joins its entity's body to another's, or (other none) to the world where
// it is when made (ADR 0094, as 3D's, ADR 0092): limits off unless set
// (lower < upper), angles and positions counted from how the bodies sit
// when it's made.
struct CRUCIBLE_EXPOSE Joint2D
{
    JointKind2D kind;
    CRUCIBLE_HINT_ENTITY_WITH(Collider2D) ecs::Entity other; // the body it's joined to
    Vector2 anchor;                                          // in this entity's space
    Vector2 otherAnchor;                                     // in the other's
    Vector2 axis;                                            // Slider: in this entity's space
    float lowerLimit;
    float upperLimit;
};

// A capsule moved by the game with moveCharacter2D (ADR 0094), as 3D's
// (ADR 0091) in the plane: sliding, riding a step height above the ground,
// steeper than the slope limit a wall, falling under gravity times its
// scale, eased up steps; bodies meet it as a kinematic capsule. Its
// entity's position is its feet. Characters are at the top of the
// hierarchy, and pass through each other.
struct CRUCIBLE_EXPOSE CharacterController2D
{
    CRUCIBLE_HINT_RANGE(0, 2) float radius;
    CRUCIBLE_HINT_RANGE(0, 4) float height;           // between the end caps' centers
    CRUCIBLE_HINT_RANGE(0, 1) float stepHeight;       // anything lower is stepped over
    CRUCIBLE_HINT_RANGE(0, 1.5708f) float slopeLimit; // radians; steeper is a wall
    CRUCIBLE_HINT_LAYERS uint32_t layers;             // the layers it's on
    CRUCIBLE_HINT_LAYERS uint32_t mask;               // the layers it collides with
    // Of 2D gravity; 0 leaves moving up and down to the game.
    CRUCIBLE_HINT_RANGE(0, 4) float gravityScale;
    // Seconds to rise onto a step (nine-tenths of the way); 0 snaps up at once.
    CRUCIBLE_HINT_RANGE(0, 0.5) float stepSmoothing;
};

struct CRUCIBLE_EXPOSE CharacterMove2D
{
    Vector2 position; // where it ended up (its feet)
    bool grounded;    // standing on something walkable
    Vector2 groundNormal;
    float fallSpeed; // its speed up (positive) or down after the move, meters per second
};

// A shape for casts and overlaps: Box, Circle, or Capsule, sized as a Collider2D.
struct CRUCIBLE_EXPOSE QueryShape2D
{
    ShapeKind2D kind;
    Vector2 size;
};

struct CRUCIBLE_EXPOSE RayHit2D
{
    bool hit;
    ecs::Entity entity;
    Vector2 point;
    Vector2 normal;
    float distance;
};

// Two entities that started or stopped touching during this frame's ticks.
struct CRUCIBLE_EXPOSE ContactEvent2D
{
    physics::ContactKind kind;
    bool trigger; // one of them is a trigger
    ecs::Entity a;
    ecs::Entity b;
    Vector2 point;
    Vector2 normal; // from a to b
};

struct CRUCIBLE_EXPOSE BodyPose2D
{
    bool found;
    Vector2 position;
    float angle;
};

// What an implementation provides for the calls below; main thread.
struct Physics2DService
{
    RayHit2D (*raycast)(ecs::World& world, Vector2 from, Vector2 direction, float distance,
                        physics::QueryFilter filter);
    RayHit2D (*shapeCast)(ecs::World& world, QueryShape2D shape, Vector2 from, float angle, Vector2 direction,
                          float distance, physics::QueryFilter filter);
    uint32_t (*overlap)(ecs::World& world, QueryShape2D shape, Vector2 at, float angle, physics::QueryFilter filter,
                        std::span<ecs::Entity> into);
    uint32_t (*contacts)(ecs::World& world, std::span<ContactEvent2D> into);
    CharacterMove2D (*moveCharacter)(ecs::World& world, ecs::Entity character, Vector2 displacement, float seconds);
    void (*launchCharacter)(ecs::World& world, ecs::Entity character, float upwardSpeed);
};

// Replaces the service; the default is Box2D's. Main thread.
CRUCIBLE_EXPORT void installPhysics2D(const Physics2DService& service);
CRUCIBLE_EXPORT const Physics2DService& physics2DService();

// Queries see the last tick's world, as 3D's do (ADR 0090): the first hit
// along a ray (direction normalized), not counting the collider it starts in.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE RayHit2D raycast(ecs::World& world, Vector2 from, Vector2 direction, float distance,
                                                 physics::QueryFilter filter);
// The first hit of a shape swept along a ray; one touching at its start hits at 0.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE RayHit2D shapeCast(ecs::World& world, QueryShape2D shape, Vector2 from, float angle,
                                                   Vector2 direction, float distance, physics::QueryFilter filter);
// The entities whose colliders overlap the shape, each once, as many as fit;
// returns how many there are.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE uint32_t overlap(ecs::World& world, QueryShape2D shape, Vector2 at, float angle,
                                                 physics::QueryFilter filter, std::span<ecs::Entity> into);
// The contact events of every tick since the last frame, as many as fit;
// returns how many there are.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE uint32_t contacts(ecs::World& world, std::span<ContactEvent2D> into);

// Moves a 2D character by a displacement over a time (ADR 0094), as
// physics::moveCharacter does in 3D; writes its Transform2D. An entity
// without a CharacterController2D, or before the physics has a world, just
// moves.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE CharacterMove2D moveCharacter2D(ecs::World& world, ecs::Entity character,
                                                                Vector2 displacement, float seconds);
// Sets a 2D character's speed up (a jump), off the ground from its next move.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE void launchCharacter2D(ecs::World& world, ecs::Entity character, float upwardSpeed);

// A dynamic body's pose as of the last tick (its Transform2D is
// interpolated), in the world; found false without a body.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE BodyPose2D bodyPose(const ecs::World& world, ecs::Entity entity);

// Registers the components and the four systems (unless an implementation
// registered systems under those names first). Registering again is harmless.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE_INTERNAL void registerPhysics2D();
// Frees a world's 2D physics (World.Dispose).
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE_INTERNAL void releasePhysics2D(const ecs::World& world);

// Adds debug lines along every 2D collider's outline, from the components,
// so it works while editing too.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE void drawColliders2D(const ecs::World& world, Vector4 color);
// The editor's View > Queries for 2D, as physics::recordQueries and drawQueries.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE_INTERNAL void recordQueries2D(bool on);
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE_INTERNAL void drawQueries2D(Vector4 missed, Vector4 hit);

} // namespace crucible::physics2d
