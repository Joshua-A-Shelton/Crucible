// FBX mesh import (ADR 0044): ufbx's sample files, converted to glTF's
// convention and then mirrored like glTF.

#include "MeshTestSupport.h"

#include <crucible/Memory.h>

#include <algorithm>
#include <fstream>
#include <iterator>
#include <sstream>

using namespace crucible;
using namespace crucible::importers;
using namespace crucible::importers::testing;

namespace {

std::vector<uint8_t> readData(const std::string& name)
{
    std::ifstream file(std::string(CRUCIBLE_UFBX_DATA_DIR) + "/" + name, std::ios::binary);
    REQUIRE(file.good());
    return {std::istreambuf_iterator<char>(file), std::istreambuf_iterator<char>()};
}

Result<MeshImport*> importData(const std::string& name, MeshSettings options = settings())
{
    const std::vector<uint8_t> bytes = readData(name);
    return importFbx(bytes, options);
}

Vec3 rotate(const float* q, const Vec3& v)
{
    const Vec3 axis{q[0], q[1], q[2]};
    const Vec3 t = cross(axis, v);
    const Vec3 u = cross(axis, t);
    return {v[0] + 2 * (q[3] * t[0] + u[0]), v[1] + 2 * (q[3] * t[1] + u[1]), v[2] + 2 * (q[3] * t[2] + u[2])};
}

// A point in a node's space, taken to world space through its parents.
Vec3 toWorld(const std::vector<MeshNode>& nodes, int32_t node, Vec3 p)
{
    for (; node >= 0; node = nodes[node].parent)
    {
        const MeshNode& n = nodes[node];
        p = rotate(n.rotation, {p[0] * n.scale[0], p[1] * n.scale[1], p[2] * n.scale[2]});
        p = {p[0] + n.translation[0], p[1] + n.translation[1], p[2] + n.translation[2]};
    }
    return p;
}

// A node's mesh's vertices, in world space; every placed mesh's if `node`
// is -1.
std::vector<Vec3> worldVertices(const Imported& imported, int32_t only = -1)
{
    const std::vector<MeshNode> nodes = imported.table<MeshNode>("NODE");
    const std::vector<MeshEntry> meshes = imported.table<MeshEntry>("MSHS");
    std::vector<Vec3> result;
    for (int32_t n = 0; n < static_cast<int32_t>(nodes.size()); ++n)
    {
        if (nodes[n].mesh < 0 || (only >= 0 && n != only))
        {
            continue;
        }
        const MeshEntry& entry = meshes[nodes[n].mesh];
        for (uint32_t v = 0; v < entry.vertexCount; ++v)
        {
            result.push_back(toWorld(nodes, n, imported.position(entry.vertexOffset + v)));
        }
    }
    return result;
}

// Whether every point of one set is within `tolerance` of a point of the
// other, both ways.
bool samePoints(const std::vector<Vec3>& a, const std::vector<Vec3>& b, float tolerance)
{
    auto covered = [&](const std::vector<Vec3>& from, const std::vector<Vec3>& to) {
        for (const Vec3& p : from)
        {
            bool found = false;
            for (const Vec3& q : to)
            {
                const Vec3 d = minus(p, q);
                found |= dot(d, d) <= tolerance * tolerance;
            }
            if (!found)
            {
                return false;
            }
        }
        return true;
    };
    return !a.empty() && !b.empty() && covered(a, b) && covered(b, a);
}

int32_t nodeNamed(const Imported& imported, std::string_view name)
{
    const std::vector<MeshNode> nodes = imported.table<MeshNode>("NODE");
    for (int32_t n = 0; n < static_cast<int32_t>(nodes.size()); ++n)
    {
        if (imported.name(nodes[n].nameOffset, nodes[n].nameLength) == name)
        {
            return n;
        }
    }
    return -1;
}

std::pair<Vec3, Vec3> bounds(const std::vector<Vec3>& points)
{
    Vec3 lo{1e30f, 1e30f, 1e30f}, hi{-1e30f, -1e30f, -1e30f};
    for (const Vec3& p : points)
    {
        for (int c = 0; c < 3; ++c)
        {
            lo[c] = std::min(lo[c], p[c]);
            hi[c] = std::max(hi[c], p[c]);
        }
    }
    return {lo, hi};
}

// An OBJ's vertex positions (the `v` lines), for files ufbx ships with a
// reference export beside the FBX.
std::vector<Vec3> objVertices(const std::string& name)
{
    const std::vector<uint8_t> bytes = readData(name);
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

bool hasWarning(const Imported& imported, std::string_view text)
{
    for (uint32_t w = 0; w < meshWarningCount(*imported.mesh); ++w)
    {
        if (meshWarning(*imported.mesh, w).find(text) != std::string_view::npos)
        {
            return true;
        }
    }
    return false;
}

} // namespace

TEST_CASE("FBX units and axes become meters with Y up")
{
    // Maya works in centimeters: its default 1-unit cube is 1 cm across.
    {
        const Imported imported(importData("maya_cube_7500_binary.fbx"));
        const auto [lo, hi] = bounds(worldVertices(imported));
        for (int c = 0; c < 3; ++c)
        {
            CHECK(lo[c] == doctest::Approx(-0.005f));
            CHECK(hi[c] == doctest::Approx(0.005f));
        }
    }
    // A Z-up Blender export, against Blender's Y-up OBJ export of the same
    // cube, mirrored on X into Crucible's space.
    {
        const Imported imported(importData("blender_340_z_up_7400_binary.fbx"));
        const int32_t cube = nodeNamed(imported, "Cube");
        REQUIRE(cube >= 0);
        std::vector<Vec3> expected = objVertices("blender_340_z_up.obj");
        for (Vec3& p : expected)
        {
            p[0] = -p[0];
        }
        CHECK(samePoints(worldVertices(imported, cube), expected, 1e-4f));
    }
}

TEST_CASE("Geometry transforms are baked into the vertices")
{
    // 3ds Max: Z up, inches, and boxes moved and scaled by geometry
    // transforms, one mesh shared by several nodes in the second file.
    // Max's own OBJ exports are the reference: (x, y, z) in Max becomes
    // (x, z, -y) in glTF's space, then (-x, z, -y) in Crucible's.
    for (const char* name : {"max_geometry_transform", "max_geometry_transform_instances"})
    {
        CAPTURE(name);
        const Imported imported(importData(std::string(name) + "_7700_binary.fbx"));
        std::vector<Vec3> expected;
        for (const Vec3& p : objVertices(std::string(name) + ".obj"))
        {
            expected.push_back({-p[0] * 0.0254f, p[2] * 0.0254f, -p[1] * 0.0254f});
        }
        CHECK(samePoints(worldVertices(imported), expected, 1e-4f));
    }
}

TEST_CASE("Polygons become outward-facing triangles with a submesh per material")
{
    const Imported imported(importData("blender_suzanne_multimaterial_7400_binary.fbx"));
    const MeshEntry entry = imported.table<MeshEntry>("MSHS")[0];
    REQUIRE(meshMaterialCount(*imported.mesh) == 7);
    CHECK(entry.submeshCount == 7);
    const std::vector<MeshRange> ranges = imported.table<MeshRange>("RNGS");
    const MeshLod lod0 = imported.table<MeshLod>("LODS")[entry.lodOffset];
    std::vector<bool> used(7, false);
    for (uint32_t s = 0; s < entry.submeshCount; ++s)
    {
        const MeshRange range = ranges[lod0.rangeOffset + s];
        REQUIRE(range.material >= 0);
        REQUIRE(range.material < 7);
        CHECK(!used[range.material]);
        used[range.material] = true;
    }
    const MaterialEntry first = meshMaterial(*imported.mesh, 0);
    CHECK(imported.name(first.nameOffset, first.nameLength) == "LeftEye");

    // Front faces are clockwise and agree with the source's smooth normals.
    size_t facing = 0, total = 0;
    for (const auto& t : imported.triangles(0, 0))
    {
        const Vec3 a = imported.position(t[0]);
        const Vec3 face = cross(minus(imported.position(t[1]), a), minus(imported.position(t[2]), a));
        if (std::sqrt(dot(face, face)) < 1e-8f)
        {
            continue;
        }
        const Vec3 n0 = imported.normal(t[0]), n1 = imported.normal(t[1]), n2 = imported.normal(t[2]);
        const Vec3 normal{n0[0] + n1[0] + n2[0], n0[1] + n1[1] + n2[1], n0[2] + n1[2] + n2[2]};
        facing += dot(face, normal) > 0 ? 1 : 0;
        ++total;
    }
    CHECK(total > 900);
    CHECK(facing == total);

    // A closed convex cube: every face points away from its centre.
    const Imported cube(importData("maya_cube_7500_binary.fbx"));
    for (const auto& t : cube.triangles(0, 0))
    {
        const Vec3 a = cube.position(t[0]);
        const Vec3 face = cross(minus(cube.position(t[1]), a), minus(cube.position(t[2]), a));
        CHECK(dot(face, a) > 0);
    }
}

TEST_CASE("Source tangents keep their handedness through the V flip")
{
    // Blender's tangents for two UV sets; the first set's are imported.
    const Imported imported(importData("blender_279_uv_set_tangents_7400_binary.fbx"));
    const MeshEntry entry = imported.table<MeshEntry>("MSHS")[0];
    REQUIRE((entry.attributes & static_cast<uint32_t>(MeshAttribute::Tangents)) != 0);
    REQUIRE((entry.attributes & static_cast<uint32_t>(MeshAttribute::Uv1)) != 0);
    const auto triangles = imported.triangles(0, 0);
    REQUIRE(!triangles.empty());
    for (const auto& t : triangles)
    {
        // The directions u and v grow along this triangle, from its UVs.
        const Vec3 e1 = minus(imported.position(t[1]), imported.position(t[0]));
        const Vec3 e2 = minus(imported.position(t[2]), imported.position(t[0]));
        const auto uv0 = imported.uv(0, t[0]), uv1 = imported.uv(0, t[1]), uv2 = imported.uv(0, t[2]);
        const float du1 = uv1[0] - uv0[0], dv1 = uv1[1] - uv0[1], du2 = uv2[0] - uv0[0], dv2 = uv2[1] - uv0[1];
        const float r = 1.0f / (du1 * dv2 - du2 * dv1);
        const Vec3 alongU{(e1[0] * dv2 - e2[0] * dv1) * r, (e1[1] * dv2 - e2[1] * dv1) * r,
                          (e1[2] * dv2 - e2[2] * dv1) * r};
        const Vec3 alongV{(e2[0] * du1 - e1[0] * du2) * r, (e2[1] * du1 - e1[1] * du2) * r,
                          (e2[2] * du1 - e1[2] * du2) * r};
        for (uint32_t v : t)
        {
            const auto [tangent, sign] = imported.tangent(v);
            CHECK(dot(tangent, alongU) > 0);
            // The bitangent as a shader rebuilds it points where v grows.
            const Vec3 bitangent = cross(imported.normal(v), tangent);
            CHECK(sign * dot(bitangent, alongV) > 0);
        }
    }
}

TEST_CASE("Texture paths are kept as the FBX stores them")
{
    const Imported imported(importData("blender_293_textures_7400_binary.fbx"));
    REQUIRE(meshMaterialCount(*imported.mesh) == 1);
    CHECK(meshMaterialTexture(*imported.mesh, 0, TextureRole::BaseColor) == "textures\\checkerboard_diffuse.png");
    CHECK(meshMaterialTexture(*imported.mesh, 0, TextureRole::Emissive) == "textures\\checkerboard_emissive.png");
    CHECK(meshMaterialTexture(*imported.mesh, 0, TextureRole::MetallicRoughness).empty());
    CHECK(!hasWarning(imported, "embedded"));

    const Imported embedded(importData("blender_293_embedded_textures_7400_binary.fbx"));
    CHECK(hasWarning(embedded, "checkerboard_diffuse.png is embedded in the FBX"));
    CHECK(!meshMaterialTexture(*embedded.mesh, 0, TextureRole::BaseColor).empty());
}

TEST_CASE("Lambert materials are fully rough")
{
    const Imported imported(importData("maya_cube_7500_binary.fbx"));
    REQUIRE(meshMaterialCount(*imported.mesh) == 1);
    const MaterialEntry lambert = meshMaterial(*imported.mesh, 0);
    CHECK(lambert.roughness == 1.0f);
    CHECK(lambert.metallic == 0.0f);
    CHECK(lambert.baseColor[0] == doctest::Approx(0.4f));
}

TEST_CASE("Vertex colours follow their corners")
{
    // Blender's "RGB cube": each corner of the cube is coloured by where it
    // is, one of the eight pure colours, on every face that meets there.
    const Imported imported(importData("blender_279_color_sets_7400_binary.fbx"));
    const MeshEntry entry = imported.table<MeshEntry>("MSHS")[0];
    REQUIRE((entry.attributes & static_cast<uint32_t>(MeshAttribute::Colors)) != 0);
    const std::vector<uint8_t>& colors = imported.sections.at(binary::tag("COLR"));
    std::map<std::array<int, 3>, std::array<uint8_t, 4>> byCorner;
    std::map<std::array<uint8_t, 4>, int> seen;
    for (uint32_t v = 0; v < entry.vertexCount; ++v)
    {
        const Vec3 p = imported.position(entry.vertexOffset + v);
        const std::array<int, 3> corner{p[0] > 0, p[1] > 0, p[2] > 0};
        std::array<uint8_t, 4> color;
        std::memcpy(color.data(), colors.data() + (entry.vertexOffset + v) * 4, 4);
        for (uint8_t channel : color)
        {
            CHECK((channel == 0 || channel == 255));
        }
        CHECK(color[3] == 255);
        const auto [at, added] = byCorner.emplace(corner, color);
        CHECK(at->second == color);
        ++seen[color];
    }
    CHECK(byCorner.size() == 8);
    CHECK(seen.size() == 8);
}

TEST_CASE("Files that aren't FBX are refused")
{
    const std::vector<uint8_t> garbage = {'n', 'o', 'p', 'e'};
    CHECK(importFbx(garbage, settings()).status() == Status::InvalidData);
    CHECK(std::string(lastImportError()).find("can't read the FBX") != std::string::npos);
}

TEST_CASE("FBX import returns its memory")
{
    const std::vector<uint8_t> bytes = readData("blender_suzanne_multimaterial_7400_binary.fbx");
    const MemoryUsage before = memoryUsage(MemoryTag::Assets);
    {
        const Imported imported(importFbx(bytes, settings()));
    }
    const MemoryUsage after = memoryUsage(MemoryTag::Assets);
    CHECK(after.bytes == before.bytes);
    CHECK(after.allocations == before.allocations);
}
