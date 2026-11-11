// 1D blend spaces (ADR 0081): a state blending the two clips either side of
// a parameter, kept in step by one phase however long each clip is.

#include "AnimationTestSupport.h"

using namespace crucible;
using namespace crucible::animation;
using namespace crucible::animation::testing;

namespace {

constexpr uint8_t machineId = 11;

// One state blending by Speed, the clips at their places.
void install(std::vector<std::pair<uint8_t, float>> clips)
{
    MachineBuilder machine;
    const uint32_t speed = machine.parameter("Speed", assets::ParameterType::Float);
    machine.blend("Move", static_cast<int32_t>(speed), std::move(clips));
    machine.install(machineId);
}

} // namespace

TEST_CASE("A 1D blend space blends the two clips either side of the parameter")
{
    install({{2, 0.0f}, {3, 1.0f}, {4, 2.0f}}); // Walk, Idle, WalkForward
    const Reference reference;
    const Clip walk = reference.clip(0), forward = reference.clip(2);
    Character walker;
    ecs::get<Animator>(*walker.world, walker.entity)->stateMachine = idOf(machineId);

    // Between Idle (at 1) and WalkForward (at 2).
    REQUIRE(setFloat(*walker.world, walker.entity, "Speed", 1.5f));
    walker.step(0.4f);
    // Idle is 2 s and WalkForward 1 s, half each: 1.5 s long, so 0.4 s is a
    // share of 0.4 / 1.5 through both.
    const float phase = 0.4f / 1.5f;
    CHECK(worstJoint(walker, reference,
                     blendPoses(reference.sample(reference.clip(1), phase * 2), reference.sample(forward, phase),
                                0.5f)) < 1e-4f);

    // Past either end, the end clip alone.
    REQUIRE(setFloat(*walker.world, walker.entity, "Speed", -1.0f));
    walker.pose();
    CHECK(worstJoint(walker, reference, reference.sample(walk, phase)) < 1e-4f);
    REQUIRE(setFloat(*walker.world, walker.entity, "Speed", 5.0f));
    walker.pose();
    CHECK(worstJoint(walker, reference, reference.sample(forward, phase)) < 1e-4f);
}

TEST_CASE("A blend space's weights follow the parameter between two clips")
{
    install({{2, 0.0f}, {4, 1.7f}}); // Walk in place, then travelling
    const Reference reference;
    const Clip walk = reference.clip(0), forward = reference.clip(2);
    Character walker;
    ecs::get<Animator>(*walker.world, walker.entity)->stateMachine = idOf(machineId);
    for (const float speed : {0.0f, 0.425f, 0.85f, 1.7f})
    {
        CAPTURE(speed);
        REQUIRE(setFloat(*walker.world, walker.entity, "Speed", speed));
        walker.pose();
        CHECK(worstJoint(walker, reference,
                         blendPoses(reference.sample(walk, 0), reference.sample(forward, 0), speed / 1.7f)) < 1e-4f);
    }
    walker.step(0.3f);
    CHECK(worstJoint(walker, reference,
                     blendPoses(reference.sample(walk, 0.3f), reference.sample(forward, 0.3f), 1.0f)) < 1e-4f);
}
