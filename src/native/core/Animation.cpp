// The pose runtime (ADR 0079, 0081): skeletons, clips, and state machines
// read through the asset source and cached by ID, a pool of poses, and the
// two systems. Poses are structure of arrays (ADR 0026): blocks of
// wideLanes joints, sampled and blended with the wide types; the hierarchy
// pass, root motion, and IK are per joint, in model space.

#include "StateMachine.h"

#include <crucible/Animation.h>
#include <crucible/BinaryFile.h>
#include <crucible/ClipFile.h>
#include <crucible/DebugLines.h>
#include <crucible/HashMap.h>
#include <crucible/Jobs.h>
#include <crucible/MeshFile.h>
#include <crucible/Physics.h>
#include <crucible/SimdWide.h>
#include <crucible/Systems.h>
#include <crucible/Transform.h>
#include <crucible/Vector.h>

#include <algorithm>
#include <cmath>
#include <cstring>

namespace crucible::animation {

using assets::AssetId;

namespace {

constexpr MemoryTag tag = MemoryTag::Animation;

struct AssetIdHash
{
    uint64_t operator()(const AssetId& id) const
    {
        uint64_t low, high;
        std::memcpy(&low, id.bytes, 8);
        std::memcpy(&high, id.bytes + 8, 8);
        return Hash<uint64_t>()(low ^ (high * 0x9e3779b97f4a7c15ull));
    }
};

bool isNone(const AssetId& id)
{
    for (const uint8_t byte : id.bytes)
    {
        if (byte != 0)
        {
            return false;
        }
    }
    return true;
}

Status readAsset(const AssetId& id, Vector<uint8_t>& out)
{
    auto deliver = [](void* context, const uint8_t* data, uint64_t size) {
        static_cast<Vector<uint8_t>*>(context)->assign(std::span<const uint8_t>(data, static_cast<size_t>(size)));
    };
    out.clear();
    return assets::assetSource().read(&id, &out, deliver) == 0 ? Status::Ok : Status::NotFound;
}

Matrix4x4 trsMatrix(Vector3 position, Quaternion rotation, Vector3 scale)
{
    Matrix4x4 m = Matrix4x4::rotation(rotation);
    m.rows[0] = m.rows[0] * scale.x;
    m.rows[1] = m.rows[1] * scale.y;
    m.rows[2] = m.rows[2] * scale.z;
    m.rows[3] = {position.x, position.y, position.z, 1};
    return m;
}

// ---------------------------------------------------------------------------
// Skeletons and clips
// ---------------------------------------------------------------------------

struct Skeleton
{
    uint64_t key = 0; // unique to this load, for caches (poseSkeletonKey)
    uint32_t jointCount = 0;
    uint32_t blocks = 0; // jointCount in wideLanes, rounded up
    Vector<int32_t> parents{tag};
    // The rest pose, by joint (padded to blocks).
    Vector<Quaternion> restRotations{tag};
    Vector<Vector3> restPositions{tag};
    Vector<Vector3> restScales{tag};
    // Nodes that aren't joints between a joint and its parent joint (an
    // "Armature" above the root): their rest matrices, folded into one.
    Vector<Matrix4x4> offsets{tag};
    Vector<uint8_t> hasOffset{tag};
    Vector<char> names{tag};
    Vector<uint32_t> nameOffsets{tag}; // jointCount + 1
    std::string_view name(uint32_t joint) const
    {
        return {names.data() + nameOffsets[joint], nameOffsets[joint + 1] - nameOffsets[joint]};
    }
};

struct Clip
{
    assets::ClipInfo info{};
    Vector<assets::ClipTrack> tracks{tag};
    Vector<uint16_t> rotations{tag};
    Vector<uint16_t> translations{tag};
    Vector<uint16_t> scales{tag};
    Vector<char> names{tag};
    Vector<assets::ClipEvent> events{tag};   // by time
    Vector<assets::ClipWindow> windows{tag}; // by start
    std::string_view trackName(const assets::ClipTrack& track) const
    {
        return {names.data() + track.nameOffset, track.nameLength};
    }
};

// A clip on a skeleton: each joint's track (-1: it holds its rest pose),
// and the first sample, which additive layers subtract.
// Where a clip has the skeleton's root joint at a time, in model space:
// its position, and its facing as a turn about the up axis.
struct RootSample
{
    Vector3 position{};
    float yaw = 0;
};

struct Binding
{
    const Clip* clip = nullptr;
    Vector<int32_t> trackOf{tag};
    Vector<Quaternion> firstRotations{tag};
    Vector<Vector3> firstPositions{tag};
    Vector<Vector3> firstScales{tag};
    // Root motion (ADR 0081): the skeleton's root joint, if the clip moves
    // it (-1 if not); the row of its model matrix whose direction is its
    // facing (z, or x for a root whose z points up); and the root at the
    // clip's start and end.
    int32_t root = -1;
    int32_t facingAxis = 2;
    RootSample first{};
    RootSample last{};
};

Skeleton* loadSkeleton(const AssetId& id)
{
    Vector<uint8_t> bytes(tag);
    if (readAsset(id, bytes) != Status::Ok)
    {
        return nullptr;
    }
    Result<binary::File*> opened = binary::parseFile(bytes.span(), assets::meshFileKind, assets::meshFileVersion);
    if (!opened)
    {
        return nullptr;
    }
    binary::File* file = opened.value();
    Vector<assets::MeshNode> nodes(tag);
    Vector<assets::JointEntry> joints(tag);
    Vector<char> names(tag);
    const bool read =
        readTable(*file, "NODE", nodes) && readTable(*file, "JNTS", joints) && readTable(*file, "NAME", names);
    binary::closeFile(file);
    if (!read || joints.empty())
    {
        return nullptr;
    }
    Skeleton* skeleton = create<Skeleton>(tag);
    static uint64_t loads = 0;
    skeleton->key = ++loads;
    skeleton->jointCount = static_cast<uint32_t>(joints.size());
    skeleton->blocks = (skeleton->jointCount + wideLanes - 1) / wideLanes;
    const size_t padded = size_t{skeleton->blocks} * wideLanes;
    skeleton->restRotations.resize(padded);
    skeleton->restPositions.resize(padded);
    skeleton->restScales.resize(padded);
    for (size_t j = skeleton->jointCount; j < padded; ++j)
    {
        skeleton->restRotations[j] = Quaternion::identity();
        skeleton->restScales[j] = {1, 1, 1};
    }
    Vector<int32_t> jointOfNode(tag);
    jointOfNode.resize(nodes.size());
    std::fill(jointOfNode.begin(), jointOfNode.end(), -1);
    for (size_t j = 0; j < joints.size(); ++j)
    {
        if (joints[j].node >= 0 && static_cast<size_t>(joints[j].node) < nodes.size())
        {
            jointOfNode[joints[j].node] = static_cast<int32_t>(j);
        }
    }
    skeleton->nameOffsets.pushBack(0);
    for (size_t j = 0; j < joints.size(); ++j)
    {
        const assets::JointEntry& joint = joints[j];
        const bool valid =
            joint.node >= 0 && static_cast<size_t>(joint.node) < nodes.size() && joint.parent < static_cast<int32_t>(j);
        skeleton->parents.pushBack(valid ? joint.parent : -1);
        Matrix4x4 offset = Matrix4x4::identity();
        bool hasOffset = false;
        if (valid)
        {
            const assets::MeshNode& node = nodes[joint.node];
            skeleton->restPositions[j] = {node.translation[0], node.translation[1], node.translation[2]};
            skeleton->restRotations[j] = {node.rotation[0], node.rotation[1], node.rotation[2], node.rotation[3]};
            skeleton->restScales[j] = {node.scale[0], node.scale[1], node.scale[2]};
            // Up from the joint's node to its parent joint's (or the top).
            for (int32_t n = node.parent, steps = 0;
                 n >= 0 && static_cast<size_t>(n) < nodes.size() && jointOfNode[n] != joint.parent && steps < 1024;
                 n = nodes[n].parent, ++steps)
            {
                const assets::MeshNode& above = nodes[n];
                offset =
                    offset * trsMatrix({above.translation[0], above.translation[1], above.translation[2]},
                                       {above.rotation[0], above.rotation[1], above.rotation[2], above.rotation[3]},
                                       {above.scale[0], above.scale[1], above.scale[2]});
                hasOffset = true;
            }
        }
        else
        {
            skeleton->restRotations[j] = Quaternion::identity();
            skeleton->restScales[j] = {1, 1, 1};
        }
        skeleton->offsets.pushBack(offset);
        skeleton->hasOffset.pushBack(hasOffset ? 1 : 0);
        const uint32_t start = std::min<uint32_t>(joint.nameOffset, static_cast<uint32_t>(names.size()));
        const uint32_t length = std::min<uint32_t>(joint.nameLength, static_cast<uint32_t>(names.size()) - start);
        for (uint32_t c = 0; c < length; ++c)
        {
            skeleton->names.pushBack(names[start + c]);
        }
        skeleton->nameOffsets.pushBack(static_cast<uint32_t>(skeleton->names.size()));
    }
    return skeleton;
}

Clip* loadClip(const AssetId& id)
{
    Vector<uint8_t> bytes(tag);
    if (readAsset(id, bytes) != Status::Ok)
    {
        return nullptr;
    }
    Result<binary::File*> opened = binary::parseFile(bytes.span(), assets::clipFileKind, assets::clipFileVersion);
    if (!opened)
    {
        return nullptr;
    }
    binary::File* file = opened.value();
    Clip* clip = create<Clip>(tag);
    Vector<assets::ClipInfo> info(tag);
    const bool read = readTable(*file, "INFO", info) && readTable(*file, "TRCK", clip->tracks) &&
                      readTable(*file, "ROTS", clip->rotations) && readTable(*file, "POSS", clip->translations) &&
                      readTable(*file, "SCLS", clip->scales) && readTable(*file, "NAME", clip->names);
    // Events and windows are optional (ADR 0081).
    (void)readTable(*file, "EVNT", clip->events);
    (void)readTable(*file, "WNDW", clip->windows);
    binary::closeFile(file);
    if (!read || info.empty() || info[0].sampleCount == 0 || info[0].sampleRate <= 0)
    {
        destroy(tag, clip);
        return nullptr;
    }
    clip->info = info[0];
    // Every offset in range, so sampling needn't check.
    const uint64_t samples = clip->info.sampleCount;
    for (const assets::ClipTrack& track : clip->tracks)
    {
        auto fits = [&](uint32_t offset, uint32_t bit, size_t values, size_t per) {
            const uint64_t count = (track.constant & bit) != 0 ? 1 : samples;
            return (uint64_t{offset} + count) * per <= values;
        };
        if (!fits(track.rotationOffset, static_cast<uint32_t>(assets::ClipChannel::Rotation), clip->rotations.size(),
                  4) ||
            !fits(track.translationOffset, static_cast<uint32_t>(assets::ClipChannel::Translation),
                  clip->translations.size(), 3) ||
            !fits(track.scaleOffset, static_cast<uint32_t>(assets::ClipChannel::Scale), clip->scales.size(), 3) ||
            uint64_t{track.nameOffset} + track.nameLength > clip->names.size())
        {
            destroy(tag, clip);
            return nullptr;
        }
    }
    return clip;
}

// A track's rotation at a sample, from smallest three.
Quaternion trackRotation(const Clip& clip, const assets::ClipTrack& track, uint32_t sample)
{
    const bool constant = (track.constant & static_cast<uint32_t>(assets::ClipChannel::Rotation)) != 0;
    const uint16_t* packed = clip.rotations.data() + (size_t{track.rotationOffset} + (constant ? 0 : sample)) * 4;
    float q[4] = {};
    float sum = 0.0f;
    const uint32_t dropped = packed[3] & 3;
    for (uint32_t c = 0, k = 0; c < 4; ++c)
    {
        if (c != dropped)
        {
            q[c] = (packed[k++] / 65535.0f * 2.0f - 1.0f) * 0.70710678f;
            sum += q[c] * q[c];
        }
    }
    q[dropped] = std::sqrt(std::max(1.0f - sum, 0.0f));
    return {q[0], q[1], q[2], q[3]};
}

Vector3 trackVector(const Vector<uint16_t>& values, uint32_t offset, bool constant, const float* lo, const float* hi,
                    uint32_t sample)
{
    const uint16_t* packed = values.data() + (size_t{offset} + (constant ? 0 : sample)) * 3;
    return {lo[0] + (hi[0] - lo[0]) * (packed[0] / 65535.0f), lo[1] + (hi[1] - lo[1]) * (packed[1] / 65535.0f),
            lo[2] + (hi[2] - lo[2]) * (packed[2] / 65535.0f)};
}

Vector3 trackTranslation(const Clip& clip, const assets::ClipTrack& track, uint32_t sample)
{
    return trackVector(clip.translations, track.translationOffset,
                       (track.constant & static_cast<uint32_t>(assets::ClipChannel::Translation)) != 0,
                       track.translationMin, track.translationMax, sample);
}

Vector3 trackScale(const Clip& clip, const assets::ClipTrack& track, uint32_t sample)
{
    return trackVector(clip.scales, track.scaleOffset,
                       (track.constant & static_cast<uint32_t>(assets::ClipChannel::Scale)) != 0, track.scaleMin,
                       track.scaleMax, sample);
}

constexpr float pi = 3.14159265358979f;

float wrapAngle(float angle)
{
    angle = std::fmod(angle + pi, 2 * pi);
    return (angle < 0 ? angle + 2 * pi : angle) - pi;
}

Quaternion nlerpScalar(Quaternion a, Quaternion b, float t)
{
    const float side = dot(a, b) < 0 ? -1.0f : 1.0f;
    return normalize(Quaternion{a.x * (1 - t) + b.x * side * t, a.y * (1 - t) + b.y * side * t,
                                a.z * (1 - t) + b.z * side * t, a.w * (1 - t) + b.w * side * t});
}

// The root joint's model matrix at a time: its own track sampled (the root
// has no parent joint, only, perhaps, nodes above it).
Matrix4x4 rootMatrix(const Binding& binding, const Skeleton& skeleton, float time)
{
    const Clip& clip = *binding.clip;
    const assets::ClipTrack& track = clip.tracks[binding.trackOf[binding.root]];
    const float at = std::max(time, 0.0f) * clip.info.sampleRate;
    const uint32_t i0 = std::min(static_cast<uint32_t>(at), clip.info.sampleCount - 1);
    const uint32_t i1 = std::min(i0 + 1, clip.info.sampleCount - 1);
    const float t = std::clamp(at - static_cast<float>(i0), 0.0f, 1.0f);
    const Vector3 p0 = trackTranslation(clip, track, i0), p1 = trackTranslation(clip, track, i1);
    const Vector3 s0 = trackScale(clip, track, i0), s1 = trackScale(clip, track, i1);
    Matrix4x4 m =
        trsMatrix(p0 + (p1 - p0) * t, nlerpScalar(trackRotation(clip, track, i0), trackRotation(clip, track, i1), t),
                  s0 + (s1 - s0) * t);
    return skeleton.hasOffset[binding.root] != 0 ? m * skeleton.offsets[binding.root] : m;
}

RootSample rootAt(const Binding& binding, const Skeleton& skeleton, float time)
{
    const Matrix4x4 m = rootMatrix(binding, skeleton, time);
    const Vector4& facing = m.rows[binding.facingAxis];
    // A turn of yaw about +y takes +z to (sin, 0, cos) and +x to (cos, 0, -sin).
    const float yaw = binding.facingAxis == 2 ? std::atan2(facing.x, facing.z) : std::atan2(-facing.z, facing.x);
    return {{m.rows[3].x, m.rows[3].y, m.rows[3].z}, yaw};
}

Binding* bind(const Clip& clip, const Skeleton& skeleton)
{
    Binding* binding = create<Binding>(tag);
    binding->clip = &clip;
    const size_t padded = size_t{skeleton.blocks} * wideLanes;
    binding->trackOf.resize(padded);
    binding->firstRotations.resize(padded);
    binding->firstPositions.resize(padded);
    binding->firstScales.resize(padded);
    for (size_t j = 0; j < padded; ++j)
    {
        int32_t found = -1;
        for (size_t t = 0; j < skeleton.jointCount && t < clip.tracks.size() && found < 0; ++t)
        {
            found = clip.trackName(clip.tracks[t]) == skeleton.name(static_cast<uint32_t>(j)) ? static_cast<int32_t>(t)
                                                                                              : -1;
        }
        binding->trackOf[j] = found;
        if (found >= 0)
        {
            const assets::ClipTrack& track = clip.tracks[found];
            binding->firstRotations[j] = trackRotation(clip, track, 0);
            binding->firstPositions[j] = trackTranslation(clip, track, 0);
            binding->firstScales[j] = trackScale(clip, track, 0);
        }
        else
        {
            binding->firstRotations[j] = skeleton.restRotations[j];
            binding->firstPositions[j] = skeleton.restPositions[j];
            binding->firstScales[j] = skeleton.restScales[j];
        }
    }
    for (uint32_t j = 0; j < skeleton.jointCount && binding->root < 0; ++j)
    {
        if (skeleton.parents[j] < 0)
        {
            binding->root = binding->trackOf[j] >= 0 ? static_cast<int32_t>(j) : -2;
        }
    }
    if (binding->root >= 0)
    {
        // Facing from the axis that lies flattest at the start, so its turn
        // about up is well defined.
        const Vector4& z = rootMatrix(*binding, skeleton, 0).rows[2];
        binding->facingAxis = z.x * z.x + z.z * z.z >= 0.25f * (z.x * z.x + z.y * z.y + z.z * z.z) ? 2 : 0;
        binding->first = rootAt(*binding, skeleton, 0);
        binding->last = rootAt(*binding, skeleton, clip.info.duration);
    }
    else
    {
        binding->root = -1;
    }
    return binding;
}

struct BindingKey
{
    AssetId clip;
    AssetId skeleton;
    bool operator==(const BindingKey& other) const
    {
        return clip == other.clip && skeleton == other.skeleton;
    }
};

struct BindingKeyHash
{
    uint64_t operator()(const BindingKey& key) const
    {
        return AssetIdHash()(key.clip) * 31 + AssetIdHash()(key.skeleton);
    }
};

// What's been read, by ID; null for an asset that couldn't be, so it isn't
// read again every frame (reloadAnimationAsset forgets it).
HashMap<AssetId, Skeleton*, AssetIdHash> skeletons{tag};
HashMap<AssetId, Clip*, AssetIdHash> clips{tag};
HashMap<BindingKey, Binding*, BindingKeyHash> bindings{tag};
HashMap<AssetId, StateMachine*, AssetIdHash> machines{tag};

const Skeleton* skeletonOf(const AssetId& id)
{
    if (isNone(id))
    {
        return nullptr;
    }
    if (Skeleton** found = skeletons.find(id))
    {
        return *found;
    }
    Skeleton* loaded = loadSkeleton(id);
    skeletons.insertOrAssign(id, loaded);
    return loaded;
}

const Clip* clipOf(const AssetId& id)
{
    if (isNone(id))
    {
        return nullptr;
    }
    if (Clip** found = clips.find(id))
    {
        return *found;
    }
    Clip* loaded = loadClip(id);
    clips.insertOrAssign(id, loaded);
    return loaded;
}

const Binding* bindingOf(const AssetId& clipId, const AssetId& skeletonId, const Skeleton& skeleton)
{
    const Clip* clip = clipOf(clipId);
    if (clip == nullptr)
    {
        return nullptr;
    }
    const BindingKey key{clipId, skeletonId};
    if (Binding** found = bindings.find(key))
    {
        return *found;
    }
    Binding* made = bind(*clip, skeleton);
    bindings.insertOrAssign(key, made);
    return made;
}

const StateMachine* machineOf(const AssetId& id)
{
    if (isNone(id))
    {
        return nullptr;
    }
    if (StateMachine** found = machines.find(id))
    {
        return *found;
    }
    Vector<uint8_t> bytes(tag);
    StateMachine* loaded = readAsset(id, bytes) == Status::Ok ? parseStateMachine(bytes.span()) : nullptr;
    machines.insertOrAssign(id, loaded);
    return loaded;
}

// ---------------------------------------------------------------------------
// Poses
// ---------------------------------------------------------------------------

float clipDuration(const AssetId& id)
{
    const Clip* clip = clipOf(id);
    return clip != nullptr ? clip->info.duration : 0.0f;
}

// One clip playing, with the one it's fading from.
struct Player
{
    AssetId clip{};
    float time = 0;
    AssetId previous{};
    float previousTime = 0;
    float fadeElapsed = 0;
    float fadeDuration = 0; // 0: no fade
    bool fresh = true;      // just started: an event at its first time counts
    // Resolved on the main thread before the jobs run.
    const Binding* binding = nullptr;
    const Binding* previousBinding = nullptr;

