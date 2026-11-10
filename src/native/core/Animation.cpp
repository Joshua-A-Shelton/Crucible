// The pose runtime (ADR 0079): skeletons and clips read through the asset
// source and cached by ID, a pool of poses, and the two systems. Poses are
// structure of arrays (ADR 0026): blocks of wideLanes joints, sampled and
// blended with the wide types; the hierarchy pass is per joint, since each
// joint waits for its parent.

#include <crucible/Animation.h>
#include <crucible/BinaryFile.h>
#include <crucible/ClipFile.h>
#include <crucible/DebugLines.h>
#include <crucible/HashMap.h>
#include <crucible/Jobs.h>
#include <crucible/MeshFile.h>
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

template <class T>
bool readTable(const binary::File& file, const char (&name)[5], Vector<T>& out)
{
    const int32_t index = binary::findSection(file, binary::tag(name));
    if (index < 0)
    {
        return false;
    }
    const binary::SectionInfo info = binary::sectionInfo(file, static_cast<uint32_t>(index));
    Vector<uint8_t> bytes(tag);
    bytes.resize(static_cast<size_t>(info.size));
    if (!binary::readSection(file, static_cast<uint32_t>(index), bytes.span()))
    {
        return false;
    }
    out.resize(bytes.size() / sizeof(T));
    std::memcpy(out.data(), bytes.data(), out.size() * sizeof(T));
    return true;
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
    std::string_view trackName(const assets::ClipTrack& track) const
    {
        return {names.data() + track.nameOffset, track.nameLength};
    }
};

// A clip on a skeleton: each joint's track (-1: it holds its rest pose),
// and the first sample, which additive layers subtract.
struct Binding
{
    const Clip* clip = nullptr;
    Vector<int32_t> trackOf{tag};
    Vector<Quaternion> firstRotations{tag};
    Vector<Vector3> firstPositions{tag};
    Vector<Vector3> firstScales{tag};
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

// ---------------------------------------------------------------------------
// Poses
// ---------------------------------------------------------------------------

// One clip playing, with the one it's fading from.
struct Player
{
    AssetId clip{};
    float time = 0;
    AssetId previous{};
    float previousTime = 0;
    float fadeElapsed = 0;
    float fadeDuration = 0; // 0: no fade
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
    Player base;
    float speed = 1;
    bool loop = true;
    Layer layers[maxLayers];
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
        for (Layer& layer : layers)
        {
            layer.jointWeights.clear();
            layer.masked = false;
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

float clipDuration(const AssetId& id)
{
    const Clip* clip = clipOf(id);
    return clip != nullptr ? clip->info.duration : 0.0f;
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

void evaluate(Pose& pose)
{
    if (pose.skeleton == nullptr)
    {
        return;
    }
    samplePlayer(pose, pose.base, pose.rotations.data(), pose.positions.data(), pose.scales.data());
    for (const Layer& layer : pose.layers)
    {
        if (layer.active && layer.weight > 0)
        {
            applyLayer(pose, layer);
        }
    }
    hierarchy(pose);
    pose.posed = true;
}

void resolve(Player& player, const Pose& pose)
{
    player.binding = bindingOf(player.clip, pose.skeletonId, *pose.skeleton);
    player.previousBinding =
        player.fadeDuration > 0 ? bindingOf(player.previous, pose.skeletonId, *pose.skeleton) : nullptr;
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
        const Animator& animator = *ecs::read<Animator>(world, pose->entity);
        // The Animator's clip changed (an inspector edit, a loaded scene):
        // cut to it. play() keeps the two the same.
        if (!(animator.clip == pose->base.clip))
        {
            pose->base.start(animator.clip, 0);
        }
        if (pose->skeleton == nullptr)
        {
            continue;
        }
        resolve(pose->base, *pose);
        for (Layer& layer : pose->layers)
        {
            if (layer.active)
            {
                resolve(layer.player, *pose);
                if (layer.masked != !layer.mask.empty() || (layer.masked && layer.jointWeights.empty()))
                {
                    buildMask(layer, *pose->skeleton);
                }
            }
        }
        // The handle, for skinning (step 3) to find the pose by index.
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

void advance(ecs::World& world, float delta)
{
    Vector<Pose*> poses(tag);
    gather(world, poses);
    for (Pose* pose : poses)
    {
        const Animator& animator = *ecs::read<Animator>(world, pose->entity);
        advancePlayer(pose->base, delta, animator.speed, animator.loop);
        for (Layer& layer : pose->layers)
        {
            if (!layer.active)
            {
                continue;
            }
            advancePlayer(layer.player, delta, layer.desc.speed, layer.desc.loop);
            const float target = layer.clearing ? 0.0f : layer.desc.weight;
            if (layer.fadeRate <= 0)
            {
                layer.weight = target;
            }
            else if (layer.weight < target)
            {
                layer.weight = std::min(layer.weight + layer.fadeRate * delta, target);
            }
            else
            {
                layer.weight = std::max(layer.weight - layer.fadeRate * delta, target);
            }
            if (layer.clearing && layer.weight <= 0)
            {
                layer = Layer{};
            }
        }
    }
}

void updateSystem(const loop::SystemContext& context, void*)
{
    if (context.world != nullptr)
    {
        advance(*context.world, static_cast<float>(context.deltaSeconds));
    }
}

void poseSystem(const loop::SystemContext& context, void*)
{
    if (context.world != nullptr)
    {
        posePoses(*context.world, context.commands);
    }
}

} // namespace

Result<void> play(ecs::World& world, ecs::Entity entity, AssetId clip, float fadeSeconds)
{
    Pose* pose = ensurePose(world, entity);
    if (pose == nullptr || pose->skeleton == nullptr || (!isNone(clip) && clipOf(clip) == nullptr))
    {
        return Status::NotFound;
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
}

uint32_t poseCount()
{
    return static_cast<uint32_t>(pool.size() - freeSlots.size());
}

void advancePoses(ecs::World& world, float deltaSeconds)
{
    advance(world, deltaSeconds);
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
