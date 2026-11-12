// 2D bodies on Box2D (ADR 0093): falling and resting, turning, every
// collider shape, kinematic bodies pushing, layers, interpolation, and the
// depth and scale a Transform2D keeps.

#include "Physics2DTestSupport.h"

using namespace crucible;
using namespace crucible::physics2d;
using namespace crucible::physics2d::testing;

TEST_CASE("A 2D body falls and comes to rest on the ground")
{
    Scene2D scene;
    ground2D(scene);
    const RigidBody2D body = dynamic2D();
    const ecs::Entity block = scene.add(box2D({1, 1}), {0, 5}, &body);
    scene.seconds(2);
    CHECK(scene.transform(block).position.y == doctest::Approx(0.5f).epsilon(0.02));
    CHECK(bodyPose(*scene.world, block).found);
    CHECK(std::fabs(ecs::read<RigidBody2D>(*scene.world, block)->linearVelocity.y) < 0.05f);
}

TEST_CASE("A spinning 2D body's angle is written back")
{
    Scene2D scene;
    RigidBody2D body = dynamic2D();
    body.gravityScale = 0;
    body.angularVelocity = 2;
    const ecs::Entity spinner = scene.add(circle2D(0.5f), {0, 0}, &body);
    scene.seconds(1);
    CHECK(scene.transform(spinner).angle == doctest::Approx(2).epsilon(0.02));
}

TEST_CASE("Every 2D collider shape collides")
{
    Scene2D scene;
    ground2D(scene);
    const RigidBody2D body = dynamic2D();
    Collider2D capsule{};
    capsule.shape = ShapeKind2D::Capsule;
    capsule.size = {0.25f, 1};
    Collider2D triangle{};
    triangle.shape = ShapeKind2D::Polygon;
    triangle.points[0] = {-0.5f, 0};
    triangle.points[1] = {0.5f, 0};
    triangle.points[2] = {0, 1};
    triangle.pointCount = 3;
    const ecs::Entity ball = scene.add(circle2D(0.3f), {-3, 4}, &body);
    const ecs::Entity pill = scene.add(capsule, {0, 4}, &body);
    const ecs::Entity wedge = scene.add(triangle, {3, 4}, &body);
    Collider2D line{};
    line.shape = ShapeKind2D::Polygon;
    line.pointCount = 2; // fewer than 3: no shape
    const ecs::Entity nothing = scene.add(line, {6, 4}, &body);
    scene.seconds(2);
    CHECK(scene.transform(ball).position.y == doctest::Approx(0.3f).epsilon(0.03));
    CHECK(scene.transform(pill).position.y > 0.2f);
    CHECK(scene.transform(wedge).position.y == doctest::Approx(0).epsilon(0.05));
    ecs::Entity found[1]{};
    CHECK(physics2d::overlap(*scene.world, {ShapeKind2D::Circle, {0.5f, 0}}, scene.transform(nothing).position, 0,
                             physics::defaultQueryFilter(), found) == 0); // nothing there to hit
}

TEST_CASE("A moved kinematic 2D body pushes a dynamic one")
{
    Scene2D scene;
    ground2D(scene);
    RigidBody2D kinematic = dynamic2D();
    kinematic.kind = physics::BodyKind::Kinematic;
    const ecs::Entity pusher = scene.add(box2D({1, 1}), {-2, 0.5f}, &kinematic);
    const RigidBody2D body = dynamic2D();
    const ecs::Entity block = scene.add(box2D({1, 1}), {0, 0.5f}, &body);
    scene.step();
    for (int i = 0; i < 60; ++i)
    {
        ecs::get<scenes::Transform2D>(*scene.world, pusher)->position.x += 0.05f;
        scene.step();
    }
    CHECK(scene.transform(block).position.x > 1.5f);
}

TEST_CASE("2D bodies on layers that don't meet pass through each other")
{
    Scene2D scene;
    RigidBody2D floorBody{physics::BodyKind::Static, 0, {}, 0, 1, 1u << 1, 1u << 1};
    scene.add(box2D({40, 1}), {0, -0.5f}, &floorBody); // on layer 2, meeting only 2
    RigidBody2D body = dynamic2D();
    body.layers = 1;
    body.mask = 1;
    const ecs::Entity faller = scene.add(box2D({1, 1}), {0, 2}, &body);
    scene.seconds(1.5);
    CHECK(scene.transform(faller).position.y < -1);
}

TEST_CASE("A 2D body's Transform2D is interpolated between ticks and keeps its depth and scale")
{
    Scene2D scene;
    RigidBody2D body = dynamic2D();
    body.gravityScale = 0;
    body.linearVelocity = {6, 0};
    const ecs::Entity mover = scene.add(box2D({1, 1}), {0, 0}, &body);
    ecs::get<scenes::Transform2D>(*scene.world, mover)->depth = 3;
    ecs::get<scenes::Transform2D>(*scene.world, mover)->scale = {2, 2};
    scene.step(1, 0);
    scene.step(1, 0.5);
    // Halfway between the ticks at 0.1 m and 0.2 m.
    CHECK(scene.transform(mover).position.x == doctest::Approx(0.15f).epsilon(0.02));
    CHECK(scene.transform(mover).depth == 3);
    CHECK(scene.transform(mover).scale.x == 2);
}
