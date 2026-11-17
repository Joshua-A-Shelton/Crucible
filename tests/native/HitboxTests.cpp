// Swept hitboxes (ADR 0096): every hit along a sweep, each target once per
// activation, timed by where along the frame it touched; a swing's arc
// followed between frames, so a hit lands at the same moment at 30 and 240
// fps; never the owner's family; on while an animation window is open.

#include "AnimationTestSupport.h"
#include "PhysicsTestSupport.h"

#include <crucible/Animation.h>

using namespace crucible;
using namespace crucible::physics;
using namespace crucible::physics::testing;

namespace {

constexpr float pi = 3.14159265f;

ecs::Entity hitboxAt(Scene& scene, Hitbox hitbox, Vector3 at, ecs::Entity parent = ecs::nullEntity)
{
    const ecs::ComponentId components[] = {ecs::componentIdOf<Hitbox>, ecs::componentIdOf<scenes::LocalTransform>,
                                           ecs::componentIdOf<scenes::WorldTransform>};
    const ecs::Entity entity = ecs::createEntity(*scene.world, components);
    *ecs::get<Hitbox>(*scene.world, entity) = hitbox;
    *ecs::get<scenes::LocalTransform>(*scene.world, entity) = {at, Quaternion::identity(), {1, 1, 1}};
    if (parent.value != 0)
    {
        REQUIRE(ecs::setParent(*scene.world, entity, parent));
    }
    return entity;
}

Hitbox ball(float radius)
{
    return {ShapeKind::Sphere, {radius, 0, 0}, allLayers, 0, true};
}

ecs::Entity pivot(Scene& scene)
{
    const ecs::ComponentId components[] = {ecs::componentIdOf<scenes::LocalTransform>,
                                           ecs::componentIdOf<scenes::WorldTransform>};
    const ecs::Entity entity = ecs::createEntity(*scene.world, components);
    *ecs::get<scenes::LocalTransform>(*scene.world, entity) = {{0, 0, 0}, Quaternion::identity(), {1, 1, 1}};
    return entity;
}

void place(Scene& scene, ecs::Entity entity, Vector3 at)
{
    ecs::get<scenes::LocalTransform>(*scene.world, entity)->position = at;
}

void turn(Scene& scene, ecs::Entity entity, float yaw)
{
    ecs::get<scenes::LocalTransform>(*scene.world, entity)->rotation = Quaternion::fromAxisAngle({0, 1, 0}, yaw);
}

// The input clock: each frame starts `seconds` after the last.
struct Clock
{
    uint64_t now = 1'000'000'000;

    uint64_t next(double seconds)
    {
        now += static_cast<uint64_t>(seconds * 1e9);
        return now;
    }
};

std::vector<HitboxHit> hitsOf(Scene& scene)
{
    std::vector<HitboxHit> found(hitboxHits(*scene.world, {}));
    (void)hitboxHits(*scene.world, found);
    return found;
}

// Where along a frame (0 its start, 1 the next one's) a timestamp falls.
double shareOf(uint64_t timestamp, uint64_t frameStart, double seconds)
{
    return 1.0 - static_cast<double>(frameStart - timestamp) / (seconds * 1e9);
}

// A ball at 1.5 m swung about Y from 0 to 180 degrees over half a second,
// at a frame rate, past a small box at 90 degrees: the first hit's time
// from the swing's start.
double swingHitTime(double frame)
{
    Scene scene;
    const Vector3 target = rotate(Vector3{1.5f, 0, 0}, Quaternion::fromAxisAngle({0, 1, 0}, pi / 2));
    scene.add(box({0.2f, 0.2f, 0.2f}), target);
    scene.step();
    const ecs::Entity arm = pivot(scene);
    hitboxAt(scene, ball(0.1f), {1.5f, 0, 0}, arm);
    Clock clock;
    const uint64_t start = clock.now;
    sweepHitboxes(*scene.world, static_cast<float>(frame), start);
    const int frames = static_cast<int>(std::lround(0.5 / frame));
    for (int i = 1; i <= frames; ++i)
    {
        turn(scene, arm, pi * static_cast<float>(i) / static_cast<float>(frames));
        sweepHitboxes(*scene.world, static_cast<float>(frame), clock.next(frame));
        const std::vector<HitboxHit> hits = hitsOf(scene);
        if (!hits.empty())
        {
            return static_cast<double>(hits[0].timestamp - start) * 1e-9;
        }
    }
    return -1;
}

} // namespace

