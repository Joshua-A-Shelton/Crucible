#pragma once

// What the mesh import tests share (ADR 0043, 0044): reading an import's
// sections back as a shader or the engine would.

#include <crucible/BinaryFile.h>
#include <crucible/importers/Mesh.h>
#include <crucible/importers/Texture.h>

#include <doctest/doctest.h>

#include <array>
#include <cmath>
#include <cstring>
#include <map>
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

} // namespace crucible::importers::testing
