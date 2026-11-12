// Characters falling (ADR 0091): under gravity times their scale, landing,
// walking off a ledge, jumping by launchCharacter, and stopped by a ceiling.

#include "PhysicsTestSupport.h"

using namespace crucible;
using namespace crucible::physics;
using namespace crucible::physics::testing;

TEST_CASE("Off the ground a character falls under gravity and lands")
{
    Scene scene;
    ground(scene);
    const ecs::Entity faller = character(scene, {0, 5, 0});
    scene.step();
    const CharacterMove falling = walk(scene, faller, {}, 30);
    CHECK_FALSE(falling.grounded);
    CHECK(falling.fallSpeed == doctest::Approx(gravity.y * 0.5f).epsilon(0.02));
    const CharacterMove landed = walk(scene, faller, {}, 90);
    CHECK(landed.grounded);
    CHECK(landed.fallSpeed == 0);
    CHECK(landed.position.y == doctest::Approx(0).epsilon(0.02));
}

TEST_CASE("A gravity scale of 0 leaves up and down to the game")
{
    Scene scene;
    ground(scene);
    const ecs::Entity floater = character(scene, {0, 5, 0}, 0);
    scene.step();
    CHECK(walk(scene, floater, {}, 60).position.y == doctest::Approx(5));
}

TEST_CASE("A character walks off a ledge and falls")
{
    Scene scene;
    ground(scene);
    scene.add(box({2, 2, 4}), {0, 1, 0}); // a platform, its top at 2, its edge at x = 1
    const ecs::Entity walker = character(scene, {0, 2, 0});
    scene.step();
    walk(scene, walker, {0.05f, 0, 0}, 40);
    const CharacterMove landed = walk(scene, walker, {}, 90);
    CHECK(landed.position.x > 1.4f);
    CHECK(landed.position.y == doctest::Approx(0).epsilon(0.02));
    CHECK(landed.grounded);
}

TEST_CASE("Launching makes a character jump and come back down")
{
    Scene scene;
    ground(scene);
    const ecs::Entity jumper = character(scene, {0, 0, 0});
    scene.step();
    walk(scene, jumper, {}, 2);
    launchCharacter(*scene.world, jumper, 5);
    float highest = 0;
    for (int i = 0; i < 90; ++i)
    {
        highest = std::max(highest, walk(scene, jumper, {}, 1).position.y);
    }
    // v^2 / 2g: about 1.27 m.
    CHECK(highest == doctest::Approx(25 / (2 * 9.81f)).epsilon(0.05));
    CHECK(scene.position(jumper).y == doctest::Approx(0).epsilon(0.02));
}

TEST_CASE("A ceiling stops a jump")
{
    Scene scene;
    ground(scene);
    scene.add(box({4, 1, 4}), {0, 3, 0}); // its bottom at 2.5
    const ecs::Entity jumper = character(scene, {0, 0, 0});
    scene.step();
    walk(scene, jumper, {}, 2);
    launchCharacter(*scene.world, jumper, 10);
    float highest = 0;
    for (int i = 0; i < 60; ++i)
    {
        highest = std::max(highest, walk(scene, jumper, {}, 1).position.y);
    }
    // The capsule's top (2.1 m above the feet) meets the ceiling.
    CHECK(highest <= 2.5f - 2.1f + 0.02f);
}
