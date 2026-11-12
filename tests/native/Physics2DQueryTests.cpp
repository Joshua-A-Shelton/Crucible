// 2D queries on Box2D (ADR 0093), as 3D's (ADR 0090): rays, casts, and
// overlaps, filtered by layers, an entity left out, and triggers.

#include "Physics2DTestSupport.h"

using namespace crucible;
using namespace crucible::physics2d;
using namespace crucible::physics2d::testing;

TEST_CASE("A 2D ray hits the first collider along it and goes on past the one it starts in")
{
    Scene2D scene;
    const ecs::Entity floor = ground2D(scene);
    const ecs::Entity block = scene.add(box2D({1, 1}), {0, 2});
    scene.step();
    const RayHit2D hit = physics2d::raycast(*scene.world, {0, 10}, {0, -1}, 100, physics::defaultQueryFilter());
    REQUIRE(hit.hit);
    CHECK(hit.entity.value == block.value);
    CHECK(hit.point.y == doctest::Approx(2.5f).epsilon(0.001));
    CHECK(hit.normal.y == doctest::Approx(1).epsilon(0.001));
    CHECK(hit.distance == doctest::Approx(7.5f).epsilon(0.001));
    const RayHit2D inside = physics2d::raycast(*scene.world, {0, 2}, {0, -1}, 100, physics::defaultQueryFilter());
    CHECK(inside.entity.value == floor.value);
    CHECK_FALSE(physics2d::raycast(*scene.world, {0, 10}, {0, -1}, 7, physics::defaultQueryFilter()).hit);
}

TEST_CASE("2D shapes swept along a ray stop where they first touch")
{
    Scene2D scene;
    scene.add(box2D({1, 1}), {0, 2});
    scene.step();
    const RayHit2D circle = shapeCast(*scene.world, {ShapeKind2D::Circle, {0.5f, 0}}, {0, 10}, 0, {0, -1}, 100,
                                      physics::defaultQueryFilter());
    CHECK(circle.distance == doctest::Approx(7).epsilon(0.01));
    const RayHit2D box =
        shapeCast(*scene.world, {ShapeKind2D::Box, {2, 0.2f}}, {-10, 2}, 0, {1, 0}, 100, physics::defaultQueryFilter());
    CHECK(box.distance == doctest::Approx(10 - 0.5f - 1).epsilon(0.01));
    const RayHit2D turned = shapeCast(*scene.world, {ShapeKind2D::Box, {2, 0.2f}}, {-10, 2}, 1.5707963f, {1, 0}, 100,
                                      physics::defaultQueryFilter());
    CHECK(turned.distance == doctest::Approx(10 - 0.5f - 0.1f).epsilon(0.01));
}

TEST_CASE("A 2D overlap finds each entity once and counts past what fits")
{
    Scene2D scene;
    ground2D(scene);
    scene.add(box2D({1, 1}), {0, 2});
    scene.add(box2D({1, 1}), {1.2f, 2});
    scene.step();
    ecs::Entity found[2]{};
    CHECK(overlap(*scene.world, {ShapeKind2D::Box, {4, 6}}, {0, 1}, 0, physics::defaultQueryFilter(), found) == 3);
    CHECK(found[1].value != 0);
}

TEST_CASE("2D queries follow their filter")
{
    Scene2D scene;
    const ecs::Entity floor = ground2D(scene);
    Collider2D zone = box2D({1, 1});
    zone.isTrigger = true;
    const ecs::Entity trigger = scene.add(zone, {0, 2});
    const ecs::Entity block = scene.add(box2D({1, 1}), {3, 2});
    scene.step();
    physics::QueryFilter filter = physics::defaultQueryFilter();
    CHECK(physics2d::raycast(*scene.world, {0, 10}, {0, -1}, 100, filter).entity.value ==
          floor.value); // past the trigger
    filter.triggers = true;
    CHECK(physics2d::raycast(*scene.world, {0, 10}, {0, -1}, 100, filter).entity.value == trigger.value);
    filter = physics::defaultQueryFilter();
    filter.ignore = block;
    CHECK(physics2d::raycast(*scene.world, {3, 10}, {0, -1}, 100, filter).entity.value == floor.value);
    filter = physics::defaultQueryFilter();
    filter.layers = 1u << 5; // nothing's there
    CHECK_FALSE(physics2d::raycast(*scene.world, {3, 10}, {0, -1}, 100, filter).hit);
}
