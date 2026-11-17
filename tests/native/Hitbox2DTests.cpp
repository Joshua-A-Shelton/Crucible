// 2D swept hitboxes (ADR 0096), as 3D's: every hit along a sweep, each
// target once per activation and timed by where along the frame it touched;
// a swing's arc followed between frames; never the owner's family.

#include "Physics2DTestSupport.h"

using namespace crucible;
using namespace crucible::physics2d;
using namespace crucible::physics2d::testing;

namespace {

constexpr float pi = 3.14159265f;

ecs::Entity hitbox2DAt(Scene2D& scene, Hitbox2D hitbox, Vector2 at, ecs::Entity parent = ecs::nullEntity)
{
    const ecs::ComponentId components[] = {ecs::componentIdOf<Hitbox2D>, ecs::componentIdOf<scenes::Transform2D>,
                                           ecs::componentIdOf<scenes::WorldTransform>};
    const ecs::Entity entity = ecs::createEntity(*scene.world, components);
    *ecs::get<Hitbox2D>(*scene.world, entity) = hitbox;
    *ecs::get<scenes::Transform2D>(*scene.world, entity) = {at, 0, {1, 1}, 0};
    if (parent.value != 0)
    {
        REQUIRE(ecs::setParent(*scene.world, entity, parent));
    }
    return entity;
}

Hitbox2D disc(float radius)
{
    return {ShapeKind2D::Circle, {radius, 0}, physics::allLayers, 0, true};
}

std::vector<HitboxHit2D> hitsOf(Scene2D& scene)
{
    std::vector<HitboxHit2D> found(hitboxHits(*scene.world, {}));
    (void)hitboxHits(*scene.world, found);
    return found;
}

double shareOf(uint64_t timestamp, uint64_t frameStart, double seconds)
{
    return 1.0 - static_cast<double>(frameStart - timestamp) / (seconds * 1e9);
}

} // namespace

TEST_CASE("2D shapeCastAll finds everything along the way once each and nearest first")
{
    Scene2D scene;
    const ecs::Entity far = scene.add(box2D({1, 1}), {6, 0});
    const ecs::Entity near = scene.add(box2D({1, 1}), {2, 0});
    scene.add(box2D({1, 1}), {4, 5});
    scene.step();
    RayHit2D found[4]{};
    const uint32_t count = shapeCastAll(*scene.world, {ShapeKind2D::Circle, {0.25f, 0}}, {0, 0}, 0, {1, 0}, 10,
                                        physics::defaultQueryFilter(), found);
    REQUIRE(count == 2);
    CHECK(found[0].entity.value == near.value);
    CHECK(found[1].entity.value == far.value);
    CHECK(found[0].distance == doctest::Approx(1.25f).epsilon(0.01));
}

TEST_CASE("A 2D hitbox swept through a box hits it once and when it touched")
{
    Scene2D scene;
    const ecs::Entity target = scene.add(box2D({1, 1}), {0, 0});
    scene.step();
    const ecs::Entity fist = hitbox2DAt(scene, disc(0.1f), {-2, 0});
    uint64_t clock = 1'000'000'000;
    sweepHitboxes(*scene.world, 0.1f, clock);
    ecs::get<scenes::Transform2D>(*scene.world, fist)->position = {2, 0};
    clock += 100'000'000;
    sweepHitboxes(*scene.world, 0.1f, clock);
    std::vector<HitboxHit2D> hits = hitsOf(scene);
    REQUIRE(hits.size() == 1);
    CHECK(hits[0].target.value == target.value);
    CHECK(shareOf(hits[0].timestamp, clock, 0.1) == doctest::Approx(0.35).epsilon(0.01));
    CHECK(hits[0].normal.x == doctest::Approx(-1.0f).epsilon(0.01));
    ecs::get<scenes::Transform2D>(*scene.world, fist)->position = {-2, 0};
    clock += 100'000'000;
    sweepHitboxes(*scene.world, 0.1f, clock);
    CHECK(hitsOf(scene).empty()); // once per activation
}

TEST_CASE("A 2D swing's arc is followed between frames and spares its owner")
{
    Scene2D scene;
    // The owner's own body sits on the arc's path too.
    const ecs::Entity arm = scene.add(box2D({0.2f, 0.2f}), {0, 0});
    const ecs::Entity target = scene.add(box2D({0.2f, 0.2f}), {0, 1.5f});
    scene.step();
    hitbox2DAt(scene, disc(0.1f), {1.5f, 0}, arm);
    uint64_t clock = 1'000'000'000;
    sweepHitboxes(*scene.world, 0.1f, clock);
    ecs::get<scenes::Transform2D>(*scene.world, arm)->angle = pi * 170 / 180;
    clock += 100'000'000;
    sweepHitboxes(*scene.world, 0.1f, clock);
    const std::vector<HitboxHit2D> hits = hitsOf(scene);
    REQUIRE(hits.size() == 1);
    CHECK(hits[0].owner.value == arm.value);
    CHECK(hits[0].target.value == target.value);
    CHECK(shareOf(hits[0].timestamp, clock, 0.1) == doctest::Approx((90.0 - 7.66) / 170).epsilon(0.03));
}