    void start(const AssetId& next, float fadeSeconds)
    {
        if (fadeSeconds > 0 && !isNone(clip))
        {
            previous = clip;
            previousTime = time;
            fadeElapsed = 0;
            fadeDuration = fadeSeconds;
        }
        else
        {
            previous = {};
            fadeDuration = 0;
        }
        clip = next;
        time = 0;
        fresh = true;
    }
};

// A state machine's state playing (ADR 0081): its clips at their weights,
// kept in step by one phase, so a blend space's walk and run plant their
// feet together.
struct StatePlay
{
    int32_t state = -1;
    float phase = 0; // 0 to 1 through the state
    bool fresh = true;
    // From the parameters, refreshed on the main thread every frame.
    uint32_t count = 0;
    AssetId clips[maxStateClips]{};
    float weights[maxStateClips]{};
    float durations[maxStateClips]{};
    const Binding* bindings[maxStateClips]{};
    float duration = 0; // the clips' durations, weighted
};

struct MachinePlay
{
    AssetId id{};
    const StateMachine* machine = nullptr;
    Vector<float> parameters{tag};
    StatePlay current;
    StatePlay previous;
    float fadeElapsed = 0;
    float fadeDuration = 0;

    void enter(uint32_t state, float blendSeconds)
    {
        if (blendSeconds > 0 && current.state >= 0)
        {
            previous = current;
            fadeElapsed = 0;
            fadeDuration = blendSeconds;
        }
        else
        {
            previous = StatePlay{};
            fadeDuration = 0;
        }
        current = StatePlay{};
        current.state = static_cast<int32_t>(state);
    }
};

struct Layer
{
    bool active = false;
    LayerDesc desc{};
    Player player;
    float weight = 0;
    float fadeRate = 0; // weight per second toward desc.weight (or 0 when clearing)
    bool clearing = false;
    Vector<MaskEntry> mask{tag};
    Vector<float> jointWeights{tag}; // from the mask, per joint (padded); empty: unmasked
    bool masked = false;
};

enum class IkKind : uint32_t
{
    None,
    TwoBone,
    LookAt,
};

struct IkSlot
{
    IkKind kind = IkKind::None;
    TwoBoneIkDesc twoBone{};
    LookAtDesc lookAt{};
    float weight = 0;
    float goal = 0;
    float fadeRate = 0; // weight per second toward the goal (or 0 when clearing)
    bool clearing = false;
};

// A window open on a pose: from which source (0 the base, 1 + a layer), clip,
// and window of it.
struct WindowState
{
    AssetId clip{};
    uint32_t source = 0;
    uint32_t window = 0;
    float end = 0;
    OpenWindow open{};
    bool seen = false;
};

struct Pose
{
    const ecs::World* world = nullptr;
    ecs::Entity entity{};
    AssetId skeletonId{};
    const Skeleton* skeleton = nullptr;
    uint32_t blocks = 0;
    Vector<QuaternionWide> rotations{tag};
    Vector<Vector3Wide> positions{tag};
    Vector<Vector3Wide> scales{tag};
    // Two more sets: what's sampled before blending into the pose.
    Vector<QuaternionWide> scratchRotations{tag};
    Vector<Vector3Wide> scratchPositions{tag};
    Vector<Vector3Wide> scratchScales{tag};
    Vector<QuaternionWide> fadeRotations{tag};
    Vector<Vector3Wide> fadePositions{tag};
    Vector<Vector3Wide> fadeScales{tag};
    Vector<Matrix4x4> model{tag};
    Vector<uint8_t> marks{tag}; // per joint: IK's subtree
    // From the Animator, each frame.
    float speed = 1;
    bool loop = true;
    bool rootMotion = false;
    Player base;
    MachinePlay machine;
    Layer layers[maxLayers];
    IkSlot ik[maxIkSlots];
    Matrix4x4 toModel = Matrix4x4::identity(); // world to model space, for IK targets
    RootMotion moved{{0, 0, 0}, Quaternion::identity()};
    Vector<WindowState> windows{tag};
    bool posed = false;

