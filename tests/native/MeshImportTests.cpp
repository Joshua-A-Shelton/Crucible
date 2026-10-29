// glTF mesh import (ADR 0043): mirroring into left-handed space on X (ADR
// 0024's amendment), winding, tangents, welding, LODs, and the MESH layout.

#include "MeshTestSupport.h"

#include <crucible/Memory.h>

using namespace crucible;
using namespace crucible::importers;
using namespace crucible::importers::testing;

namespace {

std::string base64(const std::vector<uint8_t>& bytes)
{
    static const char* digits = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
    std::string out;
    for (size_t i = 0; i < bytes.size(); i += 3)
    {
        const uint32_t chunk = (uint32_t(bytes[i]) << 16) | (i + 1 < bytes.size() ? uint32_t(bytes[i + 1]) << 8 : 0) |
                               (i + 2 < bytes.size() ? uint32_t(bytes[i + 2]) : 0);
        out += digits[(chunk >> 18) & 63];
        out += digits[(chunk >> 12) & 63];
        out += i + 1 < bytes.size() ? digits[(chunk >> 6) & 63] : '=';
        out += i + 2 < bytes.size() ? digits[chunk & 63] : '=';
    }
    return out;
}

// Builds small glTF files: arrays become accessors in one buffer.
struct Gltf
{
    std::vector<uint8_t> buffer;
    std::vector<std::string> accessors, views, primitives, meshes, nodes, materials, images, textures;

    int floats(const std::vector<float>& values, int components)
    {
        const size_t at = buffer.size();
        buffer.resize(at + values.size() * 4);
        std::memcpy(buffer.data() + at, values.data(), values.size() * 4);
        views.push_back("{\"buffer\":0,\"byteOffset\":" + std::to_string(at) +
                        ",\"byteLength\":" + std::to_string(values.size() * 4) + "}");
        const char* type = components == 2 ? "VEC2" : components == 3 ? "VEC3" : "VEC4";
        std::string extra;
        if (components == 3 && !values.empty())
        {
            float lo[3] = {values[0], values[1], values[2]}, hi[3] = {values[0], values[1], values[2]};
            for (size_t i = 0; i < values.size(); ++i)
            {
                lo[i % 3] = std::min(lo[i % 3], values[i]);
                hi[i % 3] = std::max(hi[i % 3], values[i]);
            }
            extra = ",\"min\":[" + std::to_string(lo[0]) + "," + std::to_string(lo[1]) + "," + std::to_string(lo[2]) +
                    "],\"max\":[" + std::to_string(hi[0]) + "," + std::to_string(hi[1]) + "," + std::to_string(hi[2]) +
                    "]";
        }
        accessors.push_back("{\"bufferView\":" + std::to_string(views.size() - 1) +
                            ",\"componentType\":5126,\"count\":" + std::to_string(values.size() / components) +
                            ",\"type\":\"" + type + "\"" + extra + "}");
        return int(accessors.size() - 1);
    }

    int indices(const std::vector<uint32_t>& values)
    {
        const size_t at = buffer.size();
        buffer.resize(at + values.size() * 4);
        std::memcpy(buffer.data() + at, values.data(), values.size() * 4);
        views.push_back("{\"buffer\":0,\"byteOffset\":" + std::to_string(at) +
                        ",\"byteLength\":" + std::to_string(values.size() * 4) + "}");
        accessors.push_back("{\"bufferView\":" + std::to_string(views.size() - 1) +
                            ",\"componentType\":5125,\"count\":" + std::to_string(values.size()) +
                            ",\"type\":\"SCALAR\"}");
        return int(accessors.size() - 1);
    }

    // A primitive from attribute accessors ("POSITION":0, ...).
    void primitive(const std::string& attributes, int indexAccessor = -1, int material = -1, int mode = 4)
    {
        std::string p = "{\"attributes\":{" + attributes + "},\"mode\":" + std::to_string(mode);
        if (indexAccessor >= 0)
        {
            p += ",\"indices\":" + std::to_string(indexAccessor);
        }
        if (material >= 0)
        {
            p += ",\"material\":" + std::to_string(material);
        }
        primitives.push_back(p + "}");
    }

