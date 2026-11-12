// Fixed joints on Box3D (ADR 0092): a body held where it is to the world,
// and two welded bodies falling as one.

#include "PhysicsTestSupport.h"

using namespace crucible;
using namespace crucible::physics;
using namespace crucible::physics::testing;

TEST_CASE("A fixed joint to the world holds a body where it is")
{
    Scene scene;
    const RigidBody body = dynamic();
    const ecs::Entity held = scene.add(box({1, 1, 1}), {0, 5, 0}, &body);
    join(scene, held, {JointKind::Fixed});
    scene.seconds(2);
    CHECK(bodyPose(*scene.world, held).position.y == doctest::Approx(5).epsilon(0.01));
}

TEST_CASE("Welded bodies fall together and keep their offset")
{
    Scene scene;
    const RigidBody body = dynamic();
    const ecs::Entity a = scene.add(box({1, 1, 1}), {0, 5, 0}, &body);
    const ecs::Entity b = scene.add(box({1, 1, 1}), {1, 5, 0}, &body);
    join(scene, a, {JointKind::Fixed, b, {0.5f, 0, 0}, {-0.5f, 0, 0}}); // where they touch
    scene.seconds(1);
    const Vector3 pa = bodyPose(*scene.world, a).position, pb = bodyPose(*scene.world, b).position;
    CHECK(pa.y < 1);
    CHECK(pb.x - pa.x == doctest::Approx(1).epsilon(0.02));
    CHECK(pb.y - pa.y == doctest::Approx(0).epsilon(0.02));
}
