// Characters moving (ADR 0091): along the ground, stopped and slid along by
// walls, pushing bodies in their way; and bodies and queries meeting their
// kinematic capsule.

#include "PhysicsTestSupport.h"

using namespace crucible;
using namespace crucible::physics;
using namespace crucible::physics::testing;

TEST_CASE("A character walks along the ground and stops at a wall")
{
    Scene scene;
    ground(scene);
    scene.add(box({1, 4, 10}), {3, 2, 0});
    const ecs::Entity walker = character(scene, {0, 0, 0});
    scene.step();
    const CharacterMove moved = walk(scene, walker, {0.1f, 0, 0}, 60);
    CHECK(moved.grounded);
    CHECK(moved.position.y == doctest::Approx(0).epsilon(0.02));
    CHECK(moved.position.x == doctest::Approx(3 - 0.5f - 0.4f).epsilon(0.02));
    CHECK(scene.position(walker).x == moved.position.x); // its LocalTransform
}

TEST_CASE("A character slides along a wall it walks into at an angle")
{
    Scene scene;
    ground(scene);
    scene.add(box({1, 4, 20}), {2, 2, 0});
    const ecs::Entity walker = character(scene, {0, 0, 0});
    scene.step();
    const CharacterMove moved = walk(scene, walker, {0.05f, 0, 0.05f}, 60);
    CHECK(moved.position.x == doctest::Approx(2 - 0.5f - 0.4f).epsilon(0.03));
    CHECK(moved.position.z > 2.5f); // went on along the wall
}

TEST_CASE("A character pushes a body in its way")
{
    Scene scene;
    ground(scene);
    const RigidBody body = dynamic();
    const ecs::Entity ball = scene.add(sphere(0.3f), {1.5f, 0.3f, 0}, &body);
    const ecs::Entity walker = character(scene, {0, 0, 0});
    scene.step();
    walk(scene, walker, {0.05f, 0, 0}, 60);
    CHECK(scene.position(ball).x > 2.2f);
}

TEST_CASE("Bodies and queries meet a character's capsule")
{
    Scene scene;
    ground(scene);
    const ecs::Entity walker = character(scene, {0, 0, 0});
    const RigidBody body = dynamic();
    const ecs::Entity ball = scene.add(sphere(0.2f), {0, 4, 0}, &body);
    scene.seconds(2);
    // Its capsule reaches 0.4 + 0.3 + 1 + 0.4 = 2.1 m up: the ball rests on it.
    CHECK(bodyPose(*scene.world, ball).position.y == doctest::Approx(2.1f + 0.2f).epsilon(0.02));
    const RayHit hit = raycast(*scene.world, {3, 1, 0}, {-1, 0, 0}, 10, defaultQueryFilter());
    REQUIRE(hit.hit);
    CHECK(hit.entity.value == walker.value);
}