    int mesh(const std::string& name)
    {
        std::string list;
        for (size_t i = 0; i < primitives.size(); ++i)
        {
            list += (i == 0 ? "" : ",") + primitives[i];
        }
        primitives.clear();
        meshes.push_back("{\"name\":\"" + name + "\",\"primitives\":[" + list + "]}");
        return int(meshes.size() - 1);
    }

    std::string json(const std::string& bufferUri = "") const
    {
        auto list = [](const std::vector<std::string>& items) {
            std::string out = "[";
            for (size_t i = 0; i < items.size(); ++i)
            {
                out += (i == 0 ? "" : ",") + items[i];
            }
            return out + "]";
        };
        const std::string uri =
            bufferUri.empty() ? "data:application/octet-stream;base64," + base64(buffer) : bufferUri;
        std::string out = "{\"asset\":{\"version\":\"2.0\"},\"buffers\":[{\"uri\":\"" + uri +
                          "\",\"byteLength\":" + std::to_string(buffer.size()) + "}],\"bufferViews\":" + list(views) +
                          ",\"accessors\":" + list(accessors) + ",\"meshes\":" + list(meshes);
        if (!nodes.empty())
        {
            out += ",\"nodes\":" + list(nodes);
        }
        if (!materials.empty())
        {
            out += ",\"materials\":" + list(materials);
        }
        if (!images.empty())
        {
            out += ",\"images\":" + list(images) + ",\"textures\":" + list(textures);
        }
        return out + "}";
    }
};

// A closed UV sphere in glTF's convention: counter-clockwise front faces,
// outward normals, UVs with v growing downwards.
void sphere(Gltf& gltf, int segments, int rings, bool withNormals = true)
{
    std::vector<float> positions, normals, uvs;
    for (int r = 0; r <= rings; ++r)
    {
        for (int s = 0; s <= segments; ++s)
        {
            const float v = float(r) / rings, u = float(s) / segments;
            const float theta = v * 3.14159265f, phi = u * 6.2831853f;
            // The poles exactly on the axis: sin(pi) in floats isn't quite 0,
            // which would make the last ring a tiny reversed circle.
            const float ring = r == 0 || r == rings ? 0.0f : std::sin(theta);
            const float x = ring * std::cos(phi),
                        y = r == 0       ? 1.0f
                            : r == rings ? -1.0f
                                         : std::cos(theta),
                        z = ring * std::sin(phi);
            positions.insert(positions.end(), {x, y, z});
            normals.insert(normals.end(), {x, y, z});
            uvs.insert(uvs.end(), {u, v});
        }
    }
    std::vector<uint32_t> indices;
    for (int r = 0; r < rings; ++r)
    {
        for (int s = 0; s < segments; ++s)
        {
            const uint32_t a = uint32_t(r * (segments + 1) + s), b = a + uint32_t(segments + 1);
            // Counter-clockwise seen from outside (right-handed).
            indices.insert(indices.end(), {a, a + 1, b, a + 1, b + 1, b});
        }
    }
    const int p = gltf.floats(positions, 3);
    const int n = withNormals ? gltf.floats(normals, 3) : -1;
    const int t = gltf.floats(uvs, 2);
    const int i = gltf.indices(indices);
    std::string attributes = "\"POSITION\":" + std::to_string(p) + ",\"TEXCOORD_0\":" + std::to_string(t);
    if (n >= 0)
    {
        attributes += ",\"NORMAL\":" + std::to_string(n);
    }
    gltf.primitive(attributes, i);
}

// A quad in glTF's XY plane facing +Z (the front of a glTF asset), from
// x = -1 to 1, with u growing towards +X and v towards -Y. Its tangents, when
// given, are what MikkTSpace makes for it: along +X, with the bitangent
// (w * cross(normal, tangent)) towards growing v, so w is -1.
void quad(Gltf& gltf, bool withTangents)
{
    const int p = gltf.floats({-1, -1, 0, 1, -1, 0, 1, 1, 0, -1, 1, 0}, 3);
    const int n = gltf.floats({0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1}, 3);
    const int t = gltf.floats({0, 1, 1, 1, 1, 0, 0, 0}, 2);
    std::string attributes = "\"POSITION\":" + std::to_string(p) + ",\"NORMAL\":" + std::to_string(n) +
                             ",\"TEXCOORD_0\":" + std::to_string(t);
    if (withTangents)
    {
        attributes +=
            ",\"TANGENT\":" + std::to_string(gltf.floats({1, 0, 0, -1, 1, 0, 0, -1, 1, 0, 0, -1, 1, 0, 0, -1}, 4));
    }
    gltf.primitive(attributes, gltf.indices({0, 1, 2, 0, 2, 3}));
}

} // namespace

