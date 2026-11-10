#pragma once

// What the mesh import tests share (ADR 0043, 0044): reading an import's
// sections back as a shader or the engine would.

#include <crucible/BinaryFile.h>
#include <crucible/ClipFile.h>
#include <crucible/importers/Mesh.h>
#include <crucible/importers/Texture.h>

#include <doctest/doctest.h>

#include <algorithm>
#include <array>
#include <cmath>
#include <cstring>
#include <fstream>
#include <iterator>
#include <map>
#include <sstream>
// doctest prints a string_view with operator<<, which MSVC declares only in
// <ostream>.
#include <ostream>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

namespace crucible::importers::testing {

using Vec3 = std::array<float, 3>;

inline Vec3 cross(const Vec3& a, const Vec3& b)
{
    return {a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]};
}

inline float dot(const Vec3& a, const Vec3& b)
{
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
}

inline Vec3 minus(const Vec3& a, const Vec3& b)
{
    return {a[0] - b[0], a[1] - b[1], a[2] - b[2]};
}

inline MeshSettings settings()
{
    return {.optimize = true,
            .generateTangents = true,
            .generateLods = true,
            .maxLods = 4,
            .lodRatio = 0.5f,
            .lodMaxError = 0.05f};
}

// An imported file's sections, by tag.
struct Imported
{
    MeshImport* mesh = nullptr;
    std::map<uint32_t, std::vector<uint8_t>> sections;

    explicit Imported(const std::string& json, MeshSettings options = settings())
    {
        std::vector<uint8_t> bytes(json.begin(), json.end());
        Result<GltfSource*> source = parseGltf(bytes);
        INFO(lastImportError());
        REQUIRE(source);
        Result<MeshImport*> result = importGltf(*source.value(), options);
        INFO(lastImportError());
        REQUIRE(result);
        destroyGltfSource(source.value());
        read(result.value());
    }

    explicit Imported(Result<MeshImport*> result)
    {
        INFO(lastImportError());
        REQUIRE(result);
        read(result.value());
    }

    Imported(const Imported&) = delete;
    Imported& operator=(const Imported&) = delete;

    void read(MeshImport* imported)
    {
        mesh = imported;
        for (uint32_t i = 0; i < meshSectionCount(*mesh); ++i)
        {
            const MeshSectionInfo info = meshSection(*mesh, i);
            std::vector<uint8_t>& data = sections[info.tag];
            data.resize(info.size);
            copyMeshSection(*mesh, i, data);
        }
    }

    ~Imported()
    {
        destroyMeshImport(mesh);
    }

    template <class T>
    std::vector<T> table(const char (&tag)[5]) const
    {
        const std::vector<uint8_t>& data = sections.at(binary::tag(tag));
        std::vector<T> values(data.size() / sizeof(T));
        std::memcpy(values.data(), data.data(), values.size() * sizeof(T));
        return values;
    }

    bool has(const char (&tag)[5]) const
    {
        return sections.count(binary::tag(tag)) != 0;
    }

    Vec3 position(uint32_t vertex) const
    {
        const auto& data = sections.at(binary::tag("POSN"));
        Vec3 p;
        std::memcpy(p.data(), data.data() + vertex * 12, 12);
        return p;
    }

    // Octahedral decoding, as a shader does it.
    static Vec3 decode(int16_t ex, int16_t ey)
    {
        float x = std::max(ex / 32767.0f, -1.0f), y = std::max(ey / 32767.0f, -1.0f);
        float z = 1.0f - std::fabs(x) - std::fabs(y);
        if (z < 0)
        {
            const float fx = (1.0f - std::fabs(y)) * (x >= 0 ? 1.0f : -1.0f);
            const float fy = (1.0f - std::fabs(x)) * (y >= 0 ? 1.0f : -1.0f);
            x = fx;
            y = fy;
        }
        const float length = std::sqrt(x * x + y * y + z * z);
        return {x / length, y / length, z / length};
    }

    Vec3 normal(uint32_t vertex) const
    {
        const auto& data = sections.at(binary::tag("NRML"));
        int16_t e[2];
        std::memcpy(e, data.data() + vertex * 4, 4);
        return decode(e[0], e[1]);
    }

    Vec3 outlineNormal(uint32_t vertex) const
    {
        const auto& data = sections.at(binary::tag("ONRM"));
        int16_t e[2];
        std::memcpy(e, data.data() + vertex * 4, 4);
        return decode(e[0], e[1]);
    }

