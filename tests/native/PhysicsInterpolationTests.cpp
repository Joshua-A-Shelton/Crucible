// Physics.Interpolate (ADR 0085): a dynamic body's transform between its
// last two ticks by the frame's fixed alpha, not mistaken for a teleport.

#include "PhysicsTestSupport.h"

using namespace crucible;
using namespace crucible::physics;
using namespace crucible::physics::testing;

TEST_CASE("Interpolation places a body between its last two ticks")
{
    Scene scene;
    RigidBody body = dynamic();
    body.gravityScale = 0;
    body.linearVelocity = {6, 0, 0}; // 0.1 m a tick
    const ecs::Entity ball = scene.add(sphere(0.5f), {0, 5, 0}, &body);
    scene.step(1, 0.0);
    scene.step(1, 0.0);
    const float tick = bodyPose(*scene.world, ball).position.x;
    scene.step(0, 0.5); // a frame without a tick, half way to the next
    CHECK(scene.position(ball).x == doctest::Approx(tick - 0.05f).epsilon(1e-4));
    scene.step(0, 1.0);
    CHECK(scene.position(ball).x == doctest::Approx(tick).epsilon(1e-4));
}

TEST_CASE("An interpolated transform isn't taken for a teleport")
{
    Scene scene;
    RigidBody body = dynamic();
    body.gravityScale = 0;
    body.linearVelocity = {6, 0, 0};
    const ecs::Entity ball = scene.add(sphere(0.5f), {0, 5, 0}, &body);
    for (int i = 0; i < 30; ++i)
    {
        scene.step(1, 0.37);
    }
    // Half a second at 6 m/s: unhindered by the transforms written between.
    CHECK(bodyPose(*scene.world, ball).position.x == doctest::Approx(3).epsilon(1e-3));
}