TEST_CASE("shapeCastAll finds everything along the way once each and nearest first")
{
    Scene scene;
    const ecs::Entity far = scene.add(box({1, 1, 1}), {6, 0, 0});
    const ecs::Entity near = scene.add(box({1, 1, 1}), {2, 0, 0});
    const ecs::Entity both = scene.add(box({1, 1, 1}), {4, 0, 0});
    scene.add(box({1, 1, 1}), {4, 5, 0}); // off the path
    scene.step();
    RayHit found[8]{};
    const uint32_t count = shapeCastAll(*scene.world, {ShapeKind::Sphere, {0.25f, 0, 0}}, {0, 0, 0},
                                        Quaternion::identity(), {1, 0, 0}, 10, defaultQueryFilter(), found);
    REQUIRE(count == 3);
    CHECK(found[0].entity.value == near.value);
    CHECK(found[1].entity.value == both.value);
    CHECK(found[2].entity.value == far.value);
    CHECK(found[0].distance == doctest::Approx(1.25f).epsilon(0.01));
    CHECK(found[2].distance == doctest::Approx(5.25f).epsilon(0.01));
    // As many as fit, and how many there are.
    CHECK(shapeCastAll(*scene.world, {ShapeKind::Sphere, {0.25f, 0, 0}}, {0, 0, 0}, Quaternion::identity(), {1, 0, 0},
                       10, defaultQueryFilter(), std::span<RayHit>(found, 1)) == 3);
    CHECK(found[0].entity.value == near.value);
}

TEST_CASE("A hitbox swept through a box hits it once and when it touched")
{
    Scene scene;
    const ecs::Entity target = scene.add(box({1, 1, 1}), {0, 0, 0});
    scene.step();
    const ecs::Entity fist = hitboxAt(scene, ball(0.1f), {-2, 0, 0});
    Clock clock;
    sweepHitboxes(*scene.world, 0.1f, clock.now);
    CHECK(hitsOf(scene).empty()); // first seen: nothing touched where it is

    place(scene, fist, {2, 0, 0});
    const uint64_t frame = clock.next(0.1);
    sweepHitboxes(*scene.world, 0.1f, frame);
    std::vector<HitboxHit> hits = hitsOf(scene);
    REQUIRE(hits.size() == 1);
    CHECK(hits[0].hitbox.value == fist.value);
    CHECK(hits[0].owner.value == fist.value); // no Animator, alone: its own owner
    CHECK(hits[0].target.value == target.value);
    // It touched at x = -0.6, 1.4 m of the 4 m it went.
    CHECK(shareOf(hits[0].timestamp, frame, 0.1) == doctest::Approx(0.35).epsilon(0.01));
    CHECK(hits[0].point.x == doctest::Approx(-0.5f).epsilon(0.01));
    CHECK(hits[0].normal.x == doctest::Approx(-1.0f).epsilon(0.01));

    place(scene, fist, {-2, 0, 0});
    sweepHitboxes(*scene.world, 0.1f, clock.next(0.1));
    CHECK(hitsOf(scene).empty()); // once per activation

    // Turned off and on again, it's a new activation.
    ecs::get<Hitbox>(*scene.world, fist)->enabled = false;
    place(scene, fist, {2, 0, 0});
    sweepHitboxes(*scene.world, 0.1f, clock.next(0.1));
    CHECK(hitsOf(scene).empty());
    ecs::get<Hitbox>(*scene.world, fist)->enabled = true;
    sweepHitboxes(*scene.world, 0.1f, clock.next(0.1));
    CHECK(hitsOf(scene).empty()); // switched on away from it: only where it is counts
    place(scene, fist, {-2, 0, 0});
    sweepHitboxes(*scene.world, 0.1f, clock.next(0.1));
    REQUIRE(hitsOf(scene).size() == 1);
}