    // Tangent direction and handedness.
    std::pair<Vec3, float> tangent(uint32_t vertex) const
    {
        const auto& data = sections.at(binary::tag("TNGT"));
        int16_t e[4];
        std::memcpy(e, data.data() + vertex * 8, 8);
        return {decode(e[0], e[1]), e[2] < 0 ? -1.0f : 1.0f};
    }

    // UV set 0 or 1, as half or full floats.
    std::array<float, 2> uv(uint32_t set, uint32_t vertex) const
    {
        const auto& data = sections.at(binary::tag(set == 0 ? "UV0_" : "UV1_"));
        const bool full = (table<MeshFileInfo>("INFO")[0].floatUvs & (1u << set)) != 0;
        std::array<float, 2> result{};
        if (full)
        {
            std::memcpy(result.data(), data.data() + vertex * 8, 8);
            return result;
        }
        for (uint32_t c = 0; c < 2; ++c)
        {
            uint16_t half;
            std::memcpy(&half, data.data() + vertex * 4 + c * 2, 2);
            const int exponent = (half >> 10) & 31;
            const float mantissa = static_cast<float>(half & 1023);
            const float magnitude =
                exponent == 0 ? std::ldexp(mantissa, -24) : std::ldexp(1.0f + mantissa / 1024.0f, exponent - 15);
            result[c] = (half & 0x8000) != 0 ? -magnitude : magnitude;
        }
        return result;
    }

    std::string_view name(uint32_t offset, uint32_t length) const
    {
        const std::vector<uint8_t>& names = sections.at(binary::tag("NAME"));
        return {reinterpret_cast<const char*>(names.data()) + offset, length};
    }

    // A level's triangles, as vertex triples, for mesh `m`.
    std::vector<std::array<uint32_t, 3>> triangles(uint32_t m, uint32_t level) const
    {
        const MeshEntry entry = table<MeshEntry>("MSHS")[m];
        const MeshLod lod = table<MeshLod>("LODS")[entry.lodOffset + level];
        const std::vector<MeshRange> ranges = table<MeshRange>("RNGS");
        const std::vector<uint8_t>& indexBytes = sections.at(binary::tag("INDX"));
        std::vector<std::array<uint32_t, 3>> result;
        for (uint32_t s = 0; s < entry.submeshCount; ++s)
        {
            const MeshRange range = ranges[lod.rangeOffset + s];
            for (uint32_t i = 0; i < range.indexCount; i += 3)
            {
                std::array<uint32_t, 3> triangle{};
                for (uint32_t k = 0; k < 3; ++k)
                {
                    uint32_t index = 0;
                    std::memcpy(&index, indexBytes.data() + range.indexByteOffset + (i + k) * entry.indexSize,
                                entry.indexSize);
                    triangle[k] = entry.vertexOffset + index;
                }
                result.push_back(triangle);
            }
        }
        return result;
    }
};

// Skins and clips (ADR 0078).

inline std::vector<uint8_t> readFile(const std::string& path)
{
    std::ifstream file(path, std::ios::binary);
    REQUIRE(file.good());
    return {std::istreambuf_iterator<char>(file), std::istreambuf_iterator<char>()};
}

inline Result<MeshImport*> importWalker(MeshSettings options = settings())
{
    const std::string folder = CRUCIBLE_WALKER_DIR;
    const std::vector<uint8_t> json = readFile(folder + "/Walker.gltf");
    Result<GltfSource*> source = parseGltf(json);
    REQUIRE(source);
    REQUIRE(gltfBufferCount(*source.value()) == 1);
    const std::vector<uint8_t> buffer = readFile(folder + "/Walker.bin");
    REQUIRE(setGltfBuffer(*source.value(), 0, buffer));
    Result<MeshImport*> result = importGltf(*source.value(), options);
    destroyGltfSource(source.value());
    return result;
}

inline Result<MeshImport*> importUfbx(const std::string& name, MeshSettings options = settings())
{
    const std::vector<uint8_t> bytes = readFile(std::string(CRUCIBLE_UFBX_DATA_DIR) + "/" + name);
    return importFbx(bytes, options);
}

inline MeshSettings withClips(MeshSettings options, float sampleRate = 30.0f, uint32_t influences = 4)
{
    options.maxInfluences = influences;
    options.clipSampleRate = sampleRate;
    return options;
}

// One clip's sections, by tag, decoded as the pose runtime will.
struct Clip
{
    std::string name;
    std::map<uint32_t, std::vector<uint8_t>> sections;

