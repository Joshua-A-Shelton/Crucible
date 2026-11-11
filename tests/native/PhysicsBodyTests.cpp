// Box3D bodies (ADR 0085): falling under gravity, resting on the ground,
// kinematic bodies pushing, scale, removal, and collider outlines.

#include "PhysicsTestSupport.h"

using namespace crucible;
using namespace crucible::physics;
using namespace crucible::physics::testing;

TEST_CASE("A dynamic body falls under gravity")
{
    Scene scene;
    const RigidBody body = dynamic();
    const ecs::Entity ball = scene.add(sphere(0.5f), {0, 10, 0}, &body);
    scene.seconds(1.0);
    // About half of g a second squared, by Box3D's semi-implicit substeps
    // (the transform is interpolated, a tick behind: the body's own pose).
    CHECK(bodyPose(*scene.world, ball).position.y == doctest::Approx(10 - 0.5 * 9.81).epsilon(0.02));
    CHECK(ecs::read<RigidBody>(*scene.world, ball)->linearVelocity.y == doctest::Approx(-9.81f).epsilon(0.02));
}

TEST_CASE("A body comes to rest on the ground")
{
    Scene scene;
    ground(scene);
    const RigidBody body = dynamic();
    const ecs::Entity ball = scene.add(sphere(0.5f), {0, 2, 0}, &body);
    const ecs::Entity crate = scene.add(box({1, 1, 1}), {3, 3, 0}, &body);
    scene.seconds(3.0);
    CHECK(scene.position(ball).y == doctest::Approx(0.5f).epsilon(0.02));
    CHECK(scene.position(crate).y == doctest::Approx(0.5f).epsilon(0.02));
    CHECK(std::fabs(ecs::read<RigidBody>(*scene.world, crate)->linearVelocity.y) < 0.05f);
}

TEST_CASE("Scale makes a shape bigger")
{
    Scene scene;
    ground(scene);
    const RigidBody body = dynamic();
    const ecs::Entity ball = scene.add(sphere(0.5f), {0, 3, 0}, &body, {2, 2, 2});
    scene.seconds(3.0);
    CHECK(scene.position(ball).y == doctest::Approx(1.0f).epsilon(0.02));
}

TEST_CASE("A kinematic body moved by the game pushes a dynamic one")
{
    Scene scene;
    ground(scene);
    const RigidBody pusher{BodyKind::Kinematic, 0, {}, {}, 0, 1, allLayers};
    const RigidBody body = dynamic();
    const ecs::Entity wall = scene.add(box({0.5f, 2, 4}), {-2, 1, 0}, &pusher);
    const ecs::Entity crate = scene.add(box({1, 1, 1}), {0, 0.5f, 0}, &body);
    scene.seconds(0.5);
    for (int i = 0; i < 90; ++i)
    {
        ecs::get<scenes::LocalTransform>(*scene.world, wall)->position.x += 3.0f / 60; // 3 m/s
        scene.step();
    }
    CHECK(scene.position(wall).x == doctest::Approx(2.5f).epsilon(0.001)); // the game's, untouched
    CHECK(scene.position(crate).x > 2.5f);
}

TEST_CASE("Removing a body's components removes it")
{
    Scene scene;
    const RigidBody body = dynamic();
    const ecs::Entity ball = scene.add(sphere(0.5f), {0, 10, 0}, &body);
    scene.step();
    CHECK(bodyPose(*scene.world, ball).found);
    ecs::removeComponent(*scene.world, ball, ecs::componentIdOf<Collider>);
    scene.step();
    CHECK_FALSE(bodyPose(*scene.world, ball).found);
}

TEST_CASE("Colliders are outlined from their components")
{
    Scene scene;
    scene.add(box({1, 2, 3}), {0, 0, 0});
    debug::clearDebugLines();
    drawColliders(*scene.world, {1, 1, 1, 1});
    CHECK(debug::debugLineCount() == 12);
    const RigidBody body = dynamic();
    scene.add(sphere(0.5f), {0, 5, 0}, &body);
    debug::clearDebugLines();
    drawColliders(*scene.world, {1, 1, 1, 1});
    CHECK(debug::debugLineCount() == 12 + 3 * 24);
}
