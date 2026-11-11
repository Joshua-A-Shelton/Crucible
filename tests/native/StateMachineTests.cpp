// State machines (ADR 0081): states playing clips, transitions on
// parameters and exit times with crossfades, Triggers used up, and what
// happens when a machine is missing or read again.

#include "AnimationTestSupport.h"

using namespace crucible;
using namespace crucible::animation;
using namespace crucible::animation::testing;

namespace {

constexpr uint8_t machineId = 10;

void useMachine(Character& walker, uint8_t id)
{
    ecs::get<Animator>(*walker.world, walker.entity)->stateMachine = idOf(id);
}

assets::TransitionCondition test(uint32_t parameter, assets::ConditionOp op, float value = 0)
{
    return {parameter, op, value};
}

} // namespace

TEST_CASE("A state machine starts in its start state and plays its clip")
{
    MachineBuilder machine;
    machine.state("Walk", 2);
    machine.start = machine.state("Idle", 3);
    machine.install(machineId);
    const Reference reference;
    Character walker;
    useMachine(walker, machineId);
    walker.step(0.3f);
    CHECK(currentState(*walker.world, walker.entity) == 1);
    CHECK(findState(*walker.world, walker.entity, "Walk") == 0);
    CHECK(findState(*walker.world, walker.entity, "Run") == -1);
    CHECK(worstJoint(walker, reference, reference.sample(reference.clip(1), 0.3f)) < 1e-4f);
    // The machine plays the base, so play() can't.
    CHECK(play(*walker.world, walker.entity, idOf(2), 0).status() == Status::Unsupported);
}

TEST_CASE("A condition on a parameter takes a transition and crossfades")
{
    MachineBuilder machine;
    const uint32_t speed = machine.parameter("Speed", assets::ParameterType::Float);
    const uint32_t idle = machine.state("Idle", 3);
    const uint32_t walk = machine.state("Walk", 2);
    machine.transition(static_cast<int32_t>(idle), walk, 0.5f, -1, {test(speed, assets::ConditionOp::Greater, 0.5f)});
    machine.transition(static_cast<int32_t>(walk), idle, 0, -1, {test(speed, assets::ConditionOp::Less, 0.5f)});
    machine.install(machineId);
    const Reference reference;
    const Clip walkClip = reference.clip(0), idleClip = reference.clip(1);
    Character walker;
    useMachine(walker, machineId);

    walker.step(0.2f);
    CHECK(currentState(*walker.world, walker.entity) == static_cast<int32_t>(idle));
    REQUIRE(setFloat(*walker.world, walker.entity, "Speed", 1.0f));
    walker.step(0.1f);
    // Taken after Idle played on; Walk starts from its start, faded in from 0.
    CHECK(currentState(*walker.world, walker.entity) == static_cast<int32_t>(walk));
    CHECK(worstJoint(walker, reference, reference.sample(idleClip, 0.3f)) < 1e-4f);
    walker.step(0.25f);
    CHECK(worstJoint(walker, reference,
                     blendPoses(reference.sample(idleClip, 0.55f), reference.sample(walkClip, 0.25f), 0.5f)) < 1e-4f);
    walker.step(0.3f);
    CHECK(worstJoint(walker, reference, reference.sample(walkClip, 0.55f)) < 1e-4f);

    // Without a blend time it cuts.
    REQUIRE(setFloat(*walker.world, walker.entity, "Speed", 0.0f));
    walker.step(0.1f);
    CHECK(currentState(*walker.world, walker.entity) == static_cast<int32_t>(idle));
    CHECK(worstJoint(walker, reference, reference.sample(idleClip, 0)) < 1e-4f);
}

TEST_CASE("A Trigger is used up by the transition that tests it")
{
    MachineBuilder machine;
    const uint32_t wave = machine.parameter("Wave", assets::ParameterType::Trigger);
    const uint32_t idle = machine.state("Idle", 3);
    const uint32_t waving = machine.state("Waving", 2, 1, false);
    // From any state but Waving itself; back once Waving has played.
    machine.transition(-1, waving, 0, -1, {test(wave, assets::ConditionOp::Set)});
    machine.transition(static_cast<int32_t>(waving), idle, 0, 1.0f);
    machine.install(machineId);
    Character walker;
    useMachine(walker, machineId);

    walker.step(0.1f);
    CHECK(currentState(*walker.world, walker.entity) == static_cast<int32_t>(idle));
    REQUIRE(trigger(*walker.world, walker.entity, "Wave"));
    walker.step(0.1f);
    CHECK(currentState(*walker.world, walker.entity) == static_cast<int32_t>(waving));
    walker.step(0.6f);
    CHECK(currentState(*walker.world, walker.entity) == static_cast<int32_t>(waving));
    walker.step(0.6f); // past Waving's end: it doesn't loop, so it's past its exit time
    CHECK(currentState(*walker.world, walker.entity) == static_cast<int32_t>(idle));
    walker.step(0.5f);
    CHECK(currentState(*walker.world, walker.entity) == static_cast<int32_t>(idle));

    // Set while Waving, "any state" doesn't take it to Waving again; it
    // stays set, and is taken once back in Idle.
    REQUIRE(trigger(*walker.world, walker.entity, "Wave"));
    walker.step(0.1f);
    REQUIRE(trigger(*walker.world, walker.entity, "Wave"));
    walker.step(0.5f);
    CHECK(currentState(*walker.world, walker.entity) == static_cast<int32_t>(waving));
    walker.step(0.6f);
    CHECK(currentState(*walker.world, walker.entity) == static_cast<int32_t>(idle));
    walker.step(0.1f);
    CHECK(currentState(*walker.world, walker.entity) == static_cast<int32_t>(waving));
}