TEST_CASE("Mirroring keeps an asset facing forward with its right side on +X")
{
    // glTF: the asset faces +Z, and its right is -X. One triangle on the
    // asset's front, its tip on the asset's right.
    Gltf gltf;
    const int p = gltf.floats({0, 0, 0, -1, 0, 0, 0, 1, 0}, 3); // tip at glTF -X
    const int n = gltf.floats({0, 0, 1, 0, 0, 1, 0, 0, 1}, 3);
    // Counter-clockwise seen from +Z: (0,0), (0,1), (-1,0).
    gltf.primitive("\"POSITION\":" + std::to_string(p) + ",\"NORMAL\":" + std::to_string(n), gltf.indices({0, 2, 1}));
    gltf.mesh("marker");
    MeshSettings plain = settings();
    plain.generateLods = false;
    const Imported imported(gltf.json(), plain);

    bool sawTip = false;
    for (uint32_t v = 0; v < 3; ++v)
    {
        const Vec3 position = imported.position(v);
        sawTip |= position[0] > 0.99f; // the asset's right, now on +X
        CHECK(position[0] >= -0.01f);
        const Vec3 normal = imported.normal(v);
        CHECK(normal[2] > 0.99f); // still facing +Z: Crucible's forward
    }
    CHECK(sawTip);
    // Front faces are clockwise: cross(B - A, C - A) points outward, along
    // the normal.
    for (const auto& t : imported.triangles(0, 0))
    {
        const Vec3 face = cross(minus(imported.position(t[1]), imported.position(t[0])),
                                minus(imported.position(t[2]), imported.position(t[0])));
        CHECK(face[2] > 0);
    }
}

TEST_CASE("A closed mesh has no inside-out faces at any level of detail")
{
    Gltf gltf;
    sphere(gltf, 48, 24);
    gltf.mesh("ball");
    const Imported imported(gltf.json());
    const MeshEntry entry = imported.table<MeshEntry>("MSHS")[0];
    REQUIRE(entry.lodCount >= 3);
    for (uint32_t level = 0; level < entry.lodCount; ++level)
    {
        CAPTURE(level);
        size_t inward = 0;
        for (const auto& t : imported.triangles(0, level))
        {
            const Vec3 a = imported.position(t[0]);
            const Vec3 face = cross(minus(imported.position(t[1]), a), minus(imported.position(t[2]), a));
            // Triangles meeting at a pole have two corners at one point: no
            // area and no orientation, so the sign of their cross product is
            // rounding noise. They stay in the mesh (skinning can open them up).
            if (std::sqrt(dot(face, face)) < 1e-6f)
            {
                continue;
            }
            inward += dot(face, a) < 0 ? 1 : 0; // outward from the centre, or not
        }
        CHECK(inward == 0);
    }
    // And vertex normals point outward too.
    for (uint32_t v = 0; v < entry.vertexCount; ++v)
    {
        CHECK(dot(imported.normal(v), imported.position(v)) > 0.9f);
    }
}