TEST_CASE("A hitbox switched on where it touches something hits it")
{
    Scene scene;
    const ecs::Entity target = scene.add(box({1, 1, 1}), {0, 0, 0});
    scene.step();
    Hitbox held = ball(0.2f);
    held.enabled = false;
    const ecs::Entity fist = hitboxAt(scene, held, {0.6f, 0, 0});
    Clock clock;
    sweepHitboxes(*scene.world, 0.1f, clock.now);
    CHECK(hitsOf(scene).empty());
    ecs::get<Hitbox>(*scene.world, fist)->enabled = true;
    const uint64_t frame = clock.next(0.1);
    sweepHitboxes(*scene.world, 0.1f, frame);
    const std::vector<HitboxHit> hits = hitsOf(scene);
    REQUIRE(hits.size() == 1);
    CHECK(hits[0].target.value == target.value);
    CHECK(hits[0].timestamp == frame);
}

TEST_CASE("A swing's arc is followed between frames")
{
    Scene scene;
    // At 90 degrees round the pivot: a straight line from 0 to 170 degrees
    // passes near the pivot, far from it.
    const Vector3 at = rotate(Vector3{1.5f, 0, 0}, Quaternion::fromAxisAngle({0, 1, 0}, pi / 2));
    const ecs::Entity target = scene.add(box({0.2f, 0.2f, 0.2f}), at);
    scene.step();
    const ecs::Entity arm = pivot(scene);
    hitboxAt(scene, ball(0.1f), {1.5f, 0, 0}, arm);
    Clock clock;
    sweepHitboxes(*scene.world, 0.1f, clock.now);
    turn(scene, arm, pi * 170 / 180);
    const uint64_t frame = clock.next(0.1);
    sweepHitboxes(*scene.world, 0.1f, frame);
    const std::vector<HitboxHit> hits = hitsOf(scene);
    REQUIRE(hits.size() == 1);
    CHECK(hits[0].target.value == target.value);
    // Touched about 7.7 degrees before the target's 90 (the radii over 1.5 m).
    CHECK(shareOf(hits[0].timestamp, frame, 0.1) == doctest::Approx((90.0 - 7.66) / 170).epsilon(0.03));
}

TEST_CASE("A hit lands at the same moment at 30 and 240 fps")
{
    const double slow = swingHitTime(1.0 / 30);
    const double fast = swingHitTime(1.0 / 240);
    REQUIRE(slow > 0);
    REQUIRE(fast > 0);
    // Touched at about 82.3 degrees of 180, a quarter second in.
    CHECK(fast == doctest::Approx(0.5 * 82.34 / 180).epsilon(0.01));
    CHECK(std::fabs(slow - fast) < 5e-5); // within 50 µs, as footsteps are
}

TEST_CASE("A hitbox never hits its owner or what's under it")
{
    Scene scene;
    const ecs::Entity body = scene.add(box({1, 1, 1}), {0, 0, 0});
    const ecs::Entity shield = scene.add(box({0.2f, 1, 1}), {1, 0, 0});
    REQUIRE(ecs::setParent(*scene.world, shield, body));
    const ecs::Entity stranger = scene.add(box({0.2f, 1, 1}), {2, 0, 0});
    scene.step();
    const ecs::Entity fist = hitboxAt(scene, ball(0.1f), {-2, 0, 0}, body);
    Clock clock;
    sweepHitboxes(*scene.world, 0.1f, clock.now);
    place(scene, fist, {3, 0, 0});
    sweepHitboxes(*scene.world, 0.1f, clock.next(0.1));
    const std::vector<HitboxHit> hits = hitsOf(scene);
    REQUIRE(hits.size() == 1);
    CHECK(hits[0].owner.value == body.value);
    CHECK(hits[0].target.value == stranger.value);
}

