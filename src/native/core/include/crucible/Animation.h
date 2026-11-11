#pragma once

// The pose runtime (ADR 0026, 0079, 0081): characters playing animation
// clips (ADR 0078) into poses, one job per character in the Animation phase.
//
//   - Animator, saved in scenes: the skeleton (a model asset) and the clip
//     its base plays, or a state machine choosing its clips. It sits on the
//     character's root; skinned meshes use the pose on themselves or their
//     nearest ancestor (ADR 0080).
//   - PoseHandle, the runtime's: which pose in the native pool is the
//     entity's. Never saved, like LoadedMesh.
//   - Two systems: "Animation.Update" advances clips and fades while the
//     game plays (a gameplay system, ADR 0075); "Animation.Pose" samples,
//     blends, and runs the hierarchy pass every frame, so while editing a
//     character holds its clip's first sample.
//
// A pose is the base (a clip, or a state machine's state) with crossfades,
// its root motion taken out if the Animator moves by it, then up to
// maxLayers layers in order, each overriding the pose below it (weighted,
// masked by joint subtrees) or adding to it (the difference from its clip's
// first sample), then up to maxIkSlots IK adjustments in model space.
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
inline constexpr uint32_t maxIkSlots = 4;

// A character: the skeleton its pose has, and what its base plays.
struct CRUCIBLE_EXPOSE Animator
{
    CRUCIBLE_HINT_ASSET_SKINNED assets::AssetId skeleton; // a model asset whose MESH file has the skeleton (JNTS)
    CRUCIBLE_HINT_ASSET(".crclip") assets::AssetId clip;  // a .crclip, or none for the bind pose
    CRUCIBLE_HINT_RANGE(0, 4) float speed;
    bool loop;
    // A .crstatemachine; when set, it plays the base instead of the clip.
    CRUCIBLE_HINT_ASSET(".crstatemachine") assets::AssetId stateMachine;
    // Move the entity by the base's root motion (moveCharacter) instead of
    // letting the root joint travel.
    bool rootMotion;
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
    CRUCIBLE_HINT_ASSET(".crclip") assets::AssetId clip;
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
// can't be read; Unsupported while a state machine plays the base.
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

// --- State machines (ADR 0081) ---------------------------------------------

// Sets a parameter of the entity's state machine by name: a Float, a Bool,
// or a Trigger (set until a transition testing it is taken). NotFound
// without a state machine or such a parameter of that type.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE Result<void> setFloat(ecs::World& world, ecs::Entity entity, std::string_view parameter,
                                                      float setting);
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE Result<void> setBool(ecs::World& world, ecs::Entity entity, std::string_view parameter,
                                                     bool setting);
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE Result<void> trigger(ecs::World& world, ecs::Entity entity, std::string_view parameter);
// The state the entity's state machine is in, or -1; a state by name, or -1.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE int32_t currentState(const ecs::World& world, ecs::Entity entity);
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE int32_t findState(ecs::World& world, ecs::Entity entity, std::string_view name);

// --- Events and windows (ADR 0081) ------------------------------------------

enum class CRUCIBLE_EXPOSE AnimationEventKind : uint32_t
{
    Event,       // a clip's event was passed
    WindowOpen,  // a clip's window opened
    WindowClose, // and closed
};

// Something the last "Animation.Update" passed. The timestamp is when, on
// the input clock (platform::timestampNow): the update covers the time from
// the last frame's start to this one's, and an event passed a share of the
// way through is that share of the way between them. Ones not passed by a
// clip's time (a window opened by a state change) are at this frame's start.
struct CRUCIBLE_EXPOSE AnimationEvent
{
    ecs::Entity entity;
    AnimationEventKind kind;
    uint32_t name;  // eventName of its name
    float clipTime; // where on its clip, in seconds
    uint64_t timestamp;
};

// A window open on a character: when it opened, and when it will close if
// its clip plays on at its speed (both on the input clock).
struct CRUCIBLE_EXPOSE OpenWindow
{
    uint32_t name;
    uint64_t opened;
    uint64_t closes;
};

// The hash events and windows are known by (32-bit FNV-1a of the UTF-8).
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE uint32_t eventName(std::string_view name);
// The last update's events of the world, by timestamp, as many as fit;
// returns how many there are. Events come from the clip with the most
// weight in the base and in each layer.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE uint32_t events(const ecs::World& world, std::span<AnimationEvent> into);
// The entity's open windows, as many as fit; returns how many there are.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE uint32_t openWindows(const ecs::World& world, ecs::Entity entity,
                                                     std::span<OpenWindow> into);

