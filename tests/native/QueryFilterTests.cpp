// What a query hits (ADR 0090): colliders on its layers (theirs, not their
// masks), not the entity it leaves out, and triggers only when asked.

#include "PhysicsTestSupport.h"

using namespace crucible;
using namespace crucible::physics;
using namespace crucible::physics::testing;

TEST_CASE("The default filter hits every layer and leaves nothing out")
{
    const QueryFilter filter = defaultQueryFilter();
    CHECK(filter.layers == allLayers);
    CHECK(filter.ignore.value == 0);
    CHECK_FALSE(filter.triggers);
}

TEST_CASE("A query hits only colliders on its layers")
{
    Scene scene;
    const ecs::Entity floor = ground(scene);
    // A static body on layer 2 that collides with nothing: its mask doesn't count.
    const RigidBody onTwo{BodyKind::Static, 0, {}, {}, 1, 1u << 1, 0};
    const ecs::Entity block = scene.add(box({1, 1, 1}), {0, 2, 0}, &onTwo);
    scene.step();
    const QueryFilter first{1u, {}, false}, second{1u << 1, {}, false};
    CHECK(raycast(*scene.world, {0, 10, 0}, {0, -1, 0}, 100, first).entity.value == floor.value);
    CHECK(raycast(*scene.world, {0, 10, 0}, {0, -1, 0}, 100, second).entity.value == block.value);
    ecs::Entity found[2]{};
    CHECK(overlap(*scene.world, {ShapeKind::Box, {4, 6, 4}}, {0, 1, 0}, Quaternion::identity(), second, found) == 1);
}

TEST_CASE("A query leaves out the entity it names")
{
    Scene scene;
    const ecs::Entity floor = ground(scene);
    const ecs::Entity caster = scene.add(box({1, 1, 1}), {0, 2, 0});
    scene.step();
    const QueryFilter filter{allLayers, caster, false};
    CHECK(raycast(*scene.world, {0, 10, 0}, {0, -1, 0}, 100, filter).entity.value == floor.value);
    CHECK(shapeCast(*scene.world, {ShapeKind::Sphere, {0.3f, 0, 0}}, {0, 10, 0}, Quaternion::identity(), {0, -1, 0},
                    100, filter)
              .entity.value == floor.value);
    ecs::Entity found[2]{};
    CHECK(overlap(*scene.world, {ShapeKind::Sphere, {0.2f, 0, 0}}, {0, 2, 0}, Quaternion::identity(), filter, found) ==
          0);
}

TEST_CASE("Triggers are hit only when asked")
{
    Scene scene;
    const ecs::Entity floor = ground(scene);
    Collider zone = box({1, 1, 1});
    zone.isTrigger = true;
    const ecs::Entity trigger = scene.add(zone, {0, 2, 0});
    scene.step();
    QueryFilter filter = defaultQueryFilter();
    CHECK(raycast(*scene.world, {0, 10, 0}, {0, -1, 0}, 100, filter).entity.value == floor.value);
    ecs::Entity found[2]{};
    CHECK(overlap(*scene.world, {ShapeKind::Sphere, {0.2f, 0, 0}}, {0, 2, 0}, Quaternion::identity(), filter, found) ==
          0);
    filter.triggers = true;
    CHECK(raycast(*scene.world, {0, 10, 0}, {0, -1, 0}, 100, filter).entity.value == trigger.value);
    CHECK(overlap(*scene.world, {ShapeKind::Sphere, {0.2f, 0, 0}}, {0, 2, 0}, Quaternion::identity(), filter, found) ==
          1);
}
