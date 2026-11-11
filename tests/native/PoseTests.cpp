// The pose runtime (ADR 0079): the Walker posed by its clips, against the
// scalar reference, and the pool's bookkeeping.

#include "AnimationTestSupport.h"

#include <crucible/DebugLines.h>

using namespace crucible;
using namespace crucible::animation;
using namespace crucible::animation::testing;

TEST_CASE("While editing a character holds its clip's first sample")
{
    const Reference reference;
    Character walker;
    evaluatePoses(*walker.world); // "Animation.Pose" alone, as the editor runs it
    CHECK(worstJoint(walker, reference, reference.sample(reference.clip(0), 0)) < 1e-5f);
    // Its handle, for skinning to find the pose by.
    const PoseHandle* handle = ecs::read<PoseHandle>(*walker.world, walker.entity);
    REQUIRE(handle != nullptr);
    CHECK(handle->pose != 0);
}

TEST_CASE("Playing samples the clip between samples like the scalar reference")
{
    const Reference reference;
    Character walker;
    const Clip walk = reference.clip(0);
    float time = 0;
    // On samples and between them, across the loop's end.
    for (const float delta : {0.5f, 0.0123f, 0.25f, 0.3f, 0.07f})
    {
        walker.step(delta);
        time = std::fmod(time + delta, walk.info().duration);
        CAPTURE(time);
        CHECK(worstJoint(walker, reference, reference.sample(walk, time)) < 1e-4f);
    }
}

TEST_CASE("Speed scales time and a clip that doesn't loop holds its end")
{
    const Reference reference;
    const Clip walk = reference.clip(0);
    {
        Character walker;
        ecs::get<Animator>(*walker.world, walker.entity)->speed = 2.0f;
        walker.step(0.1f);
        CHECK(worstJoint(walker, reference, reference.sample(walk, 0.2f)) < 1e-4f);
    }
    {
        Character walker(2, false);
        walker.step(0.75f);
        walker.step(0.75f);
        CHECK(worstJoint(walker, reference, reference.sample(walk, walk.info().duration)) < 1e-4f);
    }
}

TEST_CASE("Crossfades blend the clip faded from into the new one")
{
    const Reference reference;
    const Clip walk = reference.clip(0), idle = reference.clip(1);
    Character walker;
    walker.step(0.25f);
    REQUIRE(play(*walker.world, walker.entity, idOf(3), 0.5f));
    CHECK(ecs::read<Animator>(*walker.world, walker.entity)->clip == idOf(3));
    walker.step(0.25f);
    // Halfway: Walk at 0.5 s, Idle at 0.25 s, half each.
    const LocalPose from = reference.sample(walk, 0.5f), to = reference.sample(idle, 0.25f);
    LocalPose expected = from;
    for (size_t j = 0; j < expected.rotations.size(); ++j)
    {
        expected.rotations[j] = Reference::nlerp(from.rotations[j], to.rotations[j], 0.5f);
        expected.positions[j] = Reference::lerp(from.positions[j], to.positions[j], 0.5f);
        expected.scales[j] = Reference::lerp(from.scales[j], to.scales[j], 0.5f);
    }
    CHECK(worstJoint(walker, reference, expected) < 1e-4f);
    // Past the fade, only Idle.
    walker.step(0.5f);
    CHECK(worstJoint(walker, reference, reference.sample(idle, 0.75f)) < 1e-4f);
}

TEST_CASE("Changing the Animator's clip cuts to it")
{
    const Reference reference;
    Character walker;
    walker.step(0.3f);
    ecs::get<Animator>(*walker.world, walker.entity)->clip = idOf(3);
    walker.step(0.0f);
    CHECK(worstJoint(walker, reference, reference.sample(reference.clip(1), 0)) < 1e-5f);
}

TEST_CASE("Without a clip a character holds its bind pose")
{
    const Reference reference;
    Character walker(0);
    walker.step(0.5f);
    CHECK(worstJoint(walker, reference, reference.rest()) < 1e-5f);
}

TEST_CASE("Joints are found by name")
{
    const Reference reference;
    Character walker;
    CHECK(findJoint(*walker.world, walker.entity, "Hips") == reference.joint("Hips"));
    CHECK(findJoint(*walker.world, walker.entity, "RightFoot") == reference.joint("RightFoot"));
    CHECK(findJoint(*walker.world, walker.entity, "Tail") == -1);
    // Before any pose, the identity.
    CHECK(walker.model(0).rows[3].x == 0.0f);
}

TEST_CASE("Poses are freed with their entity or their world")
{
    const uint32_t before = poseCount();
    {
        Character walker;
        evaluatePoses(*walker.world);
        CHECK(poseCount() == before + 1);
        ecs::destroyEntity(*walker.world, walker.entity);
        evaluatePoses(*walker.world);
        CHECK(poseCount() == before);
    }
    {
        Character walker;
        evaluatePoses(*walker.world);
        CHECK(poseCount() == before + 1);
    }
    CHECK(poseCount() == before); // releasePoses, before the world went
}

TEST_CASE("Missing skeletons and clips are reported")
{
    Character walker;
    CHECK(play(*walker.world, walker.entity, idOf(9), 0).status() == Status::NotFound);
    const ecs::Entity bare = ecs::createEntity(*walker.world);
    CHECK(play(*walker.world, bare, idOf(2), 0).status() == Status::NotFound);
    ecs::get<Animator>(*walker.world, walker.entity)->skeleton = idOf(9);
    walker.step(0.1f);
    CHECK(findJoint(*walker.world, walker.entity, "Hips") == -1);
    CHECK(walker.model(0).rows[0].x == 1.0f);
}

TEST_CASE("Skeletons draw as debug lines from each joint to its parent")
{
    Character walker;
    walker.step(0.0f);
    debug::clearDebugLines();
    drawSkeletons(*walker.world, {1, 1, 1, 1});
    CHECK(debug::debugLineCount() == 12); // 13 joints, one root
    debug::clearDebugLines();
}

TEST_CASE("Reloading a clip reads it again")
{
    const Reference reference;
    Character walker;
    walker.step(0.2f);
    CHECK(reloadAnimationAsset(idOf(2)));
    CHECK_FALSE(reloadAnimationAsset(idOf(9)));
    walker.step(0.1f);
    CHECK(worstJoint(walker, reference, reference.sample(reference.clip(0), 0.3f)) < 1e-4f);
    CHECK(reloadAnimationAsset(idOf(1)));
    walker.step(0.1f);
    CHECK(worstJoint(walker, reference, reference.sample(reference.clip(0), 0.4f)) < 1e-4f);
}