    Clip(const MeshImport& mesh, uint32_t clip)
        : name(meshClipName(mesh, clip))
    {
        for (uint32_t i = 0; i < meshClipSectionCount(mesh, clip); ++i)
        {
            const MeshSectionInfo info = meshClipSection(mesh, clip, i);
            std::vector<uint8_t>& data = sections[info.tag];
            data.resize(info.size);
            copyMeshClipSection(mesh, clip, i, data);
        }
    }

    template <class T>
    std::vector<T> table(const char (&tag)[5]) const
    {
        const std::vector<uint8_t>& data = sections.at(binary::tag(tag));
        std::vector<T> values(data.size() / sizeof(T));
        std::memcpy(values.data(), data.data(), values.size() * sizeof(T));
        return values;
    }

    assets::ClipInfo info() const
    {
        return table<assets::ClipInfo>("INFO").at(0);
    }

    std::string_view trackName(const assets::ClipTrack& track) const
    {
        const std::vector<uint8_t>& names = sections.at(binary::tag("NAME"));
        return {reinterpret_cast<const char*>(names.data()) + track.nameOffset, track.nameLength};
    }

    // A track's rotation at a sample, from smallest three.
    std::array<float, 4> rotation(const assets::ClipTrack& track, uint32_t sample) const
    {
        const bool constant = (track.constant & static_cast<uint32_t>(assets::ClipChannel::Rotation)) != 0;
        const std::vector<uint16_t> values = table<uint16_t>("ROTS");
        const uint16_t* packed = values.data() + (track.rotationOffset + (constant ? 0 : sample)) * 4;
        std::array<float, 4> q{};
        float sum = 0.0f;
        for (int c = 0, k = 0; c < 4; ++c)
        {
            if (c != packed[3])
            {
                q[c] = (packed[k++] / 65535.0f * 2.0f - 1.0f) * 0.70710678f;
                sum += q[c] * q[c];
            }
        }
        q[packed[3]] = std::sqrt(std::max(1.0f - sum, 0.0f));
        return q;
    }

    Vec3 vector(const char (&tag)[5], uint32_t offset, bool constant, const float* lo, const float* hi,
                uint32_t sample) const
    {
        const std::vector<uint16_t> values = table<uint16_t>(tag);
        const uint16_t* packed = values.data() + (offset + (constant ? 0 : sample)) * 3;
        return {lo[0] + (hi[0] - lo[0]) * packed[0] / 65535.0f, lo[1] + (hi[1] - lo[1]) * packed[1] / 65535.0f,
                lo[2] + (hi[2] - lo[2]) * packed[2] / 65535.0f};
    }

    Vec3 translation(const assets::ClipTrack& track, uint32_t sample) const
    {
        return vector("POSS", track.translationOffset,
                      (track.constant & static_cast<uint32_t>(assets::ClipChannel::Translation)) != 0,
                      track.translationMin, track.translationMax, sample);
    }

    Vec3 scale(const assets::ClipTrack& track, uint32_t sample) const
    {
        return vector("SCLS", track.scaleOffset,
                      (track.constant & static_cast<uint32_t>(assets::ClipChannel::Scale)) != 0, track.scaleMin,
                      track.scaleMax, sample);
    }
};

using Matrix = std::array<float, 16>; // row-major, for row vectors (p' = p M)

inline Matrix multiply(const Matrix& a, const Matrix& b)
{
    Matrix r{};
    for (int row = 0; row < 4; ++row)
    {
        for (int column = 0; column < 4; ++column)
        {
            for (int k = 0; k < 4; ++k)
            {
                r[row * 4 + column] += a[row * 4 + k] * b[k * 4 + column];
            }
        }
    }
    return r;
}

// Scale, then rotate, then translate.
inline Matrix compose(const Vec3& t, const std::array<float, 4>& q, const Vec3& s)
{
    const float x = q[0], y = q[1], z = q[2], w = q[3];
    const float rows[3][3] = {{1 - 2 * (y * y + z * z), 2 * (x * y + z * w), 2 * (x * z - y * w)},
                              {2 * (x * y - z * w), 1 - 2 * (x * x + z * z), 2 * (y * z + x * w)},
                              {2 * (x * z + y * w), 2 * (y * z - x * w), 1 - 2 * (x * x + y * y)}};
    Matrix m{};
    for (int row = 0; row < 3; ++row)
    {
        for (int column = 0; column < 3; ++column)
        {
            m[row * 4 + column] = rows[row][column] * s[row];
        }
    }
    m[12] = t[0];
    m[13] = t[1];
    m[14] = t[2];
    m[15] = 1.0f;
    return m;
}