TEST_CASE("An exit time is passed once a loop")
{
    MachineBuilder machine;
    const uint32_t ready = machine.parameter("Ready", assets::ParameterType::Bool);
    const uint32_t idle = machine.state("Idle", 3); // two seconds
    const uint32_t walk = machine.state("Walk", 2);
    machine.transition(static_cast<int32_t>(idle), walk, 0, 0.5f, {test(ready, assets::ConditionOp::Equal, 1)});
    machine.install(machineId);
    Character walker;
    useMachine(walker, machineId);

    walker.step(1.1f); // to 0.55: past the exit time, but not ready
    REQUIRE(setBool(*walker.world, walker.entity, "Ready", true));
    walker.step(0.2f); // to 0.65
    CHECK(currentState(*walker.world, walker.entity) == static_cast<int32_t>(idle));
    walker.step(1.0f); // round to 0.15: not through 0.5
    CHECK(currentState(*walker.world, walker.entity) == static_cast<int32_t>(idle));
    walker.step(0.8f); // to 0.55
    CHECK(currentState(*walker.world, walker.entity) == static_cast<int32_t>(walk));
}

TEST_CASE("Parameters of the wrong name or type are NotFound")
{
    MachineBuilder machine;
    machine.parameter("Ready", assets::ParameterType::Bool);
    machine.state("Idle", 3);
    machine.install(machineId);
    Character walker;
    CHECK(setBool(*walker.world, walker.entity, "Ready", true).status() == Status::NotFound); // no machine yet
    CHECK(currentState(*walker.world, walker.entity) == -1);
    useMachine(walker, machineId);
    CHECK(setBool(*walker.world, walker.entity, "Ready", true));
    CHECK(setFloat(*walker.world, walker.entity, "Ready", 1).status() == Status::NotFound);
    CHECK(trigger(*walker.world, walker.entity, "Ready").status() == Status::NotFound);
    CHECK(setBool(*walker.world, walker.entity, "Steady", true).status() == Status::NotFound);
}

TEST_CASE("A state machine read again starts over and one that can't be read leaves the clip")
{
    MachineBuilder machine;
    const uint32_t go = machine.parameter("Go", assets::ParameterType::Trigger);
    const uint32_t idle = machine.state("Idle", 3);
    const uint32_t walk = machine.state("Walk", 2);
    machine.transition(static_cast<int32_t>(idle), walk, 0, -1, {test(go, assets::ConditionOp::Set)});
    machine.install(machineId);
    const Reference reference;
    Character walker;
    useMachine(walker, machineId);
    REQUIRE(trigger(*walker.world, walker.entity, "Go"));
    walker.step(0.1f);
    CHECK(currentState(*walker.world, walker.entity) == static_cast<int32_t>(walk));
    machine.install(machineId);
    walker.step(0.1f);
    CHECK(currentState(*walker.world, walker.entity) == static_cast<int32_t>(idle));

    // An Animator naming a missing machine plays its clip (Walk).
    useMachine(walker, 99);
    walker.step(0.2f);
    CHECK(currentState(*walker.world, walker.entity) == -1);
    CHECK(worstJoint(walker, reference, reference.sample(reference.clip(0), 0.2f)) < 1e-4f);
}

TEST_CASE("A transition at an exit time carries the frame's time past it")
{
    // Idle is 2 s; Walk follows at its end. Whatever the steps, 2.4 s in,
    // Walk is 0.4 s in: the 0.1 s of the frame after Idle's end counts.
    MachineBuilder machine;
    const uint32_t idle = machine.state("Idle", 3);
    const uint32_t walk = machine.state("Walk", 2);
    machine.transition(static_cast<int32_t>(idle), walk, 0, 1.0f);
    machine.install(machineId);
    const Reference reference;
    for (const float step : {0.3f, 0.07f, 0.4f})
    {
        CAPTURE(step);
        Character walker;
        useMachine(walker, machineId);
        const int frames = static_cast<int>(std::lround(2.4f / step));
        for (int i = 0; i < frames; ++i)
        {
            walker.step(step);
        }
        CHECK(currentState(*walker.world, walker.entity) == static_cast<int32_t>(walk));
        CHECK(worstJoint(walker, reference, reference.sample(reference.clip(0), frames * step - 2.0f)) < 1e-3f);
    }
}
