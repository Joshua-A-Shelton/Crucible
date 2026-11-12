// Distance joints on Box3D (ADR 0092): a rod keeping the length it starts
// with, and a rope free between its limits.

#include "PhysicsTestSupport.h"

using namespace crucible;
using namespace crucible::physics;
using namespace crucible::physics::testing;

namespace {

float apart(Scene& scene, ecs::Entity a, ecs::Entity b)
{
    return length(bodyPose(*scene.world, a).position - scene.position(b));
}

} // namespace

TEST_CASE("A distance joint without a range keeps the length it starts with")
{
    Scene scene;
    const ecs::Entity hook = scene.add(sphere(0.1f), {0, 5, 0}); // static
    RigidBody body = dynamic();
    body.linearVelocity = {3, 0, 0};
    const ecs::Entity ball = scene.add(sphere(0.2f), {0, 3, 0}, &body);
    join(scene, ball, {JointKind::Distance, hook});
    float widest = 0;
    for (int i = 0; i < 90; ++i)
    {
        scene.step();
        CHECK(apart(scene, ball, hook) == doctest::Approx(2).epsilon(0.02));
        widest = std::max(widest, std::fabs(bodyPose(*scene.world, ball).position.x));
    }
    CHECK(widest > 1.0f); // it swung, about 1.3 m out
}

TEST_CASE("A distance joint with a range is a rope between its limits")
{
    Scene scene;
    const ecs::Entity hook = scene.add(sphere(0.1f), {0, 5, 0});
    const RigidBody body = dynamic();
    const ecs::Entity ball = scene.add(sphere(0.2f), {0, 4, 0}, &body); // 1 m: slack
    join(scene, ball, {JointKind::Distance, hook, {}, {}, {}, 0.5f, 2.5f});
    scene.seconds(2);
    CHECK(apart(scene, ball, hook) == doctest::Approx(2.5f).epsilon(0.02)); // fell to the end
}
