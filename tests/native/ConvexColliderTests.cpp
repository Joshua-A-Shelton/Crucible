// Convex colliders (ADR 0086), from hulls made at run time: a hull resting
// at its own shape, its offset honoured; the vertex limit; too few points;
// outlines.

#include "PhysicsTestSupport.h"

using namespace crucible;
using namespace crucible::physics;
using namespace crucible::physics::testing;

namespace {

// A unit cube's corners, centred on (0, `up`, 0).
assets::AssetId cubeHull(float up = 0)
{
    Vector3 corners[8];
    for (int i = 0; i < 8; ++i)
    {
        corners[i] = {(i & 1) ? 0.5f : -0.5f, up + ((i & 2) ? 0.5f : -0.5f), (i & 4) ? 0.5f : -0.5f};
    }
    return createCollisionHull(corners, 32);
}

Collider hullOf(assets::AssetId id, Vector3 offset = {})
{
    return {ShapeKind::Convex, {}, offset, 0.6f, 0, false, id, 0};
}

} // namespace

TEST_CASE("A convex body rests on the ground at its own shape")
{
    Scene scene;
    ground(scene);
    const assets::AssetId cube = cubeHull();
    const assets::AssetId raised = cubeHull(1); // its points a metre above the origin
    const RigidBody body = dynamic();
    const ecs::Entity plain = scene.add(hullOf(cube), {0, 2, 0}, &body);
    const ecs::Entity offset = scene.add(hullOf(cube, {0, 1, 0}), {3, 3, 0}, &body);
    const ecs::Entity high = scene.add(hullOf(raised), {-3, 3, 0}, &body);
    scene.seconds(3.0);
    CHECK(bodyPose(*scene.world, plain).position.y == doctest::Approx(0.5f).epsilon(0.02));
    CHECK(bodyPose(*scene.world, offset).position.y == doctest::Approx(-0.5f).epsilon(0.02));
    CHECK(bodyPose(*scene.world, high).position.y == doctest::Approx(-0.5f).epsilon(0.02));
    destroyCollisionShape(cube);
    destroyCollisionShape(raised);
}

TEST_CASE("A hull keeps at most its vertex limit and needs a solid's points")
{
    // Points on a sphere: a hull of them all would have far more vertices.
    std::vector<Vector3> points;
    for (int i = 0; i < 200; ++i)
    {
        const float y = 1 - 2 * (static_cast<float>(i) + 0.5f) / 200;
        const float r = std::sqrt(1 - y * y), a = 2.39996f * static_cast<float>(i);
        points.push_back({r * std::cos(a), y, r * std::sin(a)});
    }
    const assets::AssetId hull = createCollisionHull(points, 8);
    REQUIRE(hull.bytes[0] != 0);
    Scene scene;
    scene.add(hullOf(hull), {0, 0, 0});
    debug::clearDebugLines();
    drawColliders(*scene.world, {1, 1, 1, 1});
    CHECK(debug::debugLineCount() > 0);
    CHECK(debug::debugLineCount() <= 3 * 8 - 6); // a polyhedron of 8 vertices has at most 18 edges
    destroyCollisionShape(hull);
    const Vector3 flat[] = {{0, 0, 0}, {1, 0, 0}, {0, 0, 1}};
    CHECK(createCollisionHull(flat, 32).bytes[0] == 0);
}

TEST_CASE("A convex cube is outlined by its twelve edges")
{
    Scene scene;
    const assets::AssetId cube = cubeHull();
    scene.add(hullOf(cube), {0, 0, 0});
    debug::clearDebugLines();
    drawColliders(*scene.world, {1, 1, 1, 1});
    CHECK(debug::debugLineCount() == 12);
    destroyCollisionShape(cube);
}