TEST_CASE("A hitbox's mask picks the layers it hits and triggers count")
{
    Scene scene;
    Collider sensor = box({1, 1, 1});
    sensor.isTrigger = true;
    const ecs::Entity zone = scene.add(sensor, {0, 0, 0});
    const RigidBody onTwo{BodyKind::Static, 0, {}, {}, 0, 2, allLayers};
    scene.add(box({1, 1, 1}), {2, 0, 0}, &onTwo);
    scene.step();
    Hitbox fist = ball(0.1f);
    fist.mask = 1;
    const ecs::Entity entity = hitboxAt(scene, fist, {-2, 0, 0});
    Clock clock;
    sweepHitboxes(*scene.world, 0.1f, clock.now);
    place(scene, entity, {4, 0, 0});
    sweepHitboxes(*scene.world, 0.1f, clock.next(0.1));
    const std::vector<HitboxHit> hits = hitsOf(scene);
    REQUIRE(hits.size() == 1);
    CHECK(hits[0].target.value == zone.value);
}

TEST_CASE("A window turns a hitbox on and off and each opening is an activation")
{
    using namespace crucible::animation;
    using animation::testing::idOf;
    const uint32_t strike = eventName("Strike");
    animation::testing::markClip(2, {}, {{0.2f, 0.35f, strike}});
    {
        Scene scene;
        animation::testing::WalkerAssets::install();
        registerAnimation();
        const ecs::ComponentId components[] = {ecs::componentIdOf<Animator>, ecs::componentIdOf<scenes::LocalTransform>,
                                               ecs::componentIdOf<scenes::WorldTransform>};
        const ecs::Entity walker = ecs::createEntity(*scene.world, components);
        *ecs::get<Animator>(*scene.world, walker) = {idOf(1), idOf(2), 1.0f, true};
        *ecs::get<scenes::LocalTransform>(*scene.world, walker) = {{0, 0, 0}, Quaternion::identity(), {1, 1, 1}};
        const ecs::Entity target = scene.add(box({1, 1, 1}), {0, 0, 5});
        scene.step();
        Hitbox fist = ball(0.2f);
        fist.window = strike;
        hitboxAt(scene, fist, {0, 0, 4.6f}, walker); // touching the target throughout

        Clock clock;
        const uint64_t start = clock.now;
        auto frame = [&](float seconds) {
            const uint64_t at = clock.next(seconds);
            advancePoses(*scene.world, seconds, at);
            evaluatePoses(*scene.world);
            sweepHitboxes(*scene.world, seconds, at);
            return hitsOf(scene);
        };
        CHECK(frame(0.1f).empty());                 // 0 to 0.1: shut
        std::vector<HitboxHit> hits = frame(0.15f); // 0.1 to 0.25: opens at 0.2
        REQUIRE(hits.size() == 1);
        CHECK(hits[0].owner.value == walker.value);
        CHECK(hits[0].target.value == target.value);
        CHECK(static_cast<double>(hits[0].timestamp - start) == doctest::Approx(0.2e9).epsilon(1e-4));
        CHECK(frame(0.1f).empty()); // still open: hit already
        CHECK(frame(0.3f).empty()); // shut at 0.35
        // The Walk loops each second: its next opening is another activation.
        float played = 0.65f;
        while (played < 1.1f)
        {
            CHECK(frame(0.1f).empty());
            played += 0.1f;
        }
        hits = frame(0.15f); // past 1.2
        REQUIRE(hits.size() == 1);
        CHECK(static_cast<double>(hits[0].timestamp - start) == doctest::Approx(1.2e9).epsilon(1e-4));
        releasePoses(*scene.world);
    }
    animation::testing::markClip(2, {}, {});
}