TEST_CASE("Levels of detail shrink with growing but bounded error")
{
    Gltf gltf;
    sphere(gltf, 64, 32);
    gltf.mesh("ball");
    const Imported imported(gltf.json());
    const MeshEntry entry = imported.table<MeshEntry>("MSHS")[0];
    const std::vector<MeshLod> lods = imported.table<MeshLod>("LODS");
    REQUIRE(entry.lodCount >= 3);
    size_t previous = imported.triangles(0, 0).size();
    for (uint32_t level = 1; level < entry.lodCount; ++level)
    {
        const size_t count = imported.triangles(0, level).size();
        MESSAGE("LOD " << level << ": " << count << " triangles, error " << lods[entry.lodOffset + level].error
                       << " m");
        CHECK(count < previous);
        CHECK(lods[entry.lodOffset + level].error >= lods[entry.lodOffset + level - 1].error);
        CHECK(lods[entry.lodOffset + level].error < 0.05f * 2.0f); // the cap: 5% of the 2 m ball
        for (const auto& t : imported.triangles(0, level))
        {
            for (uint32_t v : t)
            {
                CHECK(v < entry.vertexCount);
            }
        }
        previous = count;
    }
}

TEST_CASE("Node transforms mirror on X whether given as TRS or as a matrix")
{
    // Rotated 90 degrees about Y: glTF's +Z turns to +X.
    const float s = std::sqrt(0.5f);
    Gltf gltf;
    quad(gltf, false);
    gltf.mesh("quad");
    gltf.nodes.push_back("{\"name\":\"trs\",\"mesh\":0,\"translation\":[1,2,3],\"rotation\":[0," + std::to_string(s) +
                         ",0," + std::to_string(s) + "],\"children\":[1]}");
    // The same rotation as a column-major matrix, with a translation.
    gltf.nodes.push_back("{\"name\":\"matrix\",\"matrix\":[0,0,-1,0, 0,1,0,0, 1,0,0,0, 4,5,6,1]}");
    const Imported imported(gltf.json());
    const std::vector<MeshNode> nodes = imported.table<MeshNode>("NODE");
    REQUIRE(nodes.size() == 2);
    CHECK(nodes[0].parent == -1);
    CHECK(nodes[0].mesh == 0);
    CHECK(nodes[1].parent == 0);
    CHECK(nodes[1].mesh == -1);

    for (const MeshNode& node : nodes)
    {
        // Mirroring commutes with the rotation: rotating Crucible's +Z must
        // give the mirror of glTF's result, (+X mirrored) -X.
        const float qx = node.rotation[0], qy = node.rotation[1], qz = node.rotation[2], qw = node.rotation[3];
        const Vec3 v{0, 0, 1};
        const Vec3 q{qx, qy, qz};
        const Vec3 t = cross(q, v);
        const Vec3 rotated{v[0] + 2 * (qw * t[0] + cross(q, t)[0]), v[1] + 2 * (qw * t[1] + cross(q, t)[1]),
                           v[2] + 2 * (qw * t[2] + cross(q, t)[2])};
        CHECK(rotated[0] == doctest::Approx(-1.0f).epsilon(0.001));
        CHECK(std::fabs(rotated[2]) < 0.001f);
    }
    CHECK(nodes[0].translation[0] == -1.0f);
    CHECK(nodes[0].translation[1] == 2.0f);
    CHECK(nodes[1].translation[0] == -4.0f);
    CHECK(nodes[1].scale[0] == doctest::Approx(1.0f));
    const std::vector<uint8_t>& names = imported.sections.at(binary::tag("NAME"));
    CHECK(std::string_view(reinterpret_cast<const char*>(names.data()) + nodes[1].nameOffset, nodes[1].nameLength) ==
          "matrix");
}

