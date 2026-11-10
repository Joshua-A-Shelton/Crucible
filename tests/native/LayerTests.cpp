// Layers (ADR 0079): override layers by weight and mask, additive layers by
// their clip's difference from its first sample, fading in and out.

#include "AnimationTestSupport.h"

using namespace crucible;
using namespace crucible::animation;
using namespace crucible::animation::testing;

namespace {

float worstJoint(const Character& walker, const Reference& reference, const LocalPose& expected)
{
    const std::vector<Matrix> model = reference.modelOf(expected);
    float worst = 0;
    for (size_t j = 0; j < model.size(); ++j)
    {
        worst = std::max(worst, difference(walker.model(static_cast<int32_t>(j)), model[j]));
    }
    return worst;
}

LayerDesc layer(uint8_t clip, float weight, LayerBlend blend = LayerBlend::Override, float fade = 0)
{
    return {idOf(clip), weight, blend, 1.0f, true, fade};
}

// Whether joint j is `root` or below it.
bool under(const Reference& reference, int32_t j, int32_t root)
{
    for (; j >= 0; j = reference.joints[j].parent)
    {
        if (j == root)
        {
            return true;
        }
    }
    return false;
}

} // namespace

TEST_CASE("A full override layer replaces the pose below it")
{
    const Reference reference;
    Character walker;
    REQUIRE(setLayer(*walker.world, walker.entity, 0, layer(3, 1.0f), {}));
    walker.step(0.4f);
    CHECK(worstJoint(walker, reference, reference.sample(reference.clip(1), 0.4f)) < 1e-4f);
}

TEST_CASE("A mask limits a layer to joint subtrees")
{
    const Reference reference;
    const Clip walk = reference.clip(0), idle = reference.clip(1);
    Character walker;
    const int32_t spine = findJoint(*walker.world, walker.entity, "Spine");
    REQUIRE(spine >= 0);
    const MaskEntry mask[] = {{spine, 1.0f}};
    // Walk on the layer, over Idle at the base: the spine and above walk,
    // the legs idle (Idle moves only the spine, so they rest).
    ecs::get<Animator>(*walker.world, walker.entity)->clip = idOf(3);
    REQUIRE(setLayer(*walker.world, walker.entity, 0, layer(2, 1.0f), mask));
    walker.step(0.3f);
    const LocalPose base = reference.sample(idle, 0.3f), over = reference.sample(walk, 0.3f);
    LocalPose expected = base;
    for (size_t j = 0; j < expected.rotations.size(); ++j)
    {
        if (under(reference, static_cast<int32_t>(j), spine))
        {
            expected.rotations[j] = over.rotations[j];
            expected.positions[j] = over.positions[j];
            expected.scales[j] = over.scales[j];
        }
    }
    CHECK(worstJoint(walker, reference, expected) < 1e-4f);
}

TEST_CASE("Later mask entries win where subtrees overlap")
{
    const Reference reference;
    const Clip walk = reference.clip(0);
    Character walker(0);
    const int32_t spine = findJoint(*walker.world, walker.entity, "Spine");
    const int32_t arm = findJoint(*walker.world, walker.entity, "LeftUpperArm");
    const MaskEntry mask[] = {{spine, 1.0f}, {arm, 0.0f}};
    REQUIRE(setLayer(*walker.world, walker.entity, 0, layer(2, 1.0f), mask));
    walker.step(0.3f);
    const LocalPose rest = reference.rest(), over = reference.sample(walk, 0.3f);
    LocalPose expected = rest;
    for (size_t j = 0; j < expected.rotations.size(); ++j)
    {
        const int32_t joint = static_cast<int32_t>(j);
        if (under(reference, joint, spine) && !under(reference, joint, arm))
        {
            expected.rotations[j] = over.rotations[j];
            expected.positions[j] = over.positions[j];
            expected.scales[j] = over.scales[j];
        }
    }
    CHECK(worstJoint(walker, reference, expected) < 1e-4f);
}

TEST_CASE("A layer's weight fades in and clearing fades it out")
{
    const Reference reference;
    const Clip walk = reference.clip(0), idle = reference.clip(1);
    Character walker;
    REQUIRE(setLayer(*walker.world, walker.entity, 0, layer(3, 1.0f, LayerBlend::Override, 1.0f), {}));
    walker.step(0.5f);
    const LocalPose base = reference.sample(walk, 0.5f), over = reference.sample(idle, 0.5f);
    LocalPose half = base;
    for (size_t j = 0; j < half.rotations.size(); ++j)
    {
        half.rotations[j] = Reference::nlerp(base.rotations[j], over.rotations[j], 0.5f);
        half.positions[j] = Reference::lerp(base.positions[j], over.positions[j], 0.5f);
        half.scales[j] = Reference::lerp(base.scales[j], over.scales[j], 0.5f);
    }
    CHECK(worstJoint(walker, reference, half) < 1e-4f);
    REQUIRE(clearLayer(*walker.world, walker.entity, 0, 0.5f));
    walker.step(0.25f); // back to a quarter
    walker.step(0.5f);  // gone
    CHECK(worstJoint(walker, reference, reference.sample(walk, 0.25f)) < 1e-4f);
}

TEST_CASE("An additive layer adds its clip's difference from its first sample")
{
    const Reference reference;
    const Clip walk = reference.clip(0), idle = reference.clip(1);
    Character walker;
    REQUIRE(setLayer(*walker.world, walker.entity, 0, layer(3, 1.0f, LayerBlend::Additive), {}));
    // At its first sample an additive layer changes nothing.
    walker.step(0.0f);
    CHECK(worstJoint(walker, reference, reference.sample(walk, 0)) < 1e-4f);
    walker.step(0.6f);
    const LocalPose base = reference.sample(walk, 0.6f), at = reference.sample(idle, 0.6f),
                    first = reference.sample(idle, 0);
    LocalPose expected = base;
    for (size_t j = 0; j < expected.rotations.size(); ++j)
    {
        const Quat delta = Reference::multiply(at.rotations[j], Reference::conjugate(first.rotations[j]));
        expected.rotations[j] = Reference::nlerp({0, 0, 0, 1}, Reference::multiply(delta, base.rotations[j]), 1.0f);
        for (int c = 0; c < 3; ++c)
        {
            expected.positions[j][c] = base.positions[j][c] + at.positions[j][c] - first.positions[j][c];
            expected.scales[j][c] = base.scales[j][c] * at.scales[j][c] / first.scales[j][c];
        }
    }
    CHECK(worstJoint(walker, reference, expected) < 1e-4f);
}

TEST_CASE("Layers past the last are refused")
{
    Character walker;
    CHECK(setLayer(*walker.world, walker.entity, maxLayers, layer(3, 1.0f), {}).status() == Status::NotFound);
    CHECK(setLayer(*walker.world, walker.entity, 0, layer(9, 1.0f), {}).status() == Status::NotFound);
    CHECK(clearLayer(*walker.world, walker.entity, maxLayers, 0).status() == Status::NotFound);
}
