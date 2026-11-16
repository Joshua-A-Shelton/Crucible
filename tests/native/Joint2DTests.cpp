// 2D joints on Box2D (ADR 0094): fixed to the world, hinges about Z within
// their limits, rods and ropes, sliders along their axis, and letting go
// when the other body goes.

#include "Physics2DTestSupport.h"

using namespace crucible;
using namespace crucible::physics2d;
using namespace crucible::physics2d::testing;

namespace {

float apart(Scene2D& scene, ecs::Entity a, ecs::Entity b)
{
    return length(scene.transform(a).position - scene.transform(b).position);
}

} // namespace

TEST_CASE("A fixed 2D joint to the world holds a body where it is")
{
    Scene2D scene;
    const RigidBody2D body = dynamic2D();
    const ecs::Entity held = scene.add(box2D({1, 1}), {0, 5}, &body);
    join2D(scene, held, {JointKind2D::Fixed});
    scene.seconds(2);
    CHECK(scene.transform(held).position.y == doctest::Approx(5).epsilon(0.01));
}

TEST_CASE("A 2D hinge turns round its anchor and keeps within its limits")
{
    for (const bool limited : {false, true})
    {
        CAPTURE(limited);
        Scene2D scene;
        RigidBody2D body = dynamic2D();
        body.linearVelocity = {8, 0};
        const ecs::Entity plank = scene.add(box2D({0.2f, 2}), {0, 4}, &body);
        join2D(scene, plank, {JointKind2D::Hinge, {}, {0, 1}, {}, {}, limited ? -0.5f : 0, limited ? 0.5f : 0});
        float most = 0;
        for (int i = 0; i < 90; ++i)
        {
            scene.step();
            const scenes::Transform2D& t = scene.transform(plank);
            most = std::max(most, std::fabs(t.angle));
            // Its top (the anchor) stays at the hinge, 5 m up.
            const Vector2 top = t.position + Vector2{-std::sin(t.angle), std::cos(t.angle)};
            CHECK(length(top - Vector2{0, 5}) < 0.05f);
        }
        if (limited)
        {
            CHECK(most < 0.55f);
            CHECK(most > 0.4f);
        }
        else
        {
            CHECK(most > 1.5f);
        }
    }
}

TEST_CASE("A 2D hinge's limits are the entity's own turn")
{
    // Limited to 0..0.8 radians: pushed the positive way (+X toward +Y,
    // counterclockwise) it goes to 0.8; pushed the other way, no further than 0.
    for (const float push : {-8.0f, 8.0f})
    {
        CAPTURE(push);
        Scene2D scene;
        RigidBody2D body = dynamic2D();
        body.gravityScale = 0;
        body.angularVelocity = push;
        const ecs::Entity plank = scene.add(box2D({2, 0.2f}), {1, 4}, &body);
        join2D(scene, plank, {JointKind2D::Hinge, {}, {-1, 0}, {}, {}, 0, 0.8f});
        float lowest = 0, highest = 0;
        for (int i = 0; i < 60; ++i)
        {
            scene.step();
            lowest = std::min(lowest, scene.transform(plank).angle);
            highest = std::max(highest, scene.transform(plank).angle);
        }
        CHECK(lowest > -0.05f);
        CHECK(highest < 0.85f);
        if (push > 0)
        {
            CHECK(highest > 0.7f);
        }
    }
}

TEST_CASE("A 2D distance joint is a rod without a range and a rope with one")
{
    {
        Scene2D scene;
        const ecs::Entity hook = scene.add(circle2D(0.1f), {0, 5});
        RigidBody2D body = dynamic2D();
        body.linearVelocity = {3, 0};
        const ecs::Entity ball = scene.add(circle2D(0.2f), {0, 3}, &body);
        join2D(scene, ball, {JointKind2D::Distance, hook});
        for (int i = 0; i < 90; ++i)
        {
            scene.step();
            CHECK(apart(scene, ball, hook) == doctest::Approx(2).epsilon(0.02));
        }
    }
    {
        Scene2D scene;
        const ecs::Entity hook = scene.add(circle2D(0.1f), {0, 5});
        const RigidBody2D body = dynamic2D();
        const ecs::Entity ball = scene.add(circle2D(0.2f), {0, 4}, &body);
        join2D(scene, ball, {JointKind2D::Distance, hook, {}, {}, {}, 0.5f, 2.5f});
        scene.seconds(2);
        CHECK(apart(scene, ball, hook) == doctest::Approx(2.5f).epsilon(0.02));
    }
}

TEST_CASE("A 2D slider moves only along its axis and within its limits")
{
    Scene2D scene;
    RigidBody2D body = dynamic2D();
    body.linearVelocity = {3, 0};
    const ecs::Entity cart = scene.add(box2D({1, 0.5f}), {0, 4}, &body);
    join2D(scene, cart, {JointKind2D::Slider, {}, {}, {}, {1, 0}, -1, 2});
    scene.seconds(2);
    const scenes::Transform2D& t = scene.transform(cart);
    CHECK(t.position.y == doctest::Approx(4).epsilon(0.01)); // gravity can't take it off the axis
    CHECK(t.position.x == doctest::Approx(2).epsilon(0.02)); // stopped at the upper limit
    CHECK(std::fabs(t.angle) < 0.01f);
}

TEST_CASE("A 2D joint whose other body goes lets go")
{
    Scene2D scene;
    const RigidBody2D body = dynamic2D();
    const ecs::Entity hook = scene.add(circle2D(0.1f), {0, 5});
    const ecs::Entity ball = scene.add(circle2D(0.2f), {0, 3}, &body);
    join2D(scene, ball, {JointKind2D::Distance, hook});
    scene.seconds(0.5);
    CHECK(scene.transform(ball).position.y == doctest::Approx(3).epsilon(0.02));
    ecs::destroyEntity(*scene.world, hook);
    scene.seconds(1);
    CHECK(scene.transform(ball).position.y < 0);
}