    void fit(const Skeleton* next)
    {
        skeleton = next;
        blocks = next != nullptr ? next->blocks : 0;
        for (Vector<QuaternionWide>* set : {&rotations, &scratchRotations, &fadeRotations})
        {
            set->resize(blocks);
        }
        for (Vector<Vector3Wide>* set :
             {&positions, &scratchPositions, &fadePositions, &scales, &scratchScales, &fadeScales})
        {
            set->resize(blocks);
        }
        model.resize(next != nullptr ? next->jointCount : 0);
        std::fill(model.begin(), model.end(), Matrix4x4::identity());
        marks.resize(model.size());
        std::fill(marks.begin(), marks.end(), uint8_t{0});
        for (Layer& layer : layers)
        {
            layer.jointWeights.clear();
            layer.masked = false;
        }
        // Joints by index mean nothing on another skeleton.
        for (IkSlot& slot : ik)
        {
            slot = IkSlot{};
        }
        posed = false;
    }
};

struct PoseKey
{
    const ecs::World* world;
    uint64_t entity;
    bool operator==(const PoseKey& other) const
    {
        return world == other.world && entity == other.entity;
    }
};

struct PoseKeyHash
{
    uint64_t operator()(const PoseKey& key) const
    {
        return Hash<uint64_t>()(static_cast<uint64_t>(reinterpret_cast<uintptr_t>(key.world)) * 0x9e3779b97f4a7c15ull ^
                                key.entity);
    }
};

Vector<Pose*> pool{tag}; // null: a free slot
Vector<uint32_t> freeSlots{tag};
HashMap<PoseKey, uint32_t, PoseKeyHash> poseOf{tag};

// The last update's events, every world's, each world's by timestamp.
struct Recorded
{
    const ecs::World* world;
    AnimationEvent event;
};
Vector<Recorded> recorded{tag};

// Drops a world's events, keeping the others' in order.
void forgetEvents(const ecs::World& world)
{
    size_t kept = 0;
    for (size_t i = 0; i < recorded.size(); ++i)
    {
        if (recorded[i].world != &world)
        {
            recorded[kept++] = recorded[i];
        }
    }
    recorded.resize(kept);
}

Pose* findPose(const ecs::World& world, ecs::Entity entity)
{
    const uint32_t* index = poseOf.find({&world, entity.value});
    return index != nullptr ? pool[*index] : nullptr;
}

uint32_t indexOf(const Pose* pose)
{
    return *poseOf.find({pose->world, pose->entity.value});
}

void freePose(uint32_t index)
{
    Pose* pose = pool[index];
    poseOf.erase({pose->world, pose->entity.value});
    destroy(tag, pose);
    pool[index] = nullptr;
    freeSlots.pushBack(index);
}

// The entity's pose, made if it has an Animator and none yet; its skeleton
// read (again, if the Animator names another). Null without an Animator.
Pose* ensurePose(ecs::World& world, ecs::Entity entity)
{
    const Animator* animator = ecs::read<Animator>(world, entity);
    if (animator == nullptr)
    {
        return nullptr;
    }
    Pose* pose = findPose(world, entity);
    if (pose == nullptr)
    {
        pose = create<Pose>(tag);
        pose->world = &world;
        pose->entity = entity;
        uint32_t index;
        if (!freeSlots.empty())
        {
            index = freeSlots.back();
            freeSlots.popBack();
            pool[index] = pose;
        }
        else
        {
            index = static_cast<uint32_t>(pool.size());
            pool.pushBack(pose);
        }
        poseOf.insertOrAssign({&world, entity.value}, index);
        pose->base.clip = animator->clip;
    }
    const Skeleton* skeleton = skeletonOf(animator->skeleton);
    if (!(pose->skeletonId == animator->skeleton) || pose->skeleton != skeleton)
    {
        pose->skeletonId = animator->skeleton;
        pose->fit(skeleton);
    }
    return pose;
}

// The pose follows its Animator: a new clip cuts to it (play() keeps the two
// the same), a new state machine starts in its start state.
void sync(Pose& pose, const Animator& animator)
{
    if (!(animator.clip == pose.base.clip))
    {
        pose.base.start(animator.clip, 0);
    }
    pose.speed = animator.speed;
    pose.loop = animator.loop;
    pose.rootMotion = animator.rootMotion;
    MachinePlay& play = pose.machine;
    const StateMachine* machine = machineOf(animator.stateMachine);
    if (!(animator.stateMachine == play.id) || machine != play.machine)
    {
        play.id = animator.stateMachine;
        play.machine = machine;
        play.parameters.clear();
        play.current = StatePlay{};
        play.previous = StatePlay{};
        play.fadeDuration = 0;
        if (machine != nullptr)
        {
            for (const assets::StateParameter& parameter : machine->parameters)
            {
                play.parameters.pushBack(parameter.defaultValue);
            }
            play.current.state = static_cast<int32_t>(machine->info.start);
        }
    }
}

bool playsMachine(const Pose& pose)
{
    return pose.machine.machine != nullptr && pose.machine.current.state >= 0;
}

// Per-joint weights from a layer's mask: each entry's joint and everything
// below it, later entries over earlier ones.
void buildMask(Layer& layer, const Skeleton& skeleton)
{
    layer.masked = !layer.mask.empty();
    if (!layer.masked)
    {
        layer.jointWeights.clear();
        return;
    }
    layer.jointWeights.resize(size_t{skeleton.blocks} * wideLanes);
    std::fill(layer.jointWeights.begin(), layer.jointWeights.end(), 0.0f);
    for (const MaskEntry& entry : layer.mask)
    {
        if (entry.joint < 0 || static_cast<uint32_t>(entry.joint) >= skeleton.jointCount)
        {
            continue;
        }
        for (uint32_t j = static_cast<uint32_t>(entry.joint); j < skeleton.jointCount; ++j)
        {
            int32_t up = static_cast<int32_t>(j);
            while (up > entry.joint)
            {
                up = skeleton.parents[up];
            }
            if (up == entry.joint)
            {
                layer.jointWeights[j] = std::clamp(entry.weight, 0.0f, 1.0f);
            }
        }
    }
}

float advanceTime(float time, float delta, float duration, bool loop)
{
    time += delta;
    if (duration <= 0)
    {
        return 0;
    }
    if (loop)
    {
        time = std::fmod(time, duration);
        return time < 0 ? time + duration : time;
    }
    return std::clamp(time, 0.0f, duration);
}

void advancePlayer(Player& player, float delta, float speed, bool loop)
{
    player.time = advanceTime(player.time, delta * speed, clipDuration(player.clip), loop);
    player.fresh = false;
    if (player.fadeDuration > 0)
    {
        player.previousTime = advanceTime(player.previousTime, delta * speed, clipDuration(player.previous), loop);
        player.fadeElapsed += delta;
        if (player.fadeElapsed >= player.fadeDuration)
        {
            player.fadeDuration = 0;
            player.previous = {};
        }
    }
}

// Moves a state's phase on; how far, for its transitions' exit times.
StateProgress advanceState(const MachinePlay& play, StatePlay& state, float delta, float speed)
{
    const assets::StateEntry& entry = play.machine->states[static_cast<uint32_t>(state.state)];
    StateProgress progress{state.phase, state.phase, 0, entry.loop != 0};
    if (state.duration > 0)
    {
        const float raw = state.phase + delta * entry.speed * speed / state.duration;
        if (progress.loop)
        {
            const float turns = std::floor(raw);
            state.phase = raw - turns;
            progress.wraps = turns > 0 ? static_cast<uint32_t>(turns) : 0;
        }
        else
        {
            state.phase = std::clamp(raw, 0.0f, 1.0f);
        }
    }
    progress.after = state.phase;
    state.fresh = false;
    return progress;
}

// --- Wide sampling and blending --------------------------------------------

QuaternionWide add(const QuaternionWide& a, const QuaternionWide& b)
{
    return {a.x + b.x, a.y + b.y, a.z + b.z, a.w + b.w};
}

QuaternionWide scale(const QuaternionWide& q, FloatWide s)
{
    return {q.x * s, q.y * s, q.z * s, q.w * s};
}

FloatWide dot(const QuaternionWide& a, const QuaternionWide& b)
{
    return multiplyAdd(a.x, b.x, multiplyAdd(a.y, b.y, multiplyAdd(a.z, b.z, a.w * b.w)));
}

QuaternionWide normalize(const QuaternionWide& q)
{
    return scale(q, FloatWide::splat(1.0f) / sqrt(dot(q, q)));
}

QuaternionWide conjugate(const QuaternionWide& q)
{
    return {-q.x, -q.y, -q.z, q.w};
}

// Normalized lerp along the shorter way: b is flipped where the two point
// apart (a quaternion and its negation are the same rotation).
QuaternionWide nlerp(const QuaternionWide& a, const QuaternionWide& b, FloatWide t)
{
    const FloatWide side = max(min(dot(a, b) * 1e30f, FloatWide::splat(1.0f)), FloatWide::splat(-1.0f));
    return normalize(add(scale(a, FloatWide::splat(1.0f) - t), scale(b, side * t)));
}

Vector3Wide lerpWide(const Vector3Wide& a, const Vector3Wide& b, FloatWide t)
{
    return a + (b - a) * t;
}

Vector3Wide multiplyWide(const Vector3Wide& a, const Vector3Wide& b)
{
    return {a.x * b.x, a.y * b.y, a.z * b.z};
}

// Samples a binding at a time into a set, wideLanes joints at a time: each
// lane's two samples are decoded, then interpolated together.
void sample(const Skeleton& skeleton, const Binding* binding, float time, QuaternionWide* rotations,
            Vector3Wide* positions, Vector3Wide* scales)
{
    uint32_t i0 = 0, i1 = 0;
    float fraction = 0;
    if (binding != nullptr)
    {
        const assets::ClipInfo& info = binding->clip->info;
        const float at = std::max(time, 0.0f) * info.sampleRate;
        i0 = std::min(static_cast<uint32_t>(at), info.sampleCount - 1);
        i1 = std::min(i0 + 1, info.sampleCount - 1);
        fraction = std::clamp(at - static_cast<float>(i0), 0.0f, 1.0f);
    }
    const FloatWide t = FloatWide::splat(fraction);
    for (uint32_t b = 0; b < skeleton.blocks; ++b)
    {
        Quaternion ra[wideLanes], rb[wideLanes];
        Vector3 pa[wideLanes], pb[wideLanes], sa[wideLanes], sb[wideLanes];
        for (int lane = 0; lane < wideLanes; ++lane)
        {
            const uint32_t j = b * wideLanes + lane;
            const int32_t track = binding != nullptr ? binding->trackOf[j] : -1;
            if (track < 0)
            {
                ra[lane] = rb[lane] = skeleton.restRotations[j];
                pa[lane] = pb[lane] = skeleton.restPositions[j];
                sa[lane] = sb[lane] = skeleton.restScales[j];
                continue;
            }
            const Clip& clip = *binding->clip;
            const assets::ClipTrack& entry = clip.tracks[track];
            ra[lane] = trackRotation(clip, entry, i0);
            rb[lane] = trackRotation(clip, entry, i1);
            pa[lane] = trackTranslation(clip, entry, i0);
            pb[lane] = trackTranslation(clip, entry, i1);
            sa[lane] = trackScale(clip, entry, i0);
            sb[lane] = trackScale(clip, entry, i1);
        }
        rotations[b] = nlerp(QuaternionWide::load(ra), QuaternionWide::load(rb), t);
        positions[b] = lerpWide(Vector3Wide::load(pa), Vector3Wide::load(pb), t);
        scales[b] = lerpWide(Vector3Wide::load(sa), Vector3Wide::load(sb), t);
    }
}

// Blends `from` into the target set by t (0 keeps the target).
void blendInto(const Pose& pose, QuaternionWide* rotations, Vector3Wide* positions, Vector3Wide* scales,
               const QuaternionWide* fromRotations, const Vector3Wide* fromPositions, const Vector3Wide* fromScales,
               float t)
{
    const FloatWide share = FloatWide::splat(t);
    for (uint32_t b = 0; b < pose.blocks; ++b)
    {
        rotations[b] = nlerp(rotations[b], fromRotations[b], share);
        positions[b] = lerpWide(positions[b], fromPositions[b], share);
        scales[b] = lerpWide(scales[b], fromScales[b], share);
    }
}

// A player into the target set, crossfaded from its previous clip through
// the fade set.
void samplePlayer(Pose& pose, const Player& player, QuaternionWide* rotations, Vector3Wide* positions,
                  Vector3Wide* scales)
{
    const Skeleton& skeleton = *pose.skeleton;
    sample(skeleton, player.binding, player.time, rotations, positions, scales);
    if (player.fadeDuration <= 0 || player.previousBinding == nullptr)
    {
        return;
    }
    sample(skeleton, player.previousBinding, player.previousTime, pose.fadeRotations.data(), pose.fadePositions.data(),
           pose.fadeScales.data());
    const FloatWide t = FloatWide::splat(std::clamp(player.fadeElapsed / player.fadeDuration, 0.0f, 1.0f));
    for (uint32_t b = 0; b < skeleton.blocks; ++b)
    {
        rotations[b] = nlerp(pose.fadeRotations[b], rotations[b], t);
        positions[b] = lerpWide(pose.fadePositions[b], positions[b], t);
        scales[b] = lerpWide(pose.fadeScales[b], scales[b], t);
    }
}

// A state's clips into the target set at their weights, each after the
// first sampled into the scratch set and blended in by its share so far.
void sampleState(Pose& pose, const StatePlay& state, QuaternionWide* rotations, Vector3Wide* positions,
                 Vector3Wide* scales)
{
    const Skeleton& skeleton = *pose.skeleton;
    if (state.count == 0)
    {
        sample(skeleton, nullptr, 0, rotations, positions, scales);
        return;
    }
    sample(skeleton, state.bindings[0], state.phase * state.durations[0], rotations, positions, scales);
    float sum = state.weights[0];
    for (uint32_t i = 1; i < state.count; ++i)
    {
        if (state.weights[i] <= 0)
        {
            continue;
        }
        sample(skeleton, state.bindings[i], state.phase * state.durations[i], pose.scratchRotations.data(),
               pose.scratchPositions.data(), pose.scratchScales.data());
        sum += state.weights[i];
        blendInto(pose, rotations, positions, scales, pose.scratchRotations.data(), pose.scratchPositions.data(),
                  pose.scratchScales.data(), state.weights[i] / sum);
    }
}

// The base into the pose: its clip, or its state machine's state, each
// crossfaded from what it's leaving.
void sampleBase(Pose& pose)
{
    if (!playsMachine(pose))
    {
        samplePlayer(pose, pose.base, pose.rotations.data(), pose.positions.data(), pose.scales.data());
        return;
    }
    const MachinePlay& play = pose.machine;
    sampleState(pose, play.current, pose.rotations.data(), pose.positions.data(), pose.scales.data());
    if (play.fadeDuration <= 0 || play.previous.state < 0)
    {
        return;
    }
    sampleState(pose, play.previous, pose.fadeRotations.data(), pose.fadePositions.data(), pose.fadeScales.data());
    blendInto(pose, pose.rotations.data(), pose.positions.data(), pose.scales.data(), pose.fadeRotations.data(),
              pose.fadePositions.data(), pose.fadeScales.data(),
              1.0f - std::clamp(play.fadeElapsed / play.fadeDuration, 0.0f, 1.0f));
}

void applyLayer(Pose& pose, const Layer& layer)
{
    const Skeleton& skeleton = *pose.skeleton;
    samplePlayer(pose, layer.player, pose.scratchRotations.data(), pose.scratchPositions.data(),
                 pose.scratchScales.data());
    const Binding* binding = layer.player.binding;
    for (uint32_t b = 0; b < skeleton.blocks; ++b)
    {
        FloatWide w = FloatWide::splat(layer.weight);
        if (layer.masked)
        {
            w = w * FloatWide::load(layer.jointWeights.data() + size_t{b} * wideLanes);
        }
        if (layer.desc.blend == LayerBlend::Override)
        {
            pose.rotations[b] = nlerp(pose.rotations[b], pose.scratchRotations[b], w);
            pose.positions[b] = lerpWide(pose.positions[b], pose.scratchPositions[b], w);
            pose.scales[b] = lerpWide(pose.scales[b], pose.scratchScales[b], w);
            continue;
        }
        if (binding == nullptr)
        {
            continue;
        }
        // Additive: the difference from the clip's first sample, scaled by
        // the weight, on top of the pose.
        const size_t first = size_t{b} * wideLanes;
        const QuaternionWide reference = QuaternionWide::load(binding->firstRotations.data() + first);
        const QuaternionWide delta = pose.scratchRotations[b] * conjugate(reference);
        pose.rotations[b] =
            normalize(nlerp(QuaternionWide::splat(Quaternion::identity()), delta, w) * pose.rotations[b]);
        const Vector3Wide positionReference = Vector3Wide::load(binding->firstPositions.data() + first);
        pose.positions[b] = pose.positions[b] + (pose.scratchPositions[b] - positionReference) * w;
        const Vector3Wide scaleReference = Vector3Wide::load(binding->firstScales.data() + first);
        const Vector3Wide one = Vector3Wide::splat({1, 1, 1});
        const Vector3Wide ratio = {pose.scratchScales[b].x / scaleReference.x,
                                   pose.scratchScales[b].y / scaleReference.y,
                                   pose.scratchScales[b].z / scaleReference.z};
        pose.scales[b] = multiplyWide(pose.scales[b], lerpWide(one, ratio, w));
    }
}

// Local to model space, parents first: model = local * offset * parent's.
void hierarchy(Pose& pose)
{
    const Skeleton& skeleton = *pose.skeleton;
    Quaternion rotations[wideLanes];
    Vector3 positions[wideLanes], scales[wideLanes];
    for (uint32_t b = 0; b < skeleton.blocks; ++b)
    {
        pose.rotations[b].store(rotations);
        pose.positions[b].store(positions);
        pose.scales[b].store(scales);
        for (int lane = 0; lane < wideLanes; ++lane)
        {
            const uint32_t j = b * wideLanes + lane;
            if (j >= skeleton.jointCount)
            {
                break;
            }
            Matrix4x4 m = trsMatrix(positions[lane], rotations[lane], scales[lane]);
            if (skeleton.hasOffset[j] != 0)
            {
                m = m * skeleton.offsets[j];
            }
            const int32_t parent = skeleton.parents[j];
            pose.model[j] = parent >= 0 ? m * pose.model[parent] : m;
        }
    }
}

// --- What clips do in a frame: events, windows, root motion ----------------

// A clip's part in the base or a layer for one update: where it was, how far
// the update plays it, and its weight.
struct Contribution
{
    AssetId clip{};
    const Binding* binding = nullptr;
    float time = 0;   // before the update
    float amount = 0; // clip seconds the update plays it (never backward)
    float duration = 0;
    float weight = 0;
    float rate = 0; // clip seconds per second
    bool loop = false;
    bool fresh = false;
};

constexpr uint32_t maxContributions = 2 * maxStateClips;

float fadeShare(float elapsed, float duration)
{
    return duration > 0 ? std::clamp(elapsed / duration, 0.0f, 1.0f) : 1.0f;
}

// Durations come from the bindings: these run in the pose jobs too, where
// the caches mustn't be touched.
float durationOf(const Binding* binding)
{
    return binding != nullptr ? binding->clip->info.duration : 0.0f;
}

uint32_t playerContributions(const Player& player, float delta, float speed, bool loop, float weight, Contribution* out)
{
    uint32_t count = 0;
    const float t =
        player.fadeDuration > 0 && !isNone(player.previous) ? fadeShare(player.fadeElapsed, player.fadeDuration) : 1.0f;
    const float amount = std::max(delta * speed, 0.0f);
    if (!isNone(player.clip))
    {
        out[count++] = {player.clip, player.binding, player.time, amount,      durationOf(player.binding),
                        weight * t,  speed,          loop,        player.fresh};
    }
    if (t < 1)
    {
        out[count++] = {player.previous,
                        player.previousBinding,
                        player.previousTime,
                        amount,
                        durationOf(player.previousBinding),
                        weight * (1 - t),
                        speed,
                        loop,
                        false};
    }
    return count;
}

uint32_t stateContributions(const MachinePlay& play, const StatePlay& state, float delta, float speed, float weight,
                            Contribution* out)
{
    if (state.state < 0 || state.duration <= 0)
    {
        return 0;
    }
    const assets::StateEntry& entry = play.machine->states[static_cast<uint32_t>(state.state)];
    const float phaseRate = entry.speed * speed / state.duration;
    for (uint32_t i = 0; i < state.count; ++i)
    {
        const float rate = phaseRate * state.durations[i];
        out[i] = {state.clips[i],
                  state.bindings[i],
                  state.phase * state.durations[i],
                  std::max(delta * rate, 0.0f),
                  state.durations[i],
                  weight * state.weights[i],
                  rate,
                  entry.loop != 0,
                  state.fresh};
    }
    return state.count;
}

uint32_t baseContributions(const Pose& pose, float delta, Contribution* out)
{
    if (!playsMachine(pose))
    {
        return playerContributions(pose.base, delta, pose.speed, pose.loop, 1.0f, out);
    }
    const MachinePlay& play = pose.machine;
    const float t = play.previous.state >= 0 ? fadeShare(play.fadeElapsed, play.fadeDuration) : 1.0f;
    uint32_t count = stateContributions(play, play.current, delta, pose.speed, t, out);
    if (t < 1)
    {
        count += stateContributions(play, play.previous, delta, pose.speed, 1 - t, out + count);
    }
    return count;
}

float timeAfter(const Contribution& c)
{
    if (c.duration <= 0)
    {
        return 0;
    }
    return c.loop ? std::fmod(c.time + c.amount, c.duration) : std::min(c.time + c.amount, c.duration);
}

uint32_t wrapsOf(const Contribution& c)
{
    return c.loop && c.duration > 0 ? static_cast<uint32_t>((c.time + c.amount) / c.duration) : 0;
}

// Whether the update passes `time` on the clip, and how far through the
// update it does (0 to 1), the first time. A clip that just started passes
// its start too.
bool passes(const Contribution& c, float time, float& share)
{
    if (c.amount <= 0 || c.duration <= 0)
    {
        return false;
    }
    float at = time;
    if (c.loop)
    {
        while (c.fresh ? at < c.time : at <= c.time)
        {
            at += c.duration;
        }
    }
    else if (c.fresh ? at < c.time : at <= c.time)
    {
        return false;
    }
    if (at > c.time + c.amount)
    {
        return false;
    }
    share = (at - c.time) / c.amount;
    return true;
}

// The clip with the most weight, or -1.
int32_t dominant(const Contribution* contributions, uint32_t count)
{
    int32_t best = -1;
    for (uint32_t i = 0; i < count; ++i)
    {
        if (contributions[i].weight > 0 && contributions[i].binding != nullptr &&
            (best < 0 || contributions[i].weight > contributions[best].weight))
        {
            best = static_cast<int32_t>(i);
        }
    }
    return best;
}

// Times within the update: it covers the last frame's start to this one's.
struct FrameClock
{
    uint64_t start;
    float delta;

