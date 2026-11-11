#pragma once

// What the pose runtime's tests share (ADR 0079): the Walker imported into
// MESH and CLIP files, served through an asset source, and a scalar
// reference of sampling, blending, and the hierarchy pass, which the wide
// runtime is compared against.

#include "MeshTestSupport.h"

#include <crucible/Animation.h>
#include <crucible/AssetSource.h>
#include <crucible/ClipFile.h>
#include <crucible/Ecs.h>
#include <crucible/StateMachineFile.h>
#include <crucible/Transform.h>

namespace crucible::animation::testing {

using namespace crucible::importers;
using namespace crucible::importers::testing;

using Quat = std::array<float, 4>;

inline assets::AssetId idOf(uint8_t n)
{
    assets::AssetId id{};
    id.bytes[0] = n;
    return id;
}

// The Walker's model and clips as files, by the first byte of their ID: 1
// the model, 2 Walk, 3 Idle, 4 WalkForward; tests add more (state machines).
struct WalkerAssets
{
    inline static std::map<uint8_t, std::vector<uint8_t>> files;
    // Each clip's sections as imported, so events can be added to them.
    inline static std::map<uint8_t, std::pair<std::vector<MeshSectionInfo>, std::vector<std::vector<uint8_t>>>> clips;

    static std::vector<uint8_t> file(uint32_t kind, uint32_t version, const std::vector<MeshSectionInfo>& infos,
                                     const std::vector<std::vector<uint8_t>>& sections)
    {
        binary::Writer* writer = binary::createWriter(kind, version);
        for (size_t i = 0; i < infos.size(); ++i)
        {
            REQUIRE(binary::addSection(*writer, infos[i].tag, infos[i].version, sections[i], 16));
        }
        Vector<std::byte> bytes(MemoryTag::General);
        REQUIRE(binary::writeToMemory(*writer, binary::Compression::None, bytes));
        binary::destroyWriter(writer);
        const auto* data = reinterpret_cast<const uint8_t*>(bytes.data());
        return {data, data + bytes.size()};
    }

    static void install()
    {
        if (files.empty())
        {
            Result<MeshImport*> imported = importWalker(withClips(settings(), 30.0f));
            REQUIRE(imported);
            MeshImport& mesh = *imported.value();
            std::vector<MeshSectionInfo> infos;
            std::vector<std::vector<uint8_t>> sections;
            for (uint32_t i = 0; i < meshSectionCount(mesh); ++i)
            {
                infos.push_back(meshSection(mesh, i));
                sections.emplace_back(infos.back().size);
                copyMeshSection(mesh, i, sections.back());
            }
            files[1] = file(assets::meshFileKind, assets::meshFileVersion, infos, sections);
            for (uint32_t c = 0; c < meshClipCount(mesh); ++c)
            {
                infos.clear();
                sections.clear();
                for (uint32_t i = 0; i < meshClipSectionCount(mesh, c); ++i)
                {
                    infos.push_back(meshClipSection(mesh, c, i));
                    sections.emplace_back(infos.back().size);
                    copyMeshClipSection(mesh, c, i, sections.back());
                }
                files[static_cast<uint8_t>(2 + c)] =
                    file(assets::clipFileKind, assets::clipFileVersion, infos, sections);
                clips[static_cast<uint8_t>(2 + c)] = {infos, sections};
            }
            destroyMeshImport(&mesh);
        }
        assets::installAssetSource({[](const assets::AssetId* id, void* context, assets::AssetDelivery deliver) {
            const auto found = files.find(id->bytes[0]);
            if (found == files.end())
            {
                return -1;
            }
            deliver(context, found->second.data(), found->second.size());
            return 0;
        }});
    }
};

template <class T>
std::vector<uint8_t> bytesOf(const std::vector<T>& items)
{
    const auto* data = reinterpret_cast<const uint8_t*>(items.data());
    return {data, data + items.size() * sizeof(T)};
}

// Gives a clip events and windows (or takes them away), as the importer
// would from the sidecar, and makes the runtime read it again.
inline void markClip(uint8_t clip, const std::vector<assets::ClipEvent>& events,
                     const std::vector<assets::ClipWindow>& windows)
{
    WalkerAssets::install();
    auto [infos, sections] = WalkerAssets::clips.at(clip);
    if (!events.empty())
    {
        infos.push_back({binary::tag("EVNT"), 1, static_cast<uint64_t>(events.size() * sizeof(assets::ClipEvent))});
        sections.push_back(bytesOf(events));
    }
    if (!windows.empty())
    {
        infos.push_back({binary::tag("WNDW"), 1, static_cast<uint64_t>(windows.size() * sizeof(assets::ClipWindow))});
        sections.push_back(bytesOf(windows));
    }
    WalkerAssets::files[clip] = WalkerAssets::file(assets::clipFileKind, assets::clipFileVersion, infos, sections);
    (void)reloadAnimationAsset(idOf(clip));
}

// A state machine file, laid out as the importer lays it out.
struct MachineBuilder
{
    std::vector<assets::StateParameter> parameters;
    std::vector<assets::StateEntry> states;
    std::vector<assets::StateMotion> motions;
    std::vector<assets::StateTransition> transitions;
    std::vector<assets::TransitionCondition> conditions;
    std::vector<char> names;
    uint32_t start = 0;

