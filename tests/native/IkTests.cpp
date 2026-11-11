// IK (ADR 0081): two-bone IK reaching targets (bending toward a pole,
// straightening toward one out of reach), look-at within its limit, targets
// in world space, and weights fading in and out.

#include "AnimationTestSupport.h"

using namespace crucible;
using namespace crucible::animation;
using namespace crucible::animation::testing;

namespace {

Vector3 at(const Character& walker, int32_t joint)
{
    const Vector4& row = walker.model(joint).rows[3];
    return {row.x, row.y, row.z};
}

// The Walker in its bind pose, and its left leg.
struct Leg
{
    Character walker{0};
    int32_t hip = findJoint(*walker.world, walker.entity, "LeftUpperLeg");
    int32_t knee = findJoint(*walker.world, walker.entity, "LeftLowerLeg");
    int32_t foot = findJoint(*walker.world, walker.entity, "LeftFoot");

    Leg()
    {
        walker.step(0);
    }

    TwoBoneIkDesc reach(Vector3 target, float weight = 1, float fade = 0) const
    {
        // The knee bends forward, toward +z.
        return {foot, target, at(walker, knee) + Vector3{0, 0, 1}, true, weight, fade};
    }
};

} // namespace

TEST_CASE("Two-bone IK puts the end joint on a target in reach")
{
    Leg leg;
    const Vector3 hip = at(leg.walker, leg.hip), rest = at(leg.walker, leg.foot);
    const Vector3 target = rest + Vector3{0, 0.3f, 0.2f};
    REQUIRE(setTwoBoneIk(*leg.walker.world, leg.walker.entity, 0, leg.reach(target)));
    leg.walker.step(1.0f / 60);
    CHECK(length(at(leg.walker, leg.foot) - target) < 1e-4f);
    // The bones keep their lengths, and the knee bends toward the pole.
    const Vector3 knee = at(leg.walker, leg.knee);
    CHECK(length(knee - hip) == doctest::Approx(0.45f).epsilon(1e-4));
    CHECK(length(at(leg.walker, leg.foot) - knee) == doctest::Approx(0.45f).epsilon(1e-4));
    CHECK(knee.z > 0.05f);
    // Without the pole it bends the way it was bent: here, not at all, so
    // any way; with the knee forward it stays forward.
}

TEST_CASE("A target out of reach straightens the chain toward it")
{
    Leg leg;
    const Vector3 hip = at(leg.walker, leg.hip);
    const Vector3 target = hip + Vector3{0, -3, 2};
    REQUIRE(setTwoBoneIk(*leg.walker.world, leg.walker.entity, 0, leg.reach(target)));
    leg.walker.step(1.0f / 60);
    const Vector3 foot = at(leg.walker, leg.foot);
    CHECK(length(foot - hip) == doctest::Approx(0.9f).epsilon(1e-3));
    CHECK(dot(normalize(foot - hip), normalize(target - hip)) > 0.9999f);
}

TEST_CASE("Weight blends from where the pose has the end to the target")
{
    Leg leg;
    const Vector3 rest = at(leg.walker, leg.foot);
    const Vector3 target = rest + Vector3{0, 0.4f, 0.2f};
    REQUIRE(setTwoBoneIk(*leg.walker.world, leg.walker.entity, 0, leg.reach(target, 0.5f)));
    leg.walker.step(1.0f / 60);
    CHECK(length(at(leg.walker, leg.foot) - (rest + (target - rest) * 0.5f)) < 1e-4f);
}