TEST_CASE("Tangents mirror with their handedness and generated ones follow the UVs")
{
    for (bool fromSource : {true, false})
    {
        CAPTURE(fromSource);
        Gltf gltf;
        quad(gltf, fromSource);
        gltf.mesh("quad");
        const Imported imported(gltf.json());
        REQUIRE(imported.has("TNGT"));
        for (uint32_t v = 0; v < 4; ++v)
        {
            const auto [tangent, sign] = imported.tangent(v);
            // u grows along glTF +X, which mirrors to -X.
            CHECK(tangent[0] == doctest::Approx(-1.0f).epsilon(0.01));
            // The bitangent, as shaders rebuild it, must point where v grows:
            // glTF -Y, which mirroring leaves alone.
            const Vec3 bitangent = cross(imported.normal(v), tangent);
            CHECK(sign * bitangent[1] == doctest::Approx(-1.0f).epsilon(0.01));
        }
    }
}

TEST_CASE("Corners weld into shared vertices with 16-bit indices")
{
    // A quad as two triangles of three corners each: six in, four out.
    Gltf gltf;
    const int p = gltf.floats({0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 0, 0, 1, 1, 0, 0, 1, 0}, 3);
    gltf.primitive("\"POSITION\":" + std::to_string(p));
    gltf.mesh("unindexed");
    const Imported imported(gltf.json());
    const MeshEntry entry = imported.table<MeshEntry>("MSHS")[0];
    CHECK(entry.vertexCount == 4);
    CHECK(entry.indexSize == 2);
    CHECK((entry.attributes & uint32_t(MeshAttribute::Normals)) != 0); // flat ones, generated
    for (uint32_t v = 0; v < 4; ++v)
    {
        CHECK(imported.normal(v)[2] == doctest::Approx(1.0f).epsilon(0.001)); // the face's own side
    }
    CHECK(!imported.has("TNGT")); // no UVs, so nothing to make tangents from
    CHECK(!imported.has("UV0_"));
}

TEST_CASE("Primitives become submeshes with their materials and textures")
{
    Gltf gltf;
    quad(gltf, false);
    gltf.primitives.back().insert(gltf.primitives.back().size() - 1, ",\"material\":0");
    sphere(gltf, 8, 4);
    gltf.primitives.back().insert(gltf.primitives.back().size() - 1, ",\"material\":1");
    gltf.mesh("two");
    gltf.materials.push_back(
        "{\"name\":\"brick\",\"pbrMetallicRoughness\":{\"baseColorFactor\":[1,0.5,0.25,1],"
        "\"baseColorTexture\":{\"index\":0},\"roughnessFactor\":0.5},\"normalTexture\":{\"index\":1},"
        "\"alphaMode\":\"MASK\",\"alphaCutoff\":0.3,\"doubleSided\":true}");
    gltf.materials.push_back("{\"name\":\"plain\"}");
    gltf.images.push_back("{\"uri\":\"textures/brick%20red.png\"}");
    gltf.images.push_back("{\"uri\":\"data:image/png;base64,AAAA\"}"); // embedded
    gltf.textures.push_back("{\"source\":0}");
    gltf.textures.push_back("{\"source\":1}");
    const Imported imported(gltf.json());

    const MeshEntry entry = imported.table<MeshEntry>("MSHS")[0];
    CHECK(entry.submeshCount == 2);
    const std::vector<MeshRange> ranges = imported.table<MeshRange>("RNGS");
    CHECK(ranges[0].material == 0);
    CHECK(ranges[1].material == 1);

    REQUIRE(meshMaterialCount(*imported.mesh) == 2);
    const MaterialEntry brick = meshMaterial(*imported.mesh, 0);
    CHECK(brick.baseColor[1] == 0.5f);
    CHECK(brick.roughness == 0.5f);
    CHECK(brick.metallic == 1.0f); // glTF's default
    CHECK(brick.alphaMode == 1);
    CHECK(brick.alphaCutoff == doctest::Approx(0.3f));
    CHECK(brick.doubleSided == 1);
    CHECK(meshMaterialTexture(*imported.mesh, 0, TextureRole::BaseColor) == "textures/brick red.png");
    CHECK(meshMaterialTexture(*imported.mesh, 0, TextureRole::Normal).empty()); // embedded: not yet
    CHECK(meshMaterialTexture(*imported.mesh, 1, TextureRole::BaseColor).empty());
    REQUIRE(meshWarningCount(*imported.mesh) == 1);
    CHECK(std::string(meshWarning(*imported.mesh, 0)).find("embedded") != std::string::npos);
}

