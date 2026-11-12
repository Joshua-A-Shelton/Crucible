// Ball-and-socket and swing-twist joints on Box3D's spherical joint (ADR
// 0092): turning any way round a fixed anchor, and within a cone and a
// twist when limited.

#include "PhysicsTestSupport.h"

using namespace crucible;
using namespace crucible::physics;
using namespace crucible::physics::testing;

namespace {

// A bar hanging from a ceiling block by its top, pushed sideways both ways.
ecs::Entity bar(Scene& scene, JointKind kind, float swing, float twist)
{
    const ecs::Entity ceiling = scene.add(box({1, 0.2f, 1}), {0, 5.1f, 0});
    RigidBody body = dynamic();
    body.linearVelocity = {6, 0, 4};
    const ecs::Entity entity = scene.add(box({0.2f, 2, 0.2f}), {0, 4, 0}, &body);
    join(scene, entity, {kind, ceiling, {0, 1, 0}, {0, -0.1f, 0}, {0, -1, 0}, 0, 0, swing, twist});
    return entity;
}

} // namespace

TEST_CASE("A ball-and-socket turns any way round its anchor")
{
    Scene scene;
    const ecs::Entity hanging = bar(scene, JointKind::BallSocket, 0, 0);
    float most = 0;
    bool movedZ = false;
    for (int i = 0; i < 60; ++i)
    {
        scene.step();
        most = std::max(most, tilt(scene, hanging));
        const BodyPose pose = bodyPose(*scene.world, hanging);
        const Vector3 top = pose.position + rotate(Vector3{0, 1, 0}, pose.rotation);
        CHECK(length(top - Vector3{0, 5, 0}) < 0.05f); // held at the socket
        movedZ = movedZ || std::fabs(pose.position.z) > 0.3f;
    }
    CHECK(most > 1.0f);
    CHECK(movedZ); // not only in one plane
}

TEST_CASE("A swing-twist joint keeps within its cone")
{
    Scene scene;
    const ecs::Entity hanging = bar(scene, JointKind::SwingTwist, 0.4f, 0.2f);
    float most = 0;
    for (int i = 0; i < 90; ++i)
    {
        scene.step();
        most = std::max(most, tilt(scene, hanging));
    }
    CHECK(most < 0.4f + 0.05f);
    CHECK(most > 0.3f);
}

TEST_CASE("A swing-twist joint without limits is a ball-and-socket")
{
    Scene scene;
    const ecs::Entity hanging = bar(scene, JointKind::SwingTwist, 0, 0);
    float most = 0;
    for (int i = 0; i < 60; ++i)
    {
        scene.step();
        most = std::max(most, tilt(scene, hanging));
    }
    CHECK(most > 1.0f);
}
