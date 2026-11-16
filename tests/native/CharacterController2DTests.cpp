// 2D characters on Box2D's mover (ADR 0094), as 3D's (ADR 0091): walking
// and stopping at walls, over low steps but not high ones, up walkable
// slopes but not steep ones, falling, jumping, pushing bodies, through
// triggers, and met by bodies and queries.

#include "Physics2DTestSupport.h"

using namespace crucible;
using namespace crucible::physics2d;
using namespace crucible::physics2d::testing;

namespace {

ecs::Entity polygon(Scene2D& scene, std::initializer_list<Vector2> points)
{
    Collider2D collider{};
    collider.shape = ShapeKind2D::Polygon;
    for (const Vector2 point : points)
    {
        collider.points[collider.pointCount++] = point;
    }
    return scene.add(collider, {0, 0});
}

} // namespace

TEST_CASE("A 2D character walks along the ground and stops at a wall")
{
    Scene2D scene;
    ground2D(scene);
    scene.add(box2D({1, 4}), {3, 2});
    const ecs::Entity walker = character2D(scene, {0, 0});
    scene.step();
    const CharacterMove2D moved = walk2D(scene, walker, {0.1f, 0}, 60);
    CHECK(moved.grounded);
    CHECK(moved.position.y == doctest::Approx(0).epsilon(0.02));
    CHECK(moved.position.x == doctest::Approx(3 - 0.5f - 0.4f).epsilon(0.02));
}

TEST_CASE("A 2D character climbs a low step and is blocked by a high one")
{
    for (const float height : {0.2f, 0.5f})
    {
        CAPTURE(height);
        Scene2D scene;
        ground2D(scene);
        scene.add(box2D({4, height}), {3, height / 2}); // from x = 1
        const ecs::Entity walker = character2D(scene, {0, 0});
        scene.step();
        const CharacterMove2D moved = walk2D(scene, walker, {0.05f, 0}, 60);
        if (height < 0.3f)
        {
            CHECK(moved.position.x > 2.5f);
            CHECK(moved.position.y == doctest::Approx(height).epsilon(0.02));
        }
        else
        {
            CHECK(moved.position.x < 1);
            CHECK(moved.position.y == doctest::Approx(0).epsilon(0.02));
        }
    }
}

TEST_CASE("A 2D character walks up a walkable slope but not a steep one")
{
    {
        Scene2D scene;
        ground2D(scene);
        polygon(scene, {{0, 0}, {6, 0}, {6, 6 * 0.5773503f}}); // 30 degrees
        const ecs::Entity walker = character2D(scene, {-1, 0});
        scene.step();
        const CharacterMove2D moved = walk2D(scene, walker, {0.05f, 0}, 80);
        CHECK(moved.grounded);
        CHECK(moved.position.y > 1.0f);
    }
    {
        Scene2D scene;
        ground2D(scene);
        polygon(scene, {{0, 0}, {3, 0}, {3, 3 * 1.7320508f}}); // 60 degrees
        const ecs::Entity walker = character2D(scene, {-1, 0});
        scene.step();
        const CharacterMove2D moved = walk2D(scene, walker, {0.05f, 0}, 80);
        CHECK(moved.position.y < 0.35f);
        CHECK(moved.position.x < 0.5f);
    }
}

TEST_CASE("A 2D character falls lands and jumps")
{
    Scene2D scene;
    ground2D(scene);
    const ecs::Entity jumper = character2D(scene, {0, 3});
    scene.step();
    CHECK_FALSE(walk2D(scene, jumper, {}, 10).grounded);
    const CharacterMove2D landed = walk2D(scene, jumper, {}, 90);
    CHECK(landed.grounded);
    CHECK(landed.position.y == doctest::Approx(0).epsilon(0.02));
    launchCharacter2D(*scene.world, jumper, 5);
    float highest = 0;
    for (int i = 0; i < 90; ++i)
    {
        highest = std::max(highest, walk2D(scene, jumper, {}, 1).position.y);
    }
    CHECK(highest == doctest::Approx(25 / (2 * 9.81f)).epsilon(0.05));
    CHECK(scene.transform(jumper).position.y == doctest::Approx(0).epsilon(0.02));
}

TEST_CASE("A 2D character pushes a ball and walks through a trigger")
{
    Scene2D scene;
    ground2D(scene);
    Collider2D zone = box2D({1, 2});
    zone.isTrigger = true;
    scene.add(zone, {1, 1});
    const RigidBody2D body = dynamic2D();
    const ecs::Entity ball = scene.add(circle2D(0.3f), {3, 0.3f}, &body);
    const ecs::Entity walker = character2D(scene, {0, 0});
    scene.step();
    walk2D(scene, walker, {0.05f, 0}, 70);
    CHECK(scene.transform(walker).position.x > 2.5f); // through the trigger at x = 1
    CHECK(scene.transform(ball).position.x > 3.5f);   // and the ball pushed on
}

TEST_CASE("Bodies and queries meet a 2D character's capsule")
{
    Scene2D scene;
    ground2D(scene);
    const ecs::Entity walker = character2D(scene, {0, 0});
    const RigidBody2D body = dynamic2D();
    const ecs::Entity ball = scene.add(circle2D(0.2f), {0, 4}, &body);
    scene.seconds(2);
    // Its capsule reaches 0.4 + 0.3 + 1 + 0.4 = 2.1 m up: the ball rests on it.
    CHECK(scene.transform(ball).position.y == doctest::Approx(2.1f + 0.2f).epsilon(0.02));
    const RayHit2D hit = physics2d::raycast(*scene.world, {3, 1}, {-1, 0}, 10, physics::defaultQueryFilter());
    REQUIRE(hit.hit);
    CHECK(hit.entity.value == walker.value);
}
