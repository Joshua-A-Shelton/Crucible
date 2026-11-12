// Joints' lifetimes (ADR 0092): made when both bodies exist, made again when
// changed, gone with their component or either body.

#include "PhysicsTestSupport.h"

using namespace crucible;
using namespace crucible::physics;
using namespace crucible::physics::testing;

TEST_CASE("A joint added later holds from then and lets go when removed")
{
    Scene scene;
    const RigidBody body = dynamic();
    const ecs::Entity block = scene.add(box({1, 1, 1}), {0, 5, 0}, &body);
    scene.step();
    join(scene, block, {JointKind::Fixed});
    scene.seconds(1);
    CHECK(bodyPose(*scene.world, block).position.y == doctest::Approx(5).epsilon(0.02));
    ecs::removeComponent(*scene.world, block, ecs::componentIdOf<Joint>);
    scene.seconds(1);
    CHECK(bodyPose(*scene.world, block).position.y < 1);
}

TEST_CASE("A changed joint is made again")
{
    Scene scene;
    const RigidBody body = dynamic();
    const ecs::Entity block = scene.add(box({1, 1, 1}), {0, 5, 0}, &body);
    join(scene, block, {JointKind::Fixed});
    scene.seconds(0.5);
    // Now a rope to the world 0..3 m: the block falls to its end.
    join(scene, block, {JointKind::Distance, {}, {}, {}, {}, 0, 3});
    scene.seconds(2);
    CHECK(bodyPose(*scene.world, block).position.y == doctest::Approx(5 - 3).epsilon(0.03));
}

TEST_CASE("A joint whose other body goes lets go")
{
    Scene scene;
    const RigidBody body = dynamic();
    const ecs::Entity hook = scene.add(sphere(0.1f), {0, 5, 0});
    const ecs::Entity ball = scene.add(sphere(0.2f), {0, 3, 0}, &body);
    join(scene, ball, {JointKind::Distance, hook});
    scene.seconds(0.5);
    CHECK(bodyPose(*scene.world, ball).position.y == doctest::Approx(3).epsilon(0.02));
    ecs::destroyEntity(*scene.world, hook);
    scene.seconds(1);
    CHECK(bodyPose(*scene.world, ball).position.y < 0);
}