inline Vec3 transform(const Vec3& p, const float* m)
{
    Vec3 r{};
    for (int column = 0; column < 3; ++column)
    {
        r[column] = p[0] * m[column] + p[1] * m[4 + column] + p[2] * m[8 + column] + m[12 + column];
    }
    return r;
}

// Every vertex of the model, skinned: each node's local transform is its
// rest transform, or the clip's at `sample` for the joints it moves.
inline std::vector<Vec3> skinned(const Imported& imported, const Clip* clip, uint32_t sample)
{
    const std::vector<MeshNode> nodes = imported.table<MeshNode>("NODE");
    const std::vector<assets::JointEntry> joints = imported.table<assets::JointEntry>("JNTS");
    const uint32_t influences = imported.table<assets::SkinInfo>("SKIN")[0].influences;
    std::vector<Matrix> local(nodes.size());
    for (size_t n = 0; n < nodes.size(); ++n)
    {
        const MeshNode& node = nodes[n];
        local[n] = compose({node.translation[0], node.translation[1], node.translation[2]},
                           {node.rotation[0], node.rotation[1], node.rotation[2], node.rotation[3]},
                           {node.scale[0], node.scale[1], node.scale[2]});
    }
    if (clip != nullptr)
    {
        for (const assets::ClipTrack& track : clip->table<assets::ClipTrack>("TRCK"))
        {
            for (const assets::JointEntry& joint : joints)
            {
                if (imported.name(joint.nameOffset, joint.nameLength) == clip->trackName(track))
                {
                    local[joint.node] = compose(clip->translation(track, sample), clip->rotation(track, sample),
                                                clip->scale(track, sample));
                }
            }
        }
    }
    std::vector<Matrix> world(nodes.size());
    for (size_t n = 0; n < nodes.size(); ++n)
    {
        world[n] = local[n];
        for (int32_t p = nodes[n].parent; p >= 0; p = nodes[p].parent)
        {
            world[n] = multiply(world[n], local[p]);
        }
    }
    std::vector<Matrix> skinning(joints.size());
    for (size_t j = 0; j < joints.size(); ++j)
    {
        Matrix inverseBind;
        std::memcpy(inverseBind.data(), joints[j].inverseBind, sizeof(inverseBind));
        skinning[j] = multiply(inverseBind, world[joints[j].node]);
    }

    const std::vector<uint16_t> indices = imported.table<uint16_t>("JIDX");
    const std::vector<uint8_t> weights = imported.table<uint8_t>("JWGT");
    const size_t vertexCount = imported.sections.at(binary::tag("POSN")).size() / 12;
    std::vector<Vec3> result;
    for (uint32_t v = 0; v < vertexCount; ++v)
    {
        const Vec3 p = imported.position(v);
        Vec3 sum{};
        for (uint32_t k = 0; k < influences; ++k)
        {
            const float w = weights[v * influences + k] / 255.0f;
            const Vec3 q = transform(p, skinning[indices[v * influences + k]].data());
            for (int c = 0; c < 3; ++c)
            {
                sum[c] += w * q[c];
            }
        }
        result.push_back(sum);
    }
    return result;
}

inline std::vector<Vec3> skinObjVertices(const std::string& name)
{
    const std::vector<uint8_t> bytes = readFile(std::string(CRUCIBLE_UFBX_DATA_DIR) + "/" + name);
    std::istringstream text(std::string(bytes.begin(), bytes.end()));
    std::vector<Vec3> result;
    std::string line;
    while (std::getline(text, line))
    {
        if (line.rfind("v ", 0) == 0)
        {
            std::istringstream values(line.substr(2));
            Vec3 p{};
            values >> p[0] >> p[1] >> p[2];
            result.push_back(p);
        }
    }
    return result;
}

// The largest distance from a point of one set to the nearest of the other,
// both ways.
inline float farthest(const std::vector<Vec3>& a, const std::vector<Vec3>& b)
{
    auto oneWay = [](const std::vector<Vec3>& from, const std::vector<Vec3>& to) {
        float worst = 0.0f;
        for (const Vec3& p : from)
        {
            float nearest = 1e30f;
            for (const Vec3& q : to)
            {
                const Vec3 d = minus(p, q);
                nearest = std::min(nearest, dot(d, d));
            }
            worst = std::max(worst, nearest);
        }
        return std::sqrt(worst);
    };
    return std::max(oneWay(a, b), oneWay(b, a));
}

} // namespace crucible::importers::testing