TEST_CASE("IK fades in and clears")
{
    Leg leg;
    const Vector3 rest = at(leg.walker, leg.foot);
    const Vector3 target = rest + Vector3{0, 0.4f, 0.2f};
    REQUIRE(setTwoBoneIk(*leg.walker.world, leg.walker.entity, 1, leg.reach(target, 1, 0.5f)));
    leg.walker.step(0.25f);
    CHECK(length(at(leg.walker, leg.foot) - (rest + (target - rest) * 0.5f)) < 1e-4f);
    // Set again each frame with the same weight, the fade goes on.
    REQUIRE(setTwoBoneIk(*leg.walker.world, leg.walker.entity, 1, leg.reach(target, 1, 0.5f)));
    leg.walker.step(0.25f);
    CHECK(length(at(leg.walker, leg.foot) - target) < 1e-4f);
    REQUIRE(clearIk(*leg.walker.world, leg.walker.entity, 1, 1.0f));
    leg.walker.step(0.75f);
    CHECK(length(at(leg.walker, leg.foot) - (rest + (target - rest) * 0.25f)) < 1e-4f);
    leg.walker.step(0.5f);
    CHECK(length(at(leg.walker, leg.foot) - rest) < 1e-4f);
}

TEST_CASE("IK targets are in world space")
{
    Leg leg;
    const Vector3 rest = at(leg.walker, leg.foot);
    ecs::get<scenes::LocalTransform>(*leg.walker.world, leg.walker.entity)->position = {5, 0, 0};
    const Vector3 model = rest + Vector3{0, 0.3f, 0.2f};
    TwoBoneIkDesc desc = leg.reach(model + Vector3{5, 0, 0});
    desc.pole = desc.pole + Vector3{5, 0, 0};
    REQUIRE(setTwoBoneIk(*leg.walker.world, leg.walker.entity, 0, desc));
    leg.walker.step(1.0f / 60);
    CHECK(length(at(leg.walker, leg.foot) - model) < 1e-4f);
}

TEST_CASE("Look-at turns a joint toward the target within its limit")
{
    Character walker(0);
    const int32_t head = findJoint(*walker.world, walker.entity, "Head");
    walker.step(0);
    const Vector3 eyes = at(walker, head);
    const Vector3 target = eyes + Vector3{2, 0, 2}; // 45 degrees to the side
    auto aim = [&] {
        return normalize(transformDirection(Vector3{0, 0, 1}, walker.model(head)));
    };

    REQUIRE(setLookAt(*walker.world, walker.entity, 0, {head, target, {0, 0, 1}, 1.0f, 1.0f, 0}));
    walker.step(1.0f / 60);
    CHECK(dot(aim(), normalize(target - eyes)) > 0.9999f);
    CHECK(length(at(walker, head) - eyes) < 1e-5f); // it turns in place

    REQUIRE(setLookAt(*walker.world, walker.entity, 0, {head, target, {0, 0, 1}, 0.3f, 1.0f, 0}));
    walker.step(1.0f / 60);
    CHECK(std::acos(std::clamp(dot(aim(), Vector3{0, 0, 1}), -1.0f, 1.0f)) == doctest::Approx(0.3f).epsilon(1e-3));
}

TEST_CASE("IK needs a joint with enough of a chain above it")
{
    Leg leg;
    ecs::World& world = *leg.walker.world;
    const ecs::Entity entity = leg.walker.entity;
    const int32_t hips = findJoint(world, entity, "Hips");
    CHECK(setTwoBoneIk(world, entity, 0, leg.reach({0, 0, 0})));
    CHECK(setTwoBoneIk(world, entity, maxIkSlots, leg.reach({0, 0, 0})).status() == Status::NotFound);
    TwoBoneIkDesc desc = leg.reach({0, 0, 0});
    desc.joint = leg.hip; // its parent is the root: one ancestor short
    CHECK(setTwoBoneIk(world, entity, 0, desc).status() == Status::NotFound);
    CHECK(setLookAt(world, entity, 0, {hips, {0, 0, 1}, {0, 0, 1}, 1, 1, 0}));
    CHECK(setLookAt(world, entity, 0, {99, {0, 0, 1}, {0, 0, 1}, 1, 1, 0}).status() == Status::NotFound);
}