    std::pair<uint32_t, uint32_t> name(std::string_view text)
    {
        const auto offset = static_cast<uint32_t>(names.size());
        names.insert(names.end(), text.begin(), text.end());
        return {offset, static_cast<uint32_t>(text.size())};
    }

    uint32_t parameter(std::string_view text, assets::ParameterType type, float initial = 0)
    {
        const auto [offset, length] = name(text);
        parameters.push_back({offset, length, type, initial});
        return static_cast<uint32_t>(parameters.size() - 1);
    }

    // A state playing one clip.
    uint32_t state(std::string_view text, uint8_t clip, float speed = 1, bool loop = true)
    {
        return blend(text, -1, {{clip, 0.0f}}, speed, loop);
    }

    // A 1D blend space over `by` (or one clip, with by -1).
    uint32_t blend(std::string_view text, int32_t by, std::vector<std::pair<uint8_t, float>> clips, float speed = 1,
                   bool loop = true)
    {
        const auto [offset, length] = name(text);
        assets::StateEntry entry{offset,
                                 length,
                                 static_cast<uint32_t>(motions.size()),
                                 static_cast<uint32_t>(clips.size()),
                                 by >= 0 ? 1u : 0u,
                                 {by, -1},
                                 speed,
                                 loop ? 1u : 0u};
        for (const auto& [clip, at] : clips)
        {
            motions.push_back({idOf(clip), {at, 0}});
        }
        states.push_back(entry);
        return static_cast<uint32_t>(states.size() - 1);
    }

    void transition(int32_t from, uint32_t to, float blendSeconds, float exitTime,
                    std::vector<assets::TransitionCondition> tests = {})
    {
        transitions.push_back({from, to, blendSeconds, exitTime, static_cast<uint32_t>(conditions.size()),
                               static_cast<uint32_t>(tests.size())});
        conditions.insert(conditions.end(), tests.begin(), tests.end());
    }

    std::vector<uint8_t> build() const
    {
        const assets::StateMachineInfo info{start, static_cast<uint32_t>(parameters.size()),
                                            static_cast<uint32_t>(states.size()),
                                            static_cast<uint32_t>(transitions.size())};
        const std::vector<std::vector<uint8_t>> sections = {bytesOf(std::vector<assets::StateMachineInfo>{info}),
                                                            bytesOf(parameters),
                                                            bytesOf(states),
                                                            bytesOf(motions),
                                                            bytesOf(transitions),
                                                            bytesOf(conditions),
                                                            bytesOf(names)};
        const uint32_t tags[] = {binary::tag("INFO"), binary::tag("PARM"), binary::tag("STAT"), binary::tag("MOTN"),
                                 binary::tag("TRAN"), binary::tag("COND"), binary::tag("NAME")};
        std::vector<MeshSectionInfo> infos;
        for (size_t i = 0; i < sections.size(); ++i)
        {
            infos.push_back({tags[i], 1, static_cast<uint64_t>(sections[i].size())});
        }
        return WalkerAssets::file(assets::stateMachineFileKind, assets::stateMachineFileVersion, infos, sections);
    }

