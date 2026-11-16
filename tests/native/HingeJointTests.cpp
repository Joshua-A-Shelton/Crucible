// Hinges on Box3D (ADR 0092): turning about their axis round a fixed
// anchor, within their limits when set, freely when not.

#include "PhysicsTestSupport.h"

using namespace crucible;
using namespace crucible::physics;
using namespace crucible::physics::testing;

namespace {

// A plank hanging from a hinge to the world at its top, its axis along z,
// pushed sideways.
ecs::Entity plank(Scene& scene, float lower, float upper)
{
    RigidBody body = dynamic();
    body.linearVelocity = {8, 0, 0};
    const ecs::Entity entity = scene.add(box({0.2f, 2, 0.2f}), {0, 4, 0}, &body);
    join(scene, entity, {JointKind::Hinge, {}, {0, 1, 0}, {}, {0, 0, 1}, lower, upper});
    return entity;
}

} // namespace

TEST_CASE("A hinge turns about its axis round its anchor")
{
    Scene scene;
    const ecs::Entity hanging = plank(scene, 0, 0); // no limits
    float most = 0;
    for (int i = 0; i < 60; ++i)
    {
        scene.step();
        most = std::max(most, tilt(scene, hanging));
        const BodyPose pose = bodyPose(*scene.world, hanging);
        // The top (its anchor) stays at the hinge, 5 m up; it only turns about z.
        const Vector3 top = pose.position + rotate(Vector3{0, 1, 0}, pose.rotation);
        CHECK(top.y == doctest::Approx(5).epsilon(0.01));
        CHECK(std::fabs(top.x) < 0.05f);
        CHECK(std::fabs(pose.position.z) < 0.01f);
    }
    CHECK(most > 1.5f); // swung well up, unlimited
}

TEST_CASE("A hinge's limits are the entity's own turn")
{
    // A plank held at its left end, turning about +Z, limited to 0..0.8:
    // pushed either way, it stays within them, reaching 0.8 the positive way.
    for (const float push : {-8.0f, 8.0f})
    {
        CAPTURE(push);
        Scene scene;
        RigidBody body = dynamic();
        body.gravityScale = 0;
        body.angularVelocity = {0, 0, push};
        const ecs::Entity plank = scene.add(box({2, 0.2f, 0.2f}), {1, 4, 0}, &body);
        join(scene, plank, {JointKind::Hinge, {}, {-1, 0, 0}, {}, {0, 0, 1}, 0, 0.8f});
        float lowest = 0, highest = 0;
        for (int i = 0; i < 60; ++i)
        {
            scene.step();
            const Quaternion q = bodyPose(*scene.world, plank).rotation;
            const float angle = 2 * std::atan2(q.z, q.w); // about +Z
            lowest = std::min(lowest, angle);
            highest = std::max(highest, angle);
        }
        CHECK(lowest > -0.05f);
        CHECK(highest < 0.85f);
        if (push > 0)
        {
            CHECK(highest > 0.7f);
        }
    }
}

TEST_CASE("A hinge's limits hold it within them")
{
    Scene scene;
    const ecs::Entity hanging = plank(scene, -0.5f, 0.5f);
    float most = 0;
    for (int i = 0; i < 90; ++i)
    {
        scene.step();
        most = std::max(most, tilt(scene, hanging));
    }
    CHECK(most < 0.5f + 0.05f);
    CHECK(most > 0.4f);
}
