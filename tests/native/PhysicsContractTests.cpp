// The physics contract (ADR 0075): its components and gameplay systems
// registered, the null physics answering every call, and an installed
// service taking over.

#include "SubsystemTestSupport.h"

#include <crucible/Physics.h>
#include <crucible/Transform.h>

#include <doctest/doctest.h>

using namespace crucible;
using namespace crucible::physics;

TEST_CASE("The physics contract registers its components and gameplay systems")
{
    registerPhysics();
    for (std::string_view name : {"Physics.Sync", "Physics.Step", "Physics.WriteBack"})
    {
        CAPTURE(name);
        const int32_t index = test::systemIndex(name);
        REQUIRE(index >= 0);
        CHECK(loop::nativeSystemIsGameplay(static_cast<uint32_t>(index)));
    }
    CHECK(loop::nativeSystemPhase(static_cast<uint32_t>(test::systemIndex("Physics.Sync"))) == "PrePhysics");
    CHECK(loop::nativeSystemPhase(static_cast<uint32_t>(test::systemIndex("Physics.Step"))) == "Physics");
    CHECK(loop::nativeSystemPhase(static_cast<uint32_t>(test::systemIndex("Physics.WriteBack"))) == "PostPhysics");
    CHECK(ecs::componentIdOf<RigidBody> != ecs::invalidComponent);
    CHECK(ecs::componentIdOf<Collider> != ecs::invalidComponent);
    CHECK(ecs::componentIdOf<Joint> != ecs::invalidComponent);
    CHECK(ecs::componentIdOf<CharacterController> != ecs::invalidComponent);
}

TEST_CASE("The null physics finds nothing and moves characters freely")
{
    registerPhysics();
    scenes::registerTransforms();
    ecs::World* world = ecs::createWorld();
    const ecs::ComponentId set[] = {ecs::componentIdOf<scenes::LocalTransform>,
                                    ecs::componentIdOf<CharacterController>};
    const ecs::Entity character = ecs::createEntity(*world, set);
    *ecs::get<scenes::LocalTransform>(*world, character) = {{1, 0, 0}, {0, 0, 0, 1}, {1, 1, 1}};

    for (std::string_view name : {"Physics.Sync", "Physics.Step", "Physics.WriteBack"})
    {
        test::runSystem(name, *world);
    }
    CHECK_FALSE(raycast(*world, {0, 10, 0}, {0, -1, 0}, 100, allLayers).hit);
    CHECK_FALSE(
        shapeCast(*world, {ShapeKind::Sphere, {0.5f, 0, 0}}, {0, 10, 0}, {0, 0, 0, 1}, {0, -1, 0}, 100, allLayers).hit);
    ecs::Entity found[4];
    CHECK(overlap(*world, {ShapeKind::Box, {10, 10, 10}}, {0, 0, 0}, {0, 0, 0, 1}, allLayers, found) == 0);
    ContactEvent events[4];
    CHECK(contacts(*world, events) == 0);

    const CharacterMove moved = moveCharacter(*world, character, {0, 0, 2}, 0.5f);
    CHECK(moved.position.x == 1);
    CHECK(moved.position.z == 2);
    CHECK_FALSE(moved.grounded);
    CHECK(ecs::read<scenes::LocalTransform>(*world, character)->position.z == 2);
    ecs::destroyWorld(world);
}

namespace {

RayHit alwaysHit(ecs::World&, Vector3, Vector3, float distance, uint32_t)
{
    return {true, {}, {0, 0, 0}, {0, 1, 0}, distance / 2};
}

} // namespace

TEST_CASE("An installed physics service answers the calls")
{
    const PhysicsService before = physicsService();
    PhysicsService replacement = before;
    replacement.raycast = alwaysHit;
    installPhysics(replacement);
    ecs::World* world = ecs::createWorld();
    const RayHit hit = raycast(*world, {0, 0, 0}, {0, 0, 1}, 10, allLayers);
    CHECK(hit.hit);
    CHECK(hit.distance == 5);
    installPhysics(before);
    CHECK_FALSE(raycast(*world, {0, 0, 0}, {0, 0, 1}, 10, allLayers).hit);
    ecs::destroyWorld(world);
}