    // Installs it as asset `id` (read again if it was read before).
    void install(uint8_t id) const
    {
        WalkerAssets::install();
        WalkerAssets::files[id] = build();
        (void)reloadAnimationAsset(idOf(id));
    }
};

// A world with the engine's components and a Walker character.
struct Character
{
    ecs::World* world = nullptr;
    ecs::Entity entity{};

    explicit Character(uint8_t clip = 2, bool loop = true)
    {
        WalkerAssets::install();
        scenes::registerTransforms();
        registerAnimation();
        world = ecs::createWorld();
        const ecs::ComponentId components[] = {ecs::componentIdOf<Animator>, ecs::componentIdOf<scenes::LocalTransform>,
                                               ecs::componentIdOf<scenes::WorldTransform>};
        entity = ecs::createEntity(*world, components);
        *ecs::get<Animator>(*world, entity) = {idOf(1), clip == 0 ? assets::AssetId{} : idOf(clip), 1.0f, loop};
        *ecs::get<scenes::LocalTransform>(*world, entity) = {{0, 0, 0}, Quaternion::identity(), {1, 1, 1}};
    }

    ~Character()
    {
        releasePoses(*world);
        ecs::destroyWorld(world);
    }

    Character(const Character&) = delete;
    Character& operator=(const Character&) = delete;

    // The input clock: each step's frame starts `seconds` after the last's.
    uint64_t clock = 1'000'000'000;

    // Advances (as "Animation.Update" does) then poses (as "Animation.Pose").
    void step(float seconds)
    {
        clock += static_cast<uint64_t>(static_cast<double>(seconds) * 1e9);
        advancePoses(*world, seconds, clock);
        evaluatePoses(*world);
    }

    // Poses again without time passing (as while editing).
    void pose()
    {
        evaluatePoses(*world);
    }

    Matrix4x4 model(int32_t joint) const
    {
        return jointModelTransform(*world, entity, joint);
    }
};

// --- The scalar reference ---------------------------------------------------

struct LocalPose
{
    std::vector<Quat> rotations;
    std::vector<Vec3> positions, scales;
};

// The Walker's model, read once (its skeleton and clips).
struct Reference
{
    Imported model{importWalker(withClips(settings(), 30.0f))};
    std::vector<assets::JointEntry> joints = model.table<assets::JointEntry>("JNTS");
    std::vector<MeshNode> nodes = model.table<MeshNode>("NODE");

    std::string_view jointName(size_t j) const
    {
        return model.name(joints[j].nameOffset, joints[j].nameLength);
    }

    int32_t joint(std::string_view name) const
    {
        for (size_t j = 0; j < joints.size(); ++j)
        {
            if (jointName(j) == name)
            {
                return static_cast<int32_t>(j);
            }
        }
        return -1;
    }

    LocalPose rest() const
    {
        LocalPose pose;
        for (const assets::JointEntry& joint : joints)
        {
            const MeshNode& node = nodes[joint.node];
            pose.rotations.push_back({node.rotation[0], node.rotation[1], node.rotation[2], node.rotation[3]});
            pose.positions.push_back({node.translation[0], node.translation[1], node.translation[2]});
            pose.scales.push_back({node.scale[0], node.scale[1], node.scale[2]});
        }
        return pose;
    }

    Clip clip(uint32_t index) const
    {
        return Clip(*model.mesh, index);
    }