    uint64_t at(float share) const
    {
        const double back = (1.0 - share) * static_cast<double>(delta) * 1e9;
        return back >= static_cast<double>(start) ? 0 : start - static_cast<uint64_t>(back);
    }
};

void record(const ecs::World& world, const Pose& pose, AnimationEventKind kind, uint32_t name, float clipTime,
            uint64_t timestamp)
{
    recorded.pushBack({&world, {pose.entity, kind, name, clipTime, timestamp}});
}

// One source's events and windows (0 the base, 1 + a layer), from the clip
// with the most weight in it.
void passEvents(const ecs::World& world, Pose& pose, uint32_t source, const Contribution* contributions, uint32_t count,
                const FrameClock& clock)
{
    for (WindowState& window : pose.windows)
    {
        if (window.source == source)
        {
            window.seen = false;
        }
    }
    const int32_t best = dominant(contributions, count);
    const Contribution* one = best >= 0 ? &contributions[best] : nullptr;
    const Clip* clip = one != nullptr ? one->binding->clip : nullptr;
    if (clip != nullptr)
    {
        float share = 0;
        for (const assets::ClipEvent& event : clip->events)
        {
            if (passes(*one, event.time, share))
            {
                record(world, pose, AnimationEventKind::Event, event.name, event.time, clock.at(share));
            }
        }
        const float now = timeAfter(*one);
        const bool atEnd = !one->loop && now >= one->duration;
        for (uint32_t w = 0; w < clip->windows.size(); ++w)
        {
            const assets::ClipWindow& window = clip->windows[w];
            if (!(window.start <= now && (now < window.end || (atEnd && window.end >= one->duration))))
            {
                continue;
            }
            const uint64_t closes =
                one->rate > 0 ? clock.start + static_cast<uint64_t>(std::max(window.end - now, 0.0f) / one->rate * 1e9)
                              : UINT64_MAX;
            WindowState* open = nullptr;
            for (WindowState& state : pose.windows)
            {
                if (state.source == source && state.window == w && state.clip == one->clip)
                {
                    open = &state;
                }
            }
            if (open != nullptr)
            {
                open->seen = true;
                open->open.closes = closes;
                continue;
            }
            const uint64_t opened = passes(*one, window.start, share) ? clock.at(share) : clock.start;
            pose.windows.pushBack({one->clip, source, w, window.end, {window.name, opened, closes}, true});
            record(world, pose, AnimationEventKind::WindowOpen, window.name, window.start, opened);
        }
    }
    // What's no longer open closed: when its clip passed its end, or now.
    for (size_t i = pose.windows.size(); i-- > 0;)
    {
        const WindowState& state = pose.windows[i];
        if (state.source != source || state.seen)
        {
            continue;
        }
        float share = 0;
        const bool passed = one != nullptr && state.clip == one->clip && passes(*one, state.end, share);
        record(world, pose, AnimationEventKind::WindowClose, state.open.name, state.end,
               passed ? clock.at(share) : clock.start);
        pose.windows.removeSwap(i);
    }
}

// The root's travel from its clip's start to a time: along the ground, and
// turned about up.
struct Travel
{
    Vector3 translation{};
    float yaw = 0;
};

Travel travel(const Binding& binding, const Skeleton& skeleton, float time)
{
    const RootSample at = rootAt(binding, skeleton, time);
    const Vector3 moved = at.position - binding.first.position;
    return {{moved.x, 0, moved.z}, wrapAngle(at.yaw - binding.first.yaw)};
}

// What one clip's root did over the update, in the character's frame: the
// character turned with the root, so the root's travel is turned back by
// how far it had turned before.
Travel frameTravel(const Contribution& c, const Skeleton& skeleton)
{
    const Binding& binding = *c.binding;
    const Travel before = travel(binding, skeleton, c.time);
    const Travel after = travel(binding, skeleton, timeAfter(c));
    const float wraps = static_cast<float>(wrapsOf(c));
    const Vector3 lap = binding.last.position - binding.first.position;
    const Vector3 moved = after.translation - before.translation + Vector3{lap.x, 0, lap.z} * wraps;
    return {rotate(moved, Quaternion::fromAxisAngle({0, 1, 0}, -before.yaw)),
            wrapAngle(after.yaw - before.yaw) + wrapAngle(binding.last.yaw - binding.first.yaw) * wraps};
}

void applyRootMotion(ecs::World& world, Pose& pose, const Contribution* contributions, uint32_t count, float delta)
{
    Vector3 moved{};
    float yaw = 0;
    for (uint32_t i = 0; i < count; ++i)
    {
        const Contribution& c = contributions[i];
        if (c.binding != nullptr && c.binding->root >= 0 && c.weight > 0)
        {
            const Travel step = frameTravel(c, *pose.skeleton);
            moved = moved + step.translation * c.weight;
            yaw += step.yaw * c.weight;
        }
    }
    scenes::LocalTransform* local = ecs::get<scenes::LocalTransform>(world, pose.entity);
    if (local == nullptr)
    {
        return;
    }
    const Quaternion turn = Quaternion::fromAxisAngle({0, 1, 0}, yaw);
    const Vector3 displacement = rotate(moved * local->scale, local->rotation);
    (void)physics::moveCharacter(world, pose.entity, displacement, delta);
    local = ecs::get<scenes::LocalTransform>(world, pose.entity);
    local->rotation = normalize(concatenate(turn, local->rotation));
    pose.moved = {displacement, turn};
}

// The base's travel so far, taken out of the posed joints: the entity moved
// by it instead (applyRootMotion), so the root stays over the entity where
// it was at its clip's start, facing as it did.
void removeRootMotion(Pose& pose)
{
    Contribution contributions[maxContributions];
    const uint32_t count = baseContributions(pose, 0, contributions);
    Vector3 moved{}, pivot{};
    float yaw = 0, total = 0;
    for (uint32_t i = 0; i < count; ++i)
    {
        const Contribution& c = contributions[i];
        if (c.binding != nullptr && c.binding->root >= 0 && c.weight > 0)
        {
            const Travel so = travel(*c.binding, *pose.skeleton, c.time);
            moved = moved + so.translation * c.weight;
            pivot = pivot + c.binding->first.position * c.weight;
            yaw += so.yaw * c.weight;
            total += c.weight;
        }
    }
    if (total <= 0)
    {
        return;
    }
    pivot.y = 0;
    const Matrix4x4 back = Matrix4x4::translation(-moved) * Matrix4x4::translation(-pivot) *
                           Matrix4x4::rotation(Quaternion::fromAxisAngle({0, 1, 0}, -yaw)) *
                           Matrix4x4::translation(pivot);
    for (Matrix4x4& joint : pose.model)
    {
        joint = joint * back;
    }
}

// --- IK ---------------------------------------------------------------------

Vector3 positionOf(const Matrix4x4& m)
{
    return {m.rows[3].x, m.rows[3].y, m.rows[3].z};
}

// The shortest turn taking one direction to another (both unit length).
Quaternion turnBetween(Vector3 from, Vector3 to)
{
    const float d = dot(from, to);
    if (d > 0.999999f)
    {
        return Quaternion::identity();
    }
    if (d < -0.999999f)
    {
        const Vector3 side = cross(from, std::fabs(from.y) < 0.9f ? Vector3{0, 1, 0} : Vector3{1, 0, 0});
        return Quaternion::fromAxisAngle(normalize(side), pi);
    }
    const Vector3 axis = cross(from, to);
    return normalize(Quaternion{axis.x, axis.y, axis.z, 1 + d});
}

// Turns a joint and everything below it about a point, in model space.
void turnSubtree(Pose& pose, uint32_t joint, Vector3 pivot, Quaternion turn)
{
    const Matrix4x4 about = Matrix4x4::translation(-pivot) * Matrix4x4::rotation(turn) * Matrix4x4::translation(pivot);
    const Skeleton& skeleton = *pose.skeleton;
    pose.marks[joint] = 1;
    pose.model[joint] = pose.model[joint] * about;
    for (uint32_t j = joint + 1; j < skeleton.jointCount; ++j)
    {
        const int32_t parent = skeleton.parents[j];
        pose.marks[j] = parent >= 0 && pose.marks[parent] != 0 ? 1 : 0;
        if (pose.marks[j] != 0)
        {
            pose.model[j] = pose.model[j] * about;
        }
    }
    std::fill(pose.marks.begin() + joint, pose.marks.end(), uint8_t{0});
}

// Two-bone IK by the law of cosines: with the chain's two lengths and the
// distance to the target, the triangle's angle at the root says where the
// middle joint goes; the pole (or the current bend) says which way.
void solveTwoBone(Pose& pose, const TwoBoneIkDesc& desc, float weight)
{
    const Skeleton& skeleton = *pose.skeleton;
    if (desc.joint < 0 || static_cast<uint32_t>(desc.joint) >= skeleton.jointCount)
    {
        return;
    }
    const uint32_t c = static_cast<uint32_t>(desc.joint);
    const int32_t middleJoint = skeleton.parents[c];
    const int32_t rootJoint = middleJoint >= 0 ? skeleton.parents[middleJoint] : -1;
    if (rootJoint < 0)
    {
        return;
    }
    const uint32_t b = static_cast<uint32_t>(middleJoint), a = static_cast<uint32_t>(rootJoint);
    const Vector3 rootAtPos = positionOf(pose.model[a]);
    const Vector3 middleAt = positionOf(pose.model[b]);
    const Vector3 endAt = positionOf(pose.model[c]);
    const Vector3 goal = endAt + (transformPoint(desc.target, pose.toModel) - endAt) * weight;
    const float upper = length(middleAt - rootAtPos);
    const float lower = length(endAt - middleAt);
    const Vector3 toGoal = goal - rootAtPos;
    const float reach = length(toGoal);
    if (upper < 1e-5f || lower < 1e-5f || reach < 1e-5f)
    {
        return;
    }
    const Vector3 along = toGoal * (1.0f / reach);
    const float distance = std::clamp(reach, std::fabs(upper - lower) + 1e-4f, std::max(upper + lower - 1e-4f, 1e-4f));
    const Vector3 hint = desc.usePole ? transformPoint(desc.pole, pose.toModel) - rootAtPos : middleAt - rootAtPos;
    Vector3 bend = hint - along * dot(hint, along);
    if (lengthSquared(bend) < 1e-10f)
    {
        const Vector3 current = middleAt - rootAtPos;
        bend = current - along * dot(current, along);
    }
    if (lengthSquared(bend) < 1e-10f)
    {
        bend = cross(along, std::fabs(along.y) < 0.9f ? Vector3{0, 1, 0} : Vector3{1, 0, 0});
    }
    bend = normalize(bend);
    const float cosine =
        std::clamp((upper * upper + distance * distance - lower * lower) / (2 * upper * distance), -1.0f, 1.0f);
    const float sine = std::sqrt(1 - cosine * cosine);
    const Vector3 middle = rootAtPos + along * (upper * cosine) + bend * (upper * sine);
    const Vector3 end = rootAtPos + along * distance;
    turnSubtree(pose, a, rootAtPos, turnBetween(normalize(middleAt - rootAtPos), normalize(middle - rootAtPos)));
    const Vector3 endNow = positionOf(pose.model[c]);
    turnSubtree(pose, b, middle, turnBetween(normalize(endNow - middle), normalize(end - middle)));
}

void solveLookAt(Pose& pose, const LookAtDesc& desc, float weight)
{
    if (desc.joint < 0 || static_cast<uint32_t>(desc.joint) >= pose.skeleton->jointCount)
    {
        return;
    }
    const uint32_t joint = static_cast<uint32_t>(desc.joint);
    const Matrix4x4& m = pose.model[joint];
    const Vector3 at = positionOf(m);
    const Vector3 aim = transformDirection(desc.aim, m);
    const Vector3 wanted = transformPoint(desc.target, pose.toModel) - at;
    if (lengthSquared(aim) < 1e-12f || lengthSquared(wanted) < 1e-12f)
    {
        return;
    }
    const Vector3 from = normalize(aim), to = normalize(wanted);
    const float angle = std::acos(std::clamp(dot(from, to), -1.0f, 1.0f));
    if (angle < 1e-5f)
    {
        return;
    }
    Vector3 axis = cross(from, to);
    if (lengthSquared(axis) < 1e-12f)
    {
        axis = cross(from, std::fabs(from.y) < 0.9f ? Vector3{0, 1, 0} : Vector3{1, 0, 0});
    }
    const float turn = std::min(angle, std::max(desc.maxAngle, 0.0f)) * weight;
    turnSubtree(pose, joint, at, Quaternion::fromAxisAngle(normalize(axis), turn));
}

void evaluate(Pose& pose)
{
    if (pose.skeleton == nullptr)
    {
        return;
    }
    sampleBase(pose);
    for (const Layer& layer : pose.layers)
    {
        if (layer.active && layer.weight > 0)
        {
            applyLayer(pose, layer);
        }
    }
    hierarchy(pose);
    if (pose.rootMotion)
    {
        removeRootMotion(pose);
    }
    for (const IkSlot& slot : pose.ik)
    {
        if (slot.weight <= 0)
        {
            continue;
        }
        if (slot.kind == IkKind::TwoBone)
        {
            solveTwoBone(pose, slot.twoBone, slot.weight);
        }
        else if (slot.kind == IkKind::LookAt)
        {
            solveLookAt(pose, slot.lookAt, slot.weight);
        }
    }
    pose.posed = true;
}

void resolve(Player& player, const Pose& pose)
{
    player.binding = bindingOf(player.clip, pose.skeletonId, *pose.skeleton);
    player.previousBinding =
        player.fadeDuration > 0 ? bindingOf(player.previous, pose.skeletonId, *pose.skeleton) : nullptr;
}

// A state's clips and weights at the current parameters.
void refreshState(Pose& pose, StatePlay& state)
{
    const MachinePlay& play = pose.machine;
    state.count = 0;
    state.duration = 0;
    if (play.machine == nullptr || state.state < 0)
    {
        return;
    }
    ClipWeight weights[maxStateClips];
    state.count = stateClips(*play.machine, static_cast<uint32_t>(state.state), play.parameters.span(), weights);
    for (uint32_t i = 0; i < state.count; ++i)
    {
        const AssetId& clip = play.machine->motions[weights[i].motion].clip;
        state.clips[i] = clip;
        state.weights[i] = weights[i].weight;
        state.durations[i] = clipDuration(clip);
        state.bindings[i] = bindingOf(clip, pose.skeletonId, *pose.skeleton);
        state.duration += weights[i].weight * state.durations[i];
    }
}

// Everything the jobs read, found on the main thread.
void resolveAll(Pose& pose)
{
    resolve(pose.base, pose);
    refreshState(pose, pose.machine.current);
    refreshState(pose, pose.machine.previous);
    for (Layer& layer : pose.layers)
    {
        if (layer.active)
        {
            resolve(layer.player, pose);
            if (layer.masked != !layer.mask.empty() || (layer.masked && layer.jointWeights.empty()))
            {
                buildMask(layer, *pose.skeleton);
            }
        }
    }
}

// Every Animator of the world, its pose made and its skeleton read.
void gather(ecs::World& world, Vector<Pose*>& out)
{
    struct Found
    {
        Vector<ecs::Entity>* entities;
    };
    Vector<ecs::Entity> entities(tag);
    Found found{&entities};
    const ecs::ComponentId required[] = {ecs::componentIdOf<Animator>};
    ecs::Query* query = ecs::createQuery(world, {.required = required});
    ecs::forEachChunk(
        *query,
        [](const ecs::ChunkView& view, void* data) {
            const ecs::Entity* chunk = ecs::entitiesOf(view);
            const size_t count = ecs::readColumn<Animator>(view).size();
            for (size_t i = 0; i < count; ++i)
            {
                static_cast<Found*>(data)->entities->pushBack(chunk[i]);
            }
        },
        &found);
    ecs::destroyQuery(query);
    // Poses whose entity is gone, or lost its Animator, are freed.
    for (uint32_t i = 0; i < pool.size(); ++i)
    {
        Pose* pose = pool[i];
        if (pose != nullptr && pose->world == &world &&
            (!ecs::isAlive(world, pose->entity) ||
             !ecs::hasComponent(world, pose->entity, ecs::componentIdOf<Animator>)))
        {
            freePose(i);
        }
    }
    out.clear();
    for (const ecs::Entity entity : entities)
    {
        if (Pose* pose = ensurePose(world, entity))
        {
            sync(*pose, *ecs::read<Animator>(world, entity));
            out.pushBack(pose);
        }
    }
}

void posePoses(ecs::World& world, ecs::CommandBuffer* commands)
{
    Vector<Pose*> poses(tag);
    gather(world, poses);
    for (Pose* pose : poses)
    {
        if (pose->skeleton == nullptr)
        {
            continue;
        }
        resolveAll(*pose);
        bool aims = false;
        for (const IkSlot& slot : pose->ik)
        {
            aims = aims || (slot.kind != IkKind::None && slot.weight > 0);
        }
        if (aims)
        {
            // Where the entity is now, root motion included; propagation
            // comes later in the frame.
            if (!invert(scenes::computeWorldTransform(world, pose->entity), pose->toModel))
            {
                pose->toModel = Matrix4x4::identity();
            }
        }
        // The handle, for skinning to find the pose by index.
        const PoseHandle handle{indexOf(pose) + 1};
        const PoseHandle* current = ecs::read<PoseHandle>(world, pose->entity);
        if (current == nullptr || current->pose != handle.pose)
        {
            if (commands != nullptr)
            {
                ecs::recordSet(*commands, pose->entity, handle);
            }
            else
            {
                ecs::addComponent(world, pose->entity, ecs::componentIdOf<PoseHandle>);
                *ecs::get<PoseHandle>(world, pose->entity) = handle;
            }
        }
    }
    // One job per character (ADR 0026).
    jobs::parallelFor(
        static_cast<uint32_t>(poses.size()), 1,
        [](void* data, uint32_t begin, uint32_t end) {
            Pose** list = static_cast<Pose**>(data);
            for (uint32_t i = begin; i < end; ++i)
            {
                evaluate(*list[i]);
            }
        },
        poses.data());
}

// A weight moving toward its goal at its rate (all at once without one).
float fadeToward(float weight, float goal, float rate, float delta)
{
    if (rate <= 0)
    {
        return goal;
    }
    return weight < goal ? std::min(weight + rate * delta, goal) : std::max(weight - rate * delta, goal);
}

void advanceBase(Pose& pose, float delta)
{
    if (!playsMachine(pose))
    {
        advancePlayer(pose.base, delta, pose.speed, pose.loop);
        return;
    }
    MachinePlay& play = pose.machine;
    const StateProgress progress = advanceState(play, play.current, delta, pose.speed);
    if (play.fadeDuration > 0 && play.previous.state >= 0)
    {
        (void)advanceState(play, play.previous, delta, pose.speed);
        play.fadeElapsed += delta;
        if (play.fadeElapsed >= play.fadeDuration)
        {
            play.fadeDuration = 0;
            play.previous = StatePlay{};
        }
    }
    const int32_t taken =
        chooseTransition(*play.machine, static_cast<uint32_t>(play.current.state), play.parameters.span(), progress);
    if (taken >= 0)
    {
        consumeTriggers(*play.machine, static_cast<uint32_t>(taken), play.parameters.span());
        const assets::StateTransition& transition = play.machine->transitions[static_cast<uint32_t>(taken)];
        play.enter(transition.to, transition.blendSeconds);
        refreshState(pose, play.current);
    }
}

void advance(ecs::World& world, float delta, uint64_t frameStart)
{
    forgetEvents(world);
    const size_t firstEvent = recorded.size();
    const FrameClock clock{frameStart, delta};
    Vector<Pose*> poses(tag);
    gather(world, poses);
    for (Pose* pose : poses)
    {
        if (pose->skeleton == nullptr)
        {
            continue;
        }
        resolveAll(*pose);
        // Where every clip was and how far it plays: events, windows, and
        // root motion come from these, before anything moves.
        Contribution base[maxContributions];
        const uint32_t baseCount = baseContributions(*pose, delta, base);
        passEvents(world, *pose, 0, base, baseCount, clock);
        for (uint32_t l = 0; l < maxLayers; ++l)
        {
            const Layer& layer = pose->layers[l];
            Contribution played[2];
            const uint32_t count =
                layer.active && layer.weight > 0
                    ? playerContributions(layer.player, delta, layer.desc.speed, layer.desc.loop, layer.weight, played)
                    : 0;
            passEvents(world, *pose, 1 + l, played, count, clock);
        }
        pose->moved = {{0, 0, 0}, Quaternion::identity()};
        if (pose->rootMotion)
        {
            applyRootMotion(world, *pose, base, baseCount, delta);
        }

        advanceBase(*pose, delta);
        for (Layer& layer : pose->layers)
        {
            if (!layer.active)
            {
                continue;
            }
            advancePlayer(layer.player, delta, layer.desc.speed, layer.desc.loop);
            layer.weight = fadeToward(layer.weight, layer.clearing ? 0.0f : layer.desc.weight, layer.fadeRate, delta);
            if (layer.clearing && layer.weight <= 0)
            {
                layer = Layer{};
            }
        }
        for (IkSlot& slot : pose->ik)
        {
            if (slot.kind == IkKind::None)
            {
                continue;
            }
            slot.weight = fadeToward(slot.weight, slot.clearing ? 0.0f : slot.goal, slot.fadeRate, delta);
            if (slot.clearing && slot.weight <= 0)
            {
                slot = IkSlot{};
            }
        }
    }
    std::sort(recorded.begin() + firstEvent, recorded.end(), [](const Recorded& a, const Recorded& b) {
        if (a.event.timestamp != b.event.timestamp)
        {
            return a.event.timestamp < b.event.timestamp;
        }
        if (a.event.entity.value != b.event.entity.value)
        {
            return a.event.entity.value < b.event.entity.value;
        }
        return a.event.kind < b.event.kind;
    });
}

void updateSystem(const loop::SystemContext& context, void*)
{
    if (context.world != nullptr)
    {
        advance(*context.world, static_cast<float>(context.deltaSeconds), context.frameStartNs);
    }
}

void poseSystem(const loop::SystemContext& context, void*)
{
    if (context.world != nullptr)
    {
        posePoses(*context.world, context.commands);
    }
}

// The entity's pose, following its Animator; null without one.
Pose* currentPose(ecs::World& world, ecs::Entity entity)
{
    Pose* pose = ensurePose(world, entity);
    if (pose != nullptr)
    {
        sync(*pose, *ecs::read<Animator>(world, entity));
    }
    return pose;
}

Result<void> setParameter(ecs::World& world, ecs::Entity entity, std::string_view name, assets::ParameterType type,
                          float value)
{
    Pose* pose = currentPose(world, entity);
    const StateMachine* machine = pose != nullptr ? pose->machine.machine : nullptr;
    const int32_t index = machine != nullptr ? machine->findParameter(name) : -1;
    if (index < 0 || machine->parameters[static_cast<uint32_t>(index)].type != type)
    {
        return Status::NotFound;
    }
    pose->machine.parameters[static_cast<uint32_t>(index)] = value;
    return {};
}

// A slot taking a kind of IK and a weight: a new kind starts its fade from
// 0; a new weight fades from where the slot is.
void aimSlot(IkSlot& slot, IkKind kind, float weight, float fadeSeconds)
{
    weight = std::clamp(weight, 0.0f, 1.0f);
    fadeSeconds = std::max(fadeSeconds, 0.0f);
    if (slot.kind != kind)
    {
        slot = IkSlot{};
        slot.kind = kind;
        slot.weight = fadeSeconds > 0 ? 0.0f : weight;
        slot.goal = weight;
        slot.fadeRate = fadeSeconds > 0 ? weight / fadeSeconds : 0.0f;
        return;
    }
    if (weight != slot.goal || slot.clearing)
    {
        slot.goal = weight;
        slot.fadeRate = fadeSeconds > 0 ? std::fabs(weight - slot.weight) / fadeSeconds : 0.0f;
        if (slot.fadeRate <= 0)
        {
            slot.weight = weight;
        }
    }
    slot.clearing = false;
}

// The entity's pose with a skeleton that has the joint, and (for two-bone
// IK) its parent and theirs.
Pose* poseWithJoint(ecs::World& world, ecs::Entity entity, uint32_t slot, int32_t joint, uint32_t ancestors)
{
    if (slot >= maxIkSlots)
    {
        return nullptr;
    }
    Pose* pose = currentPose(world, entity);
    if (pose == nullptr || pose->skeleton == nullptr || joint < 0 ||
        static_cast<uint32_t>(joint) >= pose->skeleton->jointCount)
    {
        return nullptr;
    }
    int32_t up = joint;
    for (uint32_t i = 0; i < ancestors; ++i)
    {
        up = pose->skeleton->parents[static_cast<uint32_t>(up)];
        if (up < 0)
        {
            return nullptr;
        }
    }
    return pose;
}

} // namespace

Result<void> play(ecs::World& world, ecs::Entity entity, AssetId clip, float fadeSeconds)
{
    Pose* pose = currentPose(world, entity);
    if (pose == nullptr || pose->skeleton == nullptr || (!isNone(clip) && clipOf(clip) == nullptr))
    {
        return Status::NotFound;
    }
    if (playsMachine(*pose))
    {
        return Status::Unsupported;
    }
    pose->base.start(clip, std::max(fadeSeconds, 0.0f));
    ecs::get<Animator>(world, entity)->clip = clip;
    return {};
}

Result<void> setLayer(ecs::World& world, ecs::Entity entity, uint32_t index, LayerDesc desc,
                      std::span<const MaskEntry> mask)
{
    if (index >= maxLayers)
    {
        return Status::NotFound;
    }
    Pose* pose = ensurePose(world, entity);
    if (pose == nullptr || pose->skeleton == nullptr || clipOf(desc.clip) == nullptr)
    {
        return Status::NotFound;
    }
    Layer& layer = pose->layers[index];
    desc.weight = std::clamp(desc.weight, 0.0f, 1.0f);
    desc.fadeSeconds = std::max(desc.fadeSeconds, 0.0f);
    if (!layer.active)
    {
        layer.player = Player{};
        layer.player.clip = desc.clip;
        layer.weight = desc.fadeSeconds > 0 ? 0.0f : desc.weight;
    }
    else if (!(layer.player.clip == desc.clip))
    {
        layer.player.start(desc.clip, desc.fadeSeconds);
    }
    layer.active = true;
    layer.clearing = false;
    layer.desc = desc;
    layer.fadeRate = desc.fadeSeconds > 0 ? std::fabs(desc.weight - layer.weight) / desc.fadeSeconds : 0.0f;
    layer.mask.assign(mask);
    buildMask(layer, *pose->skeleton);
    return {};
}

Result<void> clearLayer(ecs::World& world, ecs::Entity entity, uint32_t index, float fadeSeconds)
{
    if (index >= maxLayers)
    {
        return Status::NotFound;
    }
    Pose* pose = findPose(world, entity);
    if (pose == nullptr)
    {
        return Status::NotFound;
    }
    Layer& layer = pose->layers[index];
    if (!layer.active)
    {
        return {};
    }
    if (fadeSeconds <= 0)
    {
        layer = Layer{};
        return {};
    }
    layer.clearing = true;
    layer.fadeRate = layer.weight / fadeSeconds;
    return {};
}

Result<void> setFloat(ecs::World& world, ecs::Entity entity, std::string_view parameter, float setting)
{
    return setParameter(world, entity, parameter, assets::ParameterType::Float, setting);
}

Result<void> setBool(ecs::World& world, ecs::Entity entity, std::string_view parameter, bool setting)
{
    return setParameter(world, entity, parameter, assets::ParameterType::Bool, setting ? 1.0f : 0.0f);
}

Result<void> trigger(ecs::World& world, ecs::Entity entity, std::string_view parameter)
{
    return setParameter(world, entity, parameter, assets::ParameterType::Trigger, 1.0f);
}

int32_t currentState(const ecs::World& world, ecs::Entity entity)
{
    const Pose* pose = findPose(world, entity);
    return pose != nullptr && playsMachine(*pose) ? pose->machine.current.state : -1;
}

int32_t findState(ecs::World& world, ecs::Entity entity, std::string_view name)
{
    const Pose* pose = currentPose(world, entity);
    return pose != nullptr && pose->machine.machine != nullptr ? pose->machine.machine->findState(name) : -1;
}

uint32_t eventName(std::string_view name)
{
    uint32_t hash = 2166136261u;
    for (const char c : name)
    {
        hash = (hash ^ static_cast<uint8_t>(c)) * 16777619u;
    }
    return hash;
}

uint32_t events(const ecs::World& world, std::span<AnimationEvent> into)
{
    uint32_t count = 0;
    for (const Recorded& entry : recorded)
    {
        if (entry.world == &world)
        {
            if (count < into.size())
            {
                into[count] = entry.event;
            }
            ++count;
        }
    }
    return count;
}

uint32_t openWindows(const ecs::World& world, ecs::Entity entity, std::span<OpenWindow> into)
{
    const Pose* pose = findPose(world, entity);
    if (pose == nullptr)
    {
        return 0;
    }
    for (size_t i = 0; i < pose->windows.size() && i < into.size(); ++i)
    {
        into[i] = pose->windows[i].open;
    }
    return static_cast<uint32_t>(pose->windows.size());
}

RootMotion rootMotion(const ecs::World& world, ecs::Entity entity)
{
    const Pose* pose = findPose(world, entity);
    return pose != nullptr ? pose->moved : RootMotion{{0, 0, 0}, Quaternion::identity()};
}

Result<void> setTwoBoneIk(ecs::World& world, ecs::Entity entity, uint32_t slot, TwoBoneIkDesc desc)
{
    Pose* pose = poseWithJoint(world, entity, slot, desc.joint, 2);
    if (pose == nullptr)
    {
        return Status::NotFound;
    }
    aimSlot(pose->ik[slot], IkKind::TwoBone, desc.weight, desc.fadeSeconds);
    pose->ik[slot].twoBone = desc;
    return {};
}

Result<void> setLookAt(ecs::World& world, ecs::Entity entity, uint32_t slot, LookAtDesc desc)
{
    Pose* pose = poseWithJoint(world, entity, slot, desc.joint, 0);
    if (pose == nullptr)
    {
        return Status::NotFound;
    }
    aimSlot(pose->ik[slot], IkKind::LookAt, desc.weight, desc.fadeSeconds);
    pose->ik[slot].lookAt = desc;
    return {};
}

Result<void> clearIk(ecs::World& world, ecs::Entity entity, uint32_t slot, float fadeSeconds)
{
    Pose* pose = slot < maxIkSlots ? findPose(world, entity) : nullptr;
    if (pose == nullptr)
    {
        return Status::NotFound;
    }
    IkSlot& ik = pose->ik[slot];
    if (ik.kind == IkKind::None)
    {
        return {};
    }
    if (fadeSeconds <= 0)
    {
        ik = IkSlot{};
        return {};
    }
    ik.clearing = true;
    ik.fadeRate = ik.weight / fadeSeconds;
    return {};
}

int32_t findJoint(ecs::World& world, ecs::Entity entity, std::string_view name)
{
    const Animator* animator = ecs::read<Animator>(world, entity);
    const Skeleton* skeleton = animator != nullptr ? skeletonOf(animator->skeleton) : nullptr;
    if (skeleton == nullptr)
    {
        return -1;
    }
    for (uint32_t j = 0; j < skeleton->jointCount; ++j)
    {
        if (skeleton->name(j) == name)
        {
            return static_cast<int32_t>(j);
        }
    }
    return -1;
}

Matrix4x4 jointModelTransform(const ecs::World& world, ecs::Entity entity, int32_t joint)
{
    const Pose* pose = findPose(world, entity);
    if (pose == nullptr || joint < 0 || static_cast<size_t>(joint) >= pose->model.size())
    {
        return Matrix4x4::identity();
    }
    return pose->model[joint];
}

namespace {

const Pose* poseAt(uint32_t pose)
{
    return pose >= 1 && pose <= pool.size() ? pool[pose - 1] : nullptr;
}

} // namespace

std::span<const Matrix4x4> poseJoints(uint32_t pose)
{
    const Pose* found = poseAt(pose);
    return found != nullptr && found->posed && found->skeleton != nullptr ? found->model.span()
                                                                          : std::span<const Matrix4x4>{};
}

int32_t poseJointIndex(uint32_t pose, std::string_view name)
{
    const Pose* found = poseAt(pose);
    const Skeleton* skeleton = found != nullptr ? found->skeleton : nullptr;
    for (uint32_t j = 0; skeleton != nullptr && j < skeleton->jointCount; ++j)
    {
        if (skeleton->name(j) == name)
        {
            return static_cast<int32_t>(j);
        }
    }
    return -1;
}

uint64_t poseSkeletonKey(uint32_t pose)
{
    const Pose* found = poseAt(pose);
    return found != nullptr && found->skeleton != nullptr ? found->skeleton->key : 0;
}

void drawSkeletons(const ecs::World& world, Vector4 color)
{
    for (const Pose* pose : pool)
    {
        if (pose == nullptr || pose->world != &world || pose->skeleton == nullptr || !pose->posed)
        {
            continue;
        }
        const scenes::WorldTransform* placed = ecs::read<scenes::WorldTransform>(world, pose->entity);
        const Matrix4x4 toWorld = placed != nullptr ? placed->matrix : Matrix4x4::identity();
        auto at = [&](uint32_t joint) {
            const Vector4& row = pose->model[joint].rows[3];
            return transformPoint(Vector3{row.x, row.y, row.z}, toWorld);
        };
        for (uint32_t j = 0; j < pose->skeleton->jointCount; ++j)
        {
            const int32_t parent = pose->skeleton->parents[j];
            if (parent >= 0)
            {
                debug::drawLine(at(static_cast<uint32_t>(parent)), at(j), color);
            }
        }
    }
}

bool reloadAnimationAsset(AssetId asset)
{
    bool loaded = false;
    // Bindings of either kind first: they point into the clip.
    Vector<BindingKey> stale(tag);
    for (const auto& entry : bindings)
    {
        if (entry.key.clip == asset || entry.key.skeleton == asset)
        {
            stale.pushBack(entry.key);
        }
    }
    for (const BindingKey& key : stale)
    {
        destroy(tag, *bindings.find(key));
        bindings.erase(key);
    }
    for (Pose* pose : pool)
    {
        if (pose == nullptr)
        {
            continue;
        }
        // Resolved again before the next evaluation.
        pose->base.binding = pose->base.previousBinding = nullptr;
        for (Layer& layer : pose->layers)
        {
            layer.player.binding = layer.player.previousBinding = nullptr;
        }
        for (StatePlay* state : {&pose->machine.current, &pose->machine.previous})
        {
            std::fill_n(state->bindings, maxStateClips, nullptr);
        }
    }
    if (Clip** clip = clips.find(asset))
    {
        loaded = *clip != nullptr;
        if (*clip != nullptr)
        {
            destroy(tag, *clip);
        }
        clips.erase(asset);
    }
    if (Skeleton** skeleton = skeletons.find(asset))
    {
        loaded = loaded || *skeleton != nullptr;
        for (Pose* pose : pool)
        {
            if (pose != nullptr && pose->skeleton == *skeleton)
            {
                pose->skeleton = nullptr; // read and fitted again by ensurePose
            }
        }
        if (*skeleton != nullptr)
        {
            destroy(tag, *skeleton);
        }
        skeletons.erase(asset);
    }
    if (StateMachine** machine = machines.find(asset))
    {
        loaded = loaded || *machine != nullptr;
        for (Pose* pose : pool)
        {
            if (pose != nullptr && pose->machine.machine == *machine)
            {
                pose->machine.machine = nullptr; // read again by sync, which starts it over
            }
        }
        if (*machine != nullptr)
        {
            destroyStateMachine(*machine);
        }
        machines.erase(asset);
    }
    return loaded;
}

void releasePoses(const ecs::World& world)
{
    for (uint32_t i = 0; i < pool.size(); ++i)
    {
        if (pool[i] != nullptr && pool[i]->world == &world)
        {
            freePose(i);
        }
    }
    forgetEvents(world);
}

uint32_t poseCount()
{
    return static_cast<uint32_t>(pool.size() - freeSlots.size());
}

void advancePoses(ecs::World& world, float deltaSeconds, uint64_t frameStartNs)
{
    advance(world, deltaSeconds, frameStartNs);
}

void evaluatePoses(ecs::World& world)
{
    posePoses(world, nullptr);
}

void registerAnimation()
{
    ecs::registerComponent<Animator>("Crucible.Animation.Animator");
    ecs::registerComponent<PoseHandle>("Crucible.Animation.PoseHandle");
    loop::registerDefaultSystem(
        {.name = updateSystemName, .phase = "Animation", .function = updateSystem, .gameplay = true});
    static constexpr std::string_view afterUpdate[] = {updateSystemName};
    loop::registerDefaultSystem(
        {.name = poseSystemName, .phase = "Animation", .function = poseSystem, .after = afterUpdate});
}

} // namespace crucible::animation