// --- Root motion (ADR 0081) -------------------------------------------------

// How far the last update moved an entity by root motion: a translation in
// its parent's space (what moveCharacter was given) and a turn about its up
// axis. Zero without Animator.rootMotion.
struct CRUCIBLE_EXPOSE RootMotion
{
    Vector3 translation;
    Quaternion rotation;
};

CRUCIBLE_EXPORT CRUCIBLE_EXPOSE RootMotion rootMotion(const ecs::World& world, ecs::Entity entity);

// --- IK (ADR 0081) ----------------------------------------------------------

// Two-bone IK: `joint` (a hand, a foot), its parent, and theirs bend so it
// reaches the target, toward the pole (or as they're bent already).
struct CRUCIBLE_EXPOSE TwoBoneIkDesc
{
    int32_t joint;  // the end of the chain (findJoint)
    Vector3 target; // world space
    Vector3 pole;   // world space: the middle joint bends toward it
    bool usePole;
    float weight;      // 0 to 1
    float fadeSeconds; // to reach the weight
};

// Look-at: `joint` turns so its aim axis points at the target, at most
// maxAngle from where the pose points it.
struct CRUCIBLE_EXPOSE LookAtDesc
{
    int32_t joint;
    Vector3 target; // world space
    Vector3 aim;    // in the joint's own space
    float maxAngle; // radians
    float weight;
    float fadeSeconds;
};

// Sets IK slot `slot` (below maxIkSlots), applied after the layers, in slot
// order. Call each frame as targets move; a new weight fades in over
// fadeSeconds. NotFound past maxIkSlots, for an entity without a pose, or
// a joint without two ancestors (two-bone) or none at all (look-at).
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE Result<void> setTwoBoneIk(ecs::World& world, ecs::Entity entity, uint32_t slot,
                                                          TwoBoneIkDesc desc);
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE Result<void> setLookAt(ecs::World& world, ecs::Entity entity, uint32_t slot,
                                                       LookAtDesc desc);
// Fades the slot's weight to 0 over fadeSeconds, then clears it.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE Result<void> clearIk(ecs::World& world, ecs::Entity entity, uint32_t slot,
                                                     float fadeSeconds);

// --- Joints -----------------------------------------------------------------

// A joint of the entity's skeleton by name, or -1.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE int32_t findJoint(ecs::World& world, ecs::Entity entity, std::string_view name);
// The joint's model-space matrix as last posed (relative to the entity);
// identity if it has no pose or no such joint.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE Matrix4x4 jointModelTransform(const ecs::World& world, ecs::Entity entity,
                                                              int32_t joint);

// The pose a PoseHandle names (its `pose`, the pool index plus one), for
// skinning (ADR 0080): its joints' model-space matrices as last posed, empty
// for none or one not posed yet; a joint of its skeleton by name, or -1; and
// its skeleton's identity, never reused, even by the same skeleton read
// again (0 for none), for caches keyed by it. Main thread, between poses.
CRUCIBLE_EXPORT std::span<const Matrix4x4> poseJoints(uint32_t pose);
CRUCIBLE_EXPORT int32_t poseJointIndex(uint32_t pose, std::string_view name);
CRUCIBLE_EXPORT uint64_t poseSkeletonKey(uint32_t pose);

// Adds debug lines (crucible/DebugLines.h) from every posed joint to its
// parent, in world space through the entity's WorldTransform.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE void drawSkeletons(const ecs::World& world, Vector4 color);

// Forgets a skeleton, clip, or state machine read before (the editor after a reimport):
// poses read it again next frame. True if it was loaded.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE bool reloadAnimationAsset(assets::AssetId asset);

// Frees the world's poses (before destroying it).
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE_INTERNAL void releasePoses(const ecs::World& world);

// How many poses the pool holds, for tests.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE_INTERNAL uint32_t poseCount();

// The systems' bodies, for tests and schedules built by hand: advance every
// pose of the world by deltaSeconds, the frame starting at frameStartNs on
// the input clock; sample, blend, and pose them.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE_INTERNAL void advancePoses(ecs::World& world, float deltaSeconds,
                                                           uint64_t frameStartNs);
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE_INTERNAL void evaluatePoses(ecs::World& world);

// Registers the components and both systems. The engine calls it at start-up.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE_INTERNAL void registerAnimation();

} // namespace crucible::animation