TEST_CASE("Large UVs are kept as floats")
{
    Gltf gltf;
    const int p = gltf.floats({0, 0, 0, 1, 0, 0, 0, 1, 0}, 3);
    const int t = gltf.floats({0, 0, 40.25f, 0, 0, 40.5f}, 2); // a tiled terrain's
    gltf.primitive("\"POSITION\":" + std::to_string(p) + ",\"TEXCOORD_0\":" + std::to_string(t));
    gltf.mesh("terrain");
    const Imported imported(gltf.json());
    const MeshFileInfo info = imported.table<MeshFileInfo>("INFO")[0];
    CHECK((info.floatUvs & 1u) != 0);
    const std::vector<float> uvs = imported.table<float>("UV0_");
    REQUIRE(uvs.size() == 6);
    bool exact = false;
    for (float value : uvs)
    {
        exact |= value == 40.25f; // a half would round it to 40.25 too; 40.5 checks the stream really is floats
    }
    CHECK(exact);
}

TEST_CASE("Buffers in other files are asked for")
{
    Gltf gltf;
    quad(gltf, false);
    gltf.mesh("quad");
    const std::string json = gltf.json("my%20mesh.bin");
    std::vector<uint8_t> bytes(json.begin(), json.end());
    Result<GltfSource*> source = parseGltf(bytes);
    REQUIRE(source);
    REQUIRE(gltfBufferCount(*source.value()) == 1);
    CHECK(gltfBufferUri(*source.value(), 0) == "my mesh.bin");
    CHECK(importGltf(*source.value(), settings()).status() == Status::InvalidData);
    CHECK(std::string(lastImportError()).find("my mesh.bin wasn't provided") != std::string::npos);
    CHECK(setGltfBuffer(*source.value(), 0, std::span(gltf.buffer).first(4)).status() == Status::InvalidData);
    REQUIRE(setGltfBuffer(*source.value(), 0, gltf.buffer));
    Result<MeshImport*> mesh = importGltf(*source.value(), settings());
    CHECK(mesh);
    destroyMeshImport(mesh.value());
    destroyGltfSource(source.value());
}

TEST_CASE("Lines and points are left out with a warning")
{
    Gltf gltf;
    quad(gltf, false);
    const int p = gltf.floats({0, 0, 0, 1, 1, 1}, 3);
    gltf.primitive("\"POSITION\":" + std::to_string(p), -1, -1, 1); // lines
    gltf.mesh("mixed");
    const Imported imported(gltf.json());
    CHECK(imported.table<MeshEntry>("MSHS")[0].submeshCount == 1);
    REQUIRE(meshWarningCount(*imported.mesh) == 1);
    CHECK(std::string(meshWarning(*imported.mesh, 0)).find("isn't triangles") != std::string::npos);
}

TEST_CASE("Files that aren't glTF are refused")
{
    const std::vector<uint8_t> garbage = {'n', 'o', 'p', 'e'};
    CHECK(parseGltf(garbage).status() == Status::InvalidData);
    CHECK(std::string(lastImportError()).find("can't read the glTF") != std::string::npos);
}

TEST_CASE("Mesh import returns its memory")
{
    const MemoryUsage before = memoryUsage(MemoryTag::Assets);
    {
        Gltf gltf;
        sphere(gltf, 16, 8);
        gltf.mesh("ball");
        const Imported imported(gltf.json());
    }
    const MemoryUsage after = memoryUsage(MemoryTag::Assets);
    CHECK(after.bytes == before.bytes);
    CHECK(after.allocations == before.allocations);
}
