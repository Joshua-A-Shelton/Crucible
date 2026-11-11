// Mesh colliders (ADR 0086), from collision data made at run time: a ball
// resting on a mesh ground, scaled; meshes only on static bodies; destroyed
// data taking the shape away; outlines from the data.

#include "PhysicsTestSupport.h"

using namespace crucible;
using namespace crucible::physics;
using namespace crucible::physics::testing;

namespace {

// A square of two triangles at y = 0, facing up (clockwise from above, as
// Crucible's front faces are), `half` metres from the middle each way.
assets::AssetId floorMesh(float half = 10)
{
    const Vector3 corners[] = {{-half, 0, -half}, {half, 0, -half}, {half, 0, half}, {-half, 0, half}};
    const uint32_t triangles[] = {0, 2, 1, 0, 3, 2};
    return createCollisionMesh(corners, triangles);
}

Collider meshOf(assets::AssetId id)
{
    return {ShapeKind::Mesh, {}, {}, 0.6f, 0, false, id, 0};
}

} // namespace

TEST_CASE("A ball rests on a mesh made at run time")
{
    Scene scene;
    const assets::AssetId floor = floorMesh();
    REQUIRE(floor.bytes[0] != 0);
    scene.add(meshOf(floor), {0, 0, 0});
    const RigidBody body = dynamic();
    const ecs::Entity ball = scene.add(sphere(0.5f), {2, 2, -3}, &body);
    scene.seconds(2.0);
    CHECK(bodyPose(*scene.world, ball).position.y == doctest::Approx(0.5f).epsilon(0.02));
    destroyCollisionShape(floor);
}

TEST_CASE("A mesh scales with its entity")
{
    Scene scene;
    const assets::AssetId floor = floorMesh(0.5f); // a metre square
    scene.add(meshOf(floor), {0, 0, 0}, nullptr, {10, 1, 10});
    const RigidBody body = dynamic();
    const ecs::Entity ball = scene.add(sphere(0.5f), {4, 2, 4}, &body);
    scene.seconds(2.0);
    CHECK(bodyPose(*scene.world, ball).position.y == doctest::Approx(0.5f).epsilon(0.02));
    destroyCollisionShape(floor);
}

TEST_CASE("A mesh collider on a moving body makes no shape")
{
    Scene scene;
    const assets::AssetId mesh = floorMesh();
    RigidBody floating = dynamic();
    floating.gravityScale = 0;
    scene.add(meshOf(mesh), {0, 0, 0}, &floating);
    const RigidBody body = dynamic();
    const ecs::Entity ball = scene.add(sphere(0.5f), {0, 2, 0}, &body);
    scene.seconds(1.0);
    CHECK(bodyPose(*scene.world, ball).position.y < -1); // straight through: there's nothing to land on
    destroyCollisionShape(mesh);
}

TEST_CASE("Destroying made data takes its colliders' shapes away")
{
    Scene scene;
    const assets::AssetId floor = floorMesh();
    scene.add(meshOf(floor), {0, 0, 0});
    const RigidBody body = dynamic();
    const ecs::Entity ball = scene.add(sphere(0.5f), {0, 1, 0}, &body);
    scene.seconds(1.0);
    CHECK(bodyPose(*scene.world, ball).position.y == doctest::Approx(0.5f).epsilon(0.02));
    destroyCollisionShape(floor);
    scene.seconds(1.0);
    CHECK(bodyPose(*scene.world, ball).position.y < -1);
}

TEST_CASE("A mesh collider is outlined by its triangles")
{
    Scene scene;
    const assets::AssetId floor = floorMesh();
    scene.add(meshOf(floor), {0, 0, 0});
    debug::clearDebugLines();
    drawColliders(*scene.world, {1, 1, 1, 1});
    CHECK(debug::debugLineCount() == 6);
    destroyCollisionShape(floor);
    const Vector3 none[] = {{0, 0, 0}};
    const uint32_t bad[] = {0, 1, 2};
    CHECK(createCollisionMesh(none, bad).bytes[0] == 0); // indices past the vertices
}