    // A clip at a time: its two nearest samples, interpolated (rotations by
    // normalized lerp); joints it doesn't move at rest.
    LocalPose sample(const Clip& clip, float time) const
    {
        LocalPose pose = rest();
        const assets::ClipInfo info = clip.info();
        const float at = time * info.sampleRate;
        const uint32_t i0 = std::min(static_cast<uint32_t>(at), info.sampleCount - 1);
        const uint32_t i1 = std::min(i0 + 1, info.sampleCount - 1);
        const float t = std::clamp(at - static_cast<float>(i0), 0.0f, 1.0f);
        for (const assets::ClipTrack& track : clip.table<assets::ClipTrack>("TRCK"))
        {
            const int32_t j = joint(clip.trackName(track));
            if (j < 0)
            {
                continue;
            }
            pose.rotations[j] = nlerp(clip.rotation(track, i0), clip.rotation(track, i1), t);
            pose.positions[j] = lerp(clip.translation(track, i0), clip.translation(track, i1), t);
            pose.scales[j] = lerp(clip.scale(track, i0), clip.scale(track, i1), t);
        }
        return pose;
    }

    // Model space, parents first (the Walker has no nodes between joints).
    std::vector<Matrix> modelOf(const LocalPose& pose) const
    {
        std::vector<Matrix> result(joints.size());
        for (size_t j = 0; j < joints.size(); ++j)
        {
            const Matrix local = compose(pose.positions[j], pose.rotations[j], pose.scales[j]);
            result[j] = joints[j].parent >= 0 ? importers::testing::multiply(local, result[joints[j].parent]) : local;
        }
        return result;
    }

    static Quat nlerp(const Quat& a, const Quat& b, float t)
    {
        const float d = a[0] * b[0] + a[1] * b[1] + a[2] * b[2] + a[3] * b[3];
        const float side = d < 0 ? -1.0f : 1.0f;
        Quat r;
        float length = 0;
        for (int c = 0; c < 4; ++c)
        {
            r[c] = a[c] * (1 - t) + b[c] * side * t;
            length += r[c] * r[c];
        }
        length = std::sqrt(length);
        for (float& c : r)
        {
            c /= length;
        }
        return r;
    }

    static Vec3 lerp(const Vec3& a, const Vec3& b, float t)
    {
        return {a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t};
    }

    // Hamilton product: b, then a.
    static Quat multiply(const Quat& a, const Quat& b)
    {
        return {a[3] * b[0] + a[0] * b[3] + a[1] * b[2] - a[2] * b[1],
                a[3] * b[1] + a[1] * b[3] + a[2] * b[0] - a[0] * b[2],
                a[3] * b[2] + a[2] * b[3] + a[0] * b[1] - a[1] * b[0],
                a[3] * b[3] - a[0] * b[0] - a[1] * b[1] - a[2] * b[2]};
    }

    static Quat conjugate(const Quat& q)
    {
        return {-q[0], -q[1], -q[2], q[3]};
    }
};

// How far a posed joint is from the reference: the largest difference of
// any matrix element.
inline float difference(const Matrix4x4& posed, const Matrix& expected)
{
    float worst = 0;
    for (int row = 0; row < 4; ++row)
    {
        const float values[4] = {posed.rows[row].x, posed.rows[row].y, posed.rows[row].z, posed.rows[row].w};
        for (int column = 0; column < 4; ++column)
        {
            worst = std::max(worst, std::fabs(values[column] - expected[row * 4 + column]));
        }
    }
    return worst;
}

// Every joint's model matrix against the reference pose.
inline float worstJoint(const Character& walker, const Reference& reference, const LocalPose& expected)
{
    const std::vector<Matrix> model = reference.modelOf(expected);
    float worst = 0;
    for (size_t j = 0; j < model.size(); ++j)
    {
        worst = std::max(worst, difference(walker.model(static_cast<int32_t>(j)), model[j]));
    }
    return worst;
}

// A local pose blended toward another by t, as the runtime blends.
inline LocalPose blendPoses(const LocalPose& a, const LocalPose& b, float t)
{
    LocalPose result = a;
    for (size_t j = 0; j < a.rotations.size(); ++j)
    {
        result.rotations[j] = Reference::nlerp(a.rotations[j], b.rotations[j], t);
        result.positions[j] = Reference::lerp(a.positions[j], b.positions[j], t);
        result.scales[j] = Reference::lerp(a.scales[j], b.scales[j], t);
    }
    return result;
}

} // namespace crucible::animation::testing
