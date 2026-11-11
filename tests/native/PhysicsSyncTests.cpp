// Physics.Sync (ADR 0085): what the game writes reaches Box3D by kind,
// layers decide what collides (either mask suffices), and bodies under a
// parent write back in its space.

#include "PhysicsTestSupport.h"

using namespace crucible;
using namespace crucible::physics;
using namespace crucible::physics::testing;

TEST_CASE("A transform the game writes teleports a dynamic body")
{
    Scene scene;
    RigidBody body = dynamic();
    body.gravityScale = 0;
    body.linearVelocity = {1, 0, 0};
    const ecs::Entity ball = scene.add(sphere(0.5f), {0, 5, 0}, &body);
    scene.seconds(0.5);
    ecs::get<scenes::LocalTransform>(*scene.world, ball)->position = {10, 5, 0};
    scene.step();
    // Moved there, still moving at 1 m/s: a tick on from the new place.
    CHECK(bodyPose(*scene.world, ball).position.x == doctest::Approx(10 + 1.0 / 60).epsilon(1e-4));
    CHECK(ecs::read<RigidBody>(*scene.world, ball)->linearVelocity.x == doctest::Approx(1));
}

TEST_CASE("A velocity the game writes is pushed in")
{
    Scene scene;
    RigidBody body = dynamic();
    body.gravityScale = 0;
    const ecs::Entity ball = scene.add(sphere(0.5f), {0, 5, 0}, &body);
    scene.step();
    ecs::get<RigidBody>(*scene.world, ball)->linearVelocity = {6, 0, 0};
    scene.seconds(1.0);
    CHECK(bodyPose(*scene.world, ball).position.x == doctest::Approx(6).epsilon(0.02));
}

TEST_CASE("Layers decide what collides and either mask suffices")
{
    Scene scene;
    const RigidBody floorBody{BodyKind::Static, 0, {}, {}, 0, 1, 1}; // on 1, colliding with 1
    scene.add(box({40, 1, 40}), {0, -0.5f, 0}, &floorBody);
    RigidBody apart = dynamic();
    apart.layers = 2;
    apart.mask = 2; // neither's mask has the other's layer
    RigidBody reaching = dynamic();
    reaching.layers = 2;
    reaching.mask = 1; // its mask has the floor's layer
    const ecs::Entity through = scene.add(sphere(0.5f), {0, 2, 0}, &apart);
    const ecs::Entity resting = scene.add(sphere(0.5f), {3, 2, 0}, &reaching);
    scene.seconds(2.0);
    CHECK(scene.position(through).y < -5);
    CHECK(scene.position(resting).y == doctest::Approx(0.5f).epsilon(0.02));
}

TEST_CASE("A body under a parent writes back in the parent's space")
{
    Scene scene;
    const ecs::ComponentId set[] = {ecs::componentIdOf<scenes::LocalTransform>,
                                    ecs::componentIdOf<scenes::WorldTransform>};
    const ecs::Entity parent = ecs::createEntity(*scene.world, set);
    *ecs::get<scenes::LocalTransform>(*scene.world, parent) = {{5, 0, 0}, Quaternion::identity(), {1, 1, 1}};
    const RigidBody body = dynamic();
    const ecs::Entity ball = scene.add(sphere(0.5f), {0, 10, 0}, &body);
    REQUIRE(ecs::setParent(*scene.world, ball, parent));
    scene.seconds(0.5);
    CHECK(bodyPose(*scene.world, ball).position.x == doctest::Approx(5).epsilon(1e-4));
    CHECK(scene.position(ball).x == doctest::Approx(0).epsilon(1e-4));
    CHECK(scene.position(ball).y < 9.5f);
}
