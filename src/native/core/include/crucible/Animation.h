#pragma once

// The pose runtime (ADR 0026, 0079): characters playing animation clips
// (ADR 0078) into poses, one job per character in the Animation phase.
//
//   - Animator, saved in scenes: the skeleton (a model asset) and the clip
//     its base plays. It sits on the character's root; skinned meshes use
//     the pose on themselves or their nearest ancestor (M8 step 3).
//   - PoseHandle, the runtime's: which pose in the native pool is the
//     entity's. Never saved, like LoadedMesh.
//   - Two systems: "Animation.Update" advances clips and fades while the
//     game plays (a gameplay system, ADR 0075); "Animation.Pose" samples,
//     blends, and runs the hierarchy pass every frame, so while editing a
//     character holds its clip's first sample.
//
// A pose is the base clip with crossfades, then up to maxLayers layers in
// order, each overriding the pose below it (weighted, masked by joint
// subtrees) or adding to it (the difference from its clip's first sample).
// Joints are local transforms (relative to the parent joint) and model-space
// matrices (relative to the Animator's entity, for row vectors). Main
// thread, except where noted.

#include <crucible/AssetSource.h>
#include <crucible/Ecs.h>
#include <crucible/Export.h>
#include <crucible/Expose.h>
#include <crucible/Math.h>
#include <crucible/Result.h>

#include <cstdint>
#include <span>
#include <string_view>

namespace crucible::animation {

inline constexpr std::string_view updateSystemName = "Animation.Update";
inline constexpr std::string_view poseSystemName = "Animation.Pose";

inline constexpr uint32_t maxLayers = 8;

// A character: the skeleton its pose has, and the clip its base plays.
struct CRUCIBLE_EXPOSE Animator
{
    assets::AssetId skeleton; // a model asset whose MESH file has the skeleton (JNTS)
    assets::AssetId clip;     // a .crclip, or none for the bind pose
    CRUCIBLE_HINT_RANGE(0, 4) float speed;
    bool loop;
};

// The entity's pose in the pool, set by "Animation.Pose" (index + 1; 0 is
// none yet). Never saved.
struct CRUCIBLE_EXPOSE PoseHandle
{
    uint32_t pose;
};

enum class CRUCIBLE_EXPOSE LayerBlend : uint32_t
{
    Override, // replaces the pose below, by weight and mask
    Additive, // adds the difference between its clip and the clip's first sample
};

struct CRUCIBLE_EXPOSE LayerDesc
{
    assets::AssetId clip;
    float weight; // 0 to 1
    LayerBlend blend;
    float speed;
    bool loop;
    float fadeSeconds; // to reach the weight (or a new clip, if the layer was playing)
};

// A mask's entry: a joint (findJoint) and everything below it, at a weight.
// Later entries win where subtrees overlap. A layer without one is unmasked.
struct CRUCIBLE_EXPOSE MaskEntry
{
    int32_t joint;
    float weight;
};

// Plays a clip on the entity's base, fading from the one playing over
// fadeSeconds (0 cuts), from its start; sets Animator.clip to it. The
// entity needs an Animator. NotFound if it has none or the skeleton or clip
// can't be read.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE Result<void> play(ecs::World& world, ecs::Entity entity, assets::AssetId clip,
                                                  float fadeSeconds);

// Sets layer `layer` (below maxLayers; NotFound past it, as for an entity
// without an Animator or a clip that can't be read): a new clip starts from its start,
// fading from the layer's last clip if it was playing; the weight fades to
// desc.weight over desc.fadeSeconds. The mask is copied.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE Result<void> setLayer(ecs::World& world, ecs::Entity entity, uint32_t layer,
                                                      LayerDesc desc, std::span<const MaskEntry> mask);
// Fades the layer's weight to 0 over fadeSeconds, then clears it.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE Result<void> clearLayer(ecs::World& world, ecs::Entity entity, uint32_t layer,
                                                        float fadeSeconds);

// A joint of the entity's skeleton by name, or -1.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE int32_t findJoint(ecs::World& world, ecs::Entity entity, std::string_view name);
// The joint's model-space matrix as last posed (relative to the entity);
// identity if it has no pose or no such joint.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE Matrix4x4 jointModelTransform(const ecs::World& world, ecs::Entity entity,
                                                              int32_t joint);

// Adds debug lines (crucible/DebugLines.h) from every posed joint to its
// parent, in world space through the entity's WorldTransform.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE void drawSkeletons(const ecs::World& world, Vector4 color);

// Forgets a skeleton or clip read before (the editor after a reimport):
// poses read it again next frame. True if it was loaded.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE bool reloadAnimationAsset(assets::AssetId asset);

// Frees the world's poses (before destroying it).
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE_INTERNAL void releasePoses(const ecs::World& world);

// How many poses the pool holds, for tests.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE_INTERNAL uint32_t poseCount();

// The systems' bodies, for tests and schedules built by hand: advance every
// pose of the world by deltaSeconds; sample, blend, and pose them.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE_INTERNAL void advancePoses(ecs::World& world, float deltaSeconds);
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE_INTERNAL void evaluatePoses(ecs::World& world);

// Registers the components and both systems. The engine calls it at start-up.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE_INTERNAL void registerAnimation();

} // namespace crucible::animation
