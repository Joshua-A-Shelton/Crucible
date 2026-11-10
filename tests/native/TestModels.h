#pragma once

// Models and textures written in memory for the renderer's GPU tests
// (DrawListTests.cpp, MaterialTests.cpp): what the importers would write,
// down to the generated materials (ADR 0052).

#include "RenderInternal.h"

#include <crucible/BinaryFile.h>
#include <crucible/Ecs.h>
#include <crucible/Lights.h>
#include <crucible/MaterialFile.h>
#include <crucible/Memory.h>
#include <crucible/MeshFile.h>
#include <crucible/MeshInstance.h>
#include <crucible/Render.h>
#include <crucible/RenderAssets.h>
#include <crucible/Shader.h>
#include <crucible/TextureFile.h>
#include <crucible/Transform.h>
#include <crucible/Vector.h>

#include "default.g.h" // DefaultMaterial

#include <doctest/doctest.h>

#include <algorithm>
#include <cmath>
#include <cstring>
#include <fstream>
#include <iterator>
#include <map>
#include <string>
#include <string_view>
#include <vector>

namespace crucible::test {

inline std::vector<uint8_t> fileBytes(binary::Writer* writer)
{
    Vector<std::byte> bytes(MemoryTag::General);
    REQUIRE(binary::writeToMemory(*writer, binary::Compression::Fast, bytes));
    binary::destroyWriter(writer);
    const auto* data = reinterpret_cast<const uint8_t*>(bytes.data());
    return {data, data + bytes.size()};
}

template <class T>
void addSection(binary::Writer* writer, uint32_t tag, const std::vector<T>& values)
{
    REQUIRE(binary::addSection(*writer, tag, 1,
                               {reinterpret_cast<const uint8_t*>(values.data()), values.size() * sizeof(T)}, 16));
}

inline assets::AssetId idOf(uint8_t first)
{
    assets::AssetId id{};
    id.bytes[0] = first;
    id.bytes[15] = 0x5A;
    return id;
}

struct QuadOptions
{
    float baseColor[4] = {1, 1, 1, 1};
    const assets::AssetId* texture = nullptr; // base color
    assets::DefaultMaterialType type = assets::DefaultMaterialType::Toon;
    assets::AlphaMode alphaMode = assets::AlphaMode::Opaque;
    float alphaCutoff = 0.5f;
    bool doubleSided = false;
    const assets::AssetId* remap = nullptr; // an authored material for the slot (MRMP)
    float roughness = 0.5f;
    float outlineWidth = 0; // pixels
    float outlineColor[3] = {0, 0, 0};
    float outlineTint = 0;
    // A skin (ADR 0078): two joints, "Root" and "Arm" (Arm's child), both
    // bound at the origin; vertices with x > 0 follow Arm, the rest Root.
    // Arm's node rests at armRest, so posing the rest pose moves them by it.
    bool skinned = false;
    Vector3 armRest{0, 0, 0};
};

// A model of one mesh from its streams (normals and half-float UVs), with
// one generated material as the mesh importer writes it.
inline std::vector<uint8_t> meshModel(const std::vector<float>& positions, const std::vector<int16_t>& normals,
                                      const std::vector<uint16_t>& uvs, const std::vector<uint16_t>& indices,
                                      const float (&boundsMin)[3], const float (&boundsMax)[3],
                                      const QuadOptions& options)
{
    const auto vertexCount = static_cast<uint32_t>(positions.size() / 3);
    binary::Writer* writer = binary::createWriter(assets::meshFileKind, assets::meshFileVersion);
    addSection(writer, binary::tag("INFO"), std::vector<assets::MeshFileInfo>{{1, 1, 1, vertexCount, 1 | 4, 0}});
    assets::MeshEntry entry{};
    entry.vertexCount = vertexCount;
    entry.indexSize = 2;
    entry.attributes = 1 | 4 | (options.skinned ? 64 : 0); // normals, UV0, skinned
    entry.lodCount = 1;
    entry.submeshCount = 1;
    std::memcpy(entry.boundsMin, boundsMin, sizeof(boundsMin));
    std::memcpy(entry.boundsMax, boundsMax, sizeof(boundsMax));
    addSection(writer, binary::tag("MSHS"), std::vector<assets::MeshEntry>{entry});
    addSection(writer, binary::tag("LODS"), std::vector<assets::MeshLod>{{0, 0.0f}});
    addSection(writer, binary::tag("RNGS"),
               std::vector<assets::MeshRange>{{0, static_cast<uint32_t>(indices.size()), 0}});
    addSection(writer, binary::tag("POSN"), positions);
    addSection(writer, binary::tag("NRML"), normals);
    addSection(writer, binary::tag("UV0_"), uvs);
    addSection(writer, binary::tag("INDX"), indices);
    assets::MeshNode node{};
    node.parent = -1;
    node.mesh = 0;
    node.rotation[3] = 1;
    node.scale[0] = node.scale[1] = node.scale[2] = 1;
    std::vector<assets::MeshNode> nodes{node};
    if (options.skinned)
    {
        // Nodes 1 (Root) and 2 (Arm), named in NAME ("RootArm").
        assets::MeshNode root = node, arm = node;
        root.mesh = arm.mesh = -1;
        root.nameOffset = 0;
        root.nameLength = 4;
        arm.parent = 1;
        arm.translation[0] = options.armRest.x;
        arm.translation[1] = options.armRest.y;
        arm.translation[2] = options.armRest.z;
        arm.nameOffset = 4;
        arm.nameLength = 3;
        nodes.push_back(root);
        nodes.push_back(arm);
        assets::JointEntry joints[2]{};
        joints[0] = {-1, 1, {}, 0, 4};
        joints[1] = {0, 2, {}, 4, 3};
        for (assets::JointEntry& joint : joints)
        {
            for (int i = 0; i < 4; ++i)
            {
                joint.inverseBind[i * 5] = 1;
            }
        }
        addSection(writer, binary::tag("JNTS"), std::vector<assets::JointEntry>(joints, joints + 2));
        addSection(writer, binary::tag("SKIN"), std::vector<assets::SkinInfo>{{2, 4}});
        addSection(writer, binary::tag("NAME"), std::vector<char>{'R', 'o', 'o', 't', 'A', 'r', 'm', 0});
        std::vector<uint16_t> jointIndices(size_t{vertexCount} * 4, 0);
        std::vector<uint8_t> weights(size_t{vertexCount} * 4, 0);
        for (uint32_t v = 0; v < vertexCount; ++v)
        {
            jointIndices[v * 4] = positions[v * 3] > 0 ? 1 : 0;
            weights[v * 4] = 255;
        }
        addSection(writer, binary::tag("JIDX"), jointIndices);
        addSection(writer, binary::tag("JWGT"), weights);
    }
    addSection(writer, binary::tag("NODE"), nodes);
    assets::MaterialEntry imported{};
    std::memcpy(imported.baseColor, options.baseColor, sizeof(options.baseColor));
    addSection(writer, binary::tag("MATL"), std::vector<assets::MaterialEntry>{imported});

    // The generated material, as the mesh importer writes it.
    shaders::DefaultMaterial parameters{};
    parameters.baseColor = {options.baseColor[0], options.baseColor[1], options.baseColor[2], options.baseColor[3]};
    parameters.roughness = options.roughness;
    parameters.normalScale = 1;
    parameters.occlusionStrength = 1;
    assets::MaterialInfo info{};
    info.type = static_cast<uint32_t>(options.type);
    info.alphaMode = options.alphaMode;
    info.doubleSided = options.doubleSided ? 1 : 0;
    info.alphaCutoff = options.alphaCutoff;
    info.outlineWidth = options.outlineWidth;
    std::memcpy(info.outlineColor, options.outlineColor, sizeof(info.outlineColor));
    info.outlineTint = options.outlineTint;
    info.parameterOffset = sizeof(info);
    info.parameterSize = sizeof(parameters);
    info.textureOffset = (sizeof(info) + sizeof(parameters) + 3) / 4 * 4;
    info.textureCount = options.texture != nullptr ? 1 : 0;
    std::vector<uint8_t> section(info.textureOffset + info.textureCount * sizeof(assets::MaterialTexture));
    std::memcpy(section.data(), &info, sizeof(info));
    std::memcpy(section.data() + info.parameterOffset, &parameters, sizeof(parameters));
    if (options.texture != nullptr)
    {
        assets::MaterialTexture texture{};
        texture.offset = offsetof(shaders::DefaultMaterial, baseColorTexture);
        std::memcpy(texture.texture, options.texture->bytes, 16);
        std::memcpy(section.data() + info.textureOffset, &texture, sizeof(texture));
    }
    addSection(writer, assets::meshMaterialsTag, section);
    if (options.remap != nullptr)
    {
        addSection(writer, assets::meshRemappedMaterialsTag,
                   std::vector<uint8_t>(options.remap->bytes, options.remap->bytes + 16));
    }
    return fileBytes(writer);
}

// A unit quad in the XY plane facing -Z (clockwise from a camera looking
// along +Z), with normals, half-float UVs (u left to right, v top to
// bottom), and one generated material.
inline std::vector<uint8_t> quadModel(const QuadOptions& options = {})
{
    const float boundsMin[3] = {-0.5f, -0.5f, 0}, boundsMax[3] = {0.5f, 0.5f, 0};
    // (0, 0, -1), octahedral: the fold puts it at the square's corner.
    // Half floats: 0 and 1 (0x3c00).
    return meshModel({-0.5f, -0.5f, 0, -0.5f, 0.5f, 0, 0.5f, 0.5f, 0, 0.5f, -0.5f, 0}, std::vector<int16_t>(8, 32767),
                     {0, 0x3c00, 0, 0, 0x3c00, 0, 0x3c00, 0x3c00}, {0, 1, 2, 0, 2, 3}, boundsMin, boundsMax, options);
}

// A float as a half float (normal numbers and zero; enough for UVs).
inline uint16_t toHalf(float value)
{
    uint32_t bits;
    std::memcpy(&bits, &value, 4);
    const uint32_t sign = (bits >> 16) & 0x8000;
    const int exponent = static_cast<int>((bits >> 23) & 0xff) - 127 + 15;
    if (exponent <= 0)
    {
        return static_cast<uint16_t>(sign);
    }
    return static_cast<uint16_t>(sign | (static_cast<uint32_t>(exponent) << 10) | ((bits >> 13) & 0x3ff));
}

// A sphere of radius 0.5 (24 rings of 48 segments), with smooth normals and
// UVs: u around it (0.75 faces -Z), v from top to bottom.
inline std::vector<uint8_t> sphereModel(const QuadOptions& options = {})
{
    constexpr int rings = 24, segments = 48;
    std::vector<float> positions;
    std::vector<int16_t> normals;
    std::vector<uint16_t> uvs, indices;
    for (int r = 0; r <= rings; ++r)
    {
        const float theta = 3.14159265f * static_cast<float>(r) / rings;
        for (int s = 0; s <= segments; ++s)
        {
            const float phi = 6.2831853f * static_cast<float>(s) / segments;
            const float n[3] = {std::sin(theta) * std::cos(phi), std::cos(theta), std::sin(theta) * std::sin(phi)};
            for (float c : n)
            {
                positions.push_back(c * 0.5f);
            }
            // Octahedral, as the importer encodes it.
            float x = n[0], y = n[1];
            const float sum = std::fabs(n[0]) + std::fabs(n[1]) + std::fabs(n[2]);
            x /= sum;
            y /= sum;
            if (n[2] < 0)
            {
                const float fx = (1 - std::fabs(y)) * (x >= 0 ? 1.0f : -1.0f);
                const float fy = (1 - std::fabs(x)) * (y >= 0 ? 1.0f : -1.0f);
                x = fx;
                y = fy;
            }
            normals.push_back(static_cast<int16_t>(std::lround(x * 32767)));
            normals.push_back(static_cast<int16_t>(std::lround(y * 32767)));
            uvs.push_back(toHalf(static_cast<float>(s) / segments));
            uvs.push_back(toHalf(static_cast<float>(r) / rings));
        }
    }
    auto vertex = [&](int r, int s) {
        return static_cast<uint16_t>(r * (segments + 1) + s);
    };
    auto triangle = [&](uint16_t a, uint16_t b, uint16_t c) {
        // Clockwise from outside: (b - a) x (c - a) points outward here.
        const float* pa = &positions[a * 3];
        const float* pb = &positions[b * 3];
        const float* pc = &positions[c * 3];
        const float u[3] = {pb[0] - pa[0], pb[1] - pa[1], pb[2] - pa[2]};
        const float v[3] = {pc[0] - pa[0], pc[1] - pa[1], pc[2] - pa[2]};
        const float cross[3] = {u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0]};
        const float outward = cross[0] * (pa[0] + pb[0] + pc[0]) + cross[1] * (pa[1] + pb[1] + pc[1]) +
                              cross[2] * (pa[2] + pb[2] + pc[2]);
        if (std::fabs(outward) < 1e-9f)
        {
            return; // degenerate, at a pole
        }
        indices.insert(indices.end(), {a, outward > 0 ? b : c, outward > 0 ? c : b});
    };
    for (int r = 0; r < rings; ++r)
    {
        for (int s = 0; s < segments; ++s)
        {
            triangle(vertex(r, s), vertex(r + 1, s), vertex(r + 1, s + 1));
            triangle(vertex(r, s), vertex(r + 1, s + 1), vertex(r, s + 1));
        }
    }
    const float boundsMin[3] = {-0.5f, -0.5f, -0.5f}, boundsMax[3] = {0.5f, 0.5f, 0.5f};
    return meshModel(positions, normals, uvs, indices, boundsMin, boundsMax, options);
}

// Octahedral, as the importer encodes normals.
inline void octahedralNormal(const float (&n)[3], std::vector<int16_t>& out)
{
    float x = n[0], y = n[1];
    const float sum = std::fabs(n[0]) + std::fabs(n[1]) + std::fabs(n[2]);
    x /= sum;
    y /= sum;
    if (n[2] < 0)
    {
        const float fx = (1 - std::fabs(y)) * (x >= 0 ? 1.0f : -1.0f);
        const float fy = (1 - std::fabs(x)) * (y >= 0 ? 1.0f : -1.0f);
        x = fx;
        y = fy;
    }
    out.push_back(static_cast<int16_t>(std::lround(x * 32767)));
    out.push_back(static_cast<int16_t>(std::lround(y * 32767)));
}

// A unit cube (24 vertices, 4 per face). With `smoothNormals`, each corner's
// normal points out of the corner, as an outline-normal stream would, so it
// shades rounded; otherwise each face's normal is flat.
inline std::vector<uint8_t> cubeModel(bool smoothNormals, const QuadOptions& options = {})
{
    std::vector<float> positions;
    std::vector<int16_t> normals;
    std::vector<uint16_t> uvs, indices;
    for (int axis = 0; axis < 3; ++axis)
    {
        for (int side = -1; side <= 1; side += 2)
        {
            const int u = (axis + 1) % 3, v = (axis + 2) % 3;
            const auto base = static_cast<uint16_t>(positions.size() / 3);
            const float corners[4][2] = {{-1, -1}, {1, -1}, {1, 1}, {-1, 1}};
            for (const auto& corner : corners)
            {
                float p[3]{};
                p[axis] = 0.5f * static_cast<float>(side);
                p[u] = 0.5f * corner[0];
                p[v] = 0.5f * corner[1];
                positions.insert(positions.end(), p, p + 3);
                float n[3]{};
                if (smoothNormals)
                {
                    const float length = std::sqrt(p[0] * p[0] + p[1] * p[1] + p[2] * p[2]);
                    n[0] = p[0] / length;
                    n[1] = p[1] / length;
                    n[2] = p[2] / length;
                }
                else
                {
                    n[axis] = static_cast<float>(side);
                }
                octahedralNormal(n, normals);
                uvs.push_back(0);
                uvs.push_back(0);
            }
            // Clockwise from outside: (b - a) x (c - a) points outward.
            const bool flip = side > 0; // (a, b, c) has (b - a) x (c - a) along +axis
            const uint16_t quad[6] = {base, static_cast<uint16_t>(base + 1), static_cast<uint16_t>(base + 2),
                                      base, static_cast<uint16_t>(base + 2), static_cast<uint16_t>(base + 3)};
            for (int t = 0; t < 2; ++t)
            {
                indices.push_back(quad[t * 3]);
                indices.push_back(flip ? quad[t * 3 + 1] : quad[t * 3 + 2]);
                indices.push_back(flip ? quad[t * 3 + 2] : quad[t * 3 + 1]);
            }
        }
    }
    const float boundsMin[3] = {-0.5f, -0.5f, -0.5f}, boundsMax[3] = {0.5f, 0.5f, 0.5f};
    return meshModel(positions, normals, uvs, indices, boundsMin, boundsMax, options);
}

// An RGBA8 texture of `width` x `height` texels, not sRGB, rows top to
// bottom.
inline std::vector<uint8_t> texture2D(uint32_t width, uint32_t height, const std::vector<uint8_t>& rgba)
{
    binary::Writer* writer = binary::createWriter(assets::textureFileKind, assets::textureFileVersion);
    addSection(writer, binary::tag("INFO"),
               std::vector<assets::TextureInfo>{{assets::TextureFormat::Rgba8, width, height, 1, 0}});
    REQUIRE(binary::addSection(*writer, assets::textureMipTag(0), 1, rgba, 16));
    return fileBytes(writer);
}

// An RGBA8 texture of `width` x 1 texels, not sRGB.
inline std::vector<uint8_t> rowTexture(const std::vector<uint8_t>& rgba)
{
    const auto width = static_cast<uint32_t>(rgba.size() / 4);
    binary::Writer* writer = binary::createWriter(assets::textureFileKind, assets::textureFileVersion);
    addSection(writer, binary::tag("INFO"),
               std::vector<assets::TextureInfo>{{assets::TextureFormat::Rgba8, width, 1, 1, 0}});
    REQUIRE(binary::addSection(*writer, assets::textureMipTag(0), 1, rgba, 16));
    return fileBytes(writer);
}

inline std::vector<uint8_t> pixelTexture(uint8_t r, uint8_t g, uint8_t b, uint8_t a)
{
    return rowTexture({r, g, b, a});
}

// Assets for the renderer's reader (setAssetReader), by their ID's first byte.
inline std::map<uint8_t, std::vector<uint8_t>> library;

inline int32_t readTestAsset(const assets::AssetId* id, void* context,
                             void (*deliver)(void* context, const uint8_t* data, uint64_t size))
{
    auto found = library.find(id->bytes[0]);
    if (found == library.end())
    {
        return -1;
    }
    deliver(context, found->second.data(), found->second.size());
    return 0;
}

struct Bgra
{
    uint8_t b, g, r, a;
};

// The default tonemapper (Khronos PBR Neutral, as tonemap.slang), for
// expected pixel values.
inline Vector3 pbrNeutral(Vector3 color)
{
    constexpr float startCompression = 0.8f - 0.04f, desaturation = 0.15f;
    const float x = std::min(color.x, std::min(color.y, color.z));
    const float offset = x < 0.08f ? x - 6.25f * x * x : 0.04f;
    color = color - Vector3{offset, offset, offset};
    const float peak = std::max(color.x, std::max(color.y, color.z));
    if (peak < startCompression)
    {
        return color;
    }
    constexpr float d = 1.0f - startCompression;
    const float newPeak = 1.0f - d * d / (peak + d - startCompression);
    color = color * (newPeak / peak);
    const float g = 1.0f - 1.0f / (desaturation * (peak - newPeak) + 1.0f);
    return lerp(color, Vector3{newPeak, newPeak, newPeak}, g);
}

// A linear value as an sRGB-encoded byte.
inline uint8_t srgbByte(float linear)
{
    const float c = std::clamp(linear, 0.0f, 1.0f);
    const float encoded = c <= 0.0031308f ? c * 12.92f : 1.055f * std::pow(c, 1.0f / 2.4f) - 0.055f;
    return static_cast<uint8_t>(std::lround(encoded * 255.0f));
}

// A world of mesh instances, rendered as the frame loop would.
struct Scene
{
    ecs::World* world = ecs::createWorld();
    uint32_t lastRun = 0;

    Scene() = default;
    Scene(const Scene&) = delete;
    Scene& operator=(const Scene&) = delete;
    ~Scene()
    {
        ecs::destroyWorld(world);
    }

    ecs::Entity quad(const assets::AssetId& model, Vector3 position, float scale = 1,
                     Quaternion rotation = {0, 0, 0, 1})
    {
        const ecs::ComponentId set[] = {ecs::componentIdOf<scenes::MeshInstance>,
                                        ecs::componentIdOf<scenes::LocalTransform>,
                                        ecs::componentIdOf<scenes::WorldTransform>};
        const ecs::Entity entity = ecs::createEntity(*world, set);
        *ecs::get<scenes::MeshInstance>(*world, entity) = {model, 0};
        *ecs::get<scenes::LocalTransform>(*world, entity) = {position, rotation, {scale, scale, scale}};
        return entity;
    }

    // A rotation turning +Z (where lights shine) to `direction`.
    static Quaternion facing(Vector3 direction)
    {
        const Vector3 forward{0, 0, 1};
        const Vector3 to = normalize(direction);
        const float cosine = dot(forward, to);
        if (cosine < -0.9999f)
        {
            return Quaternion::fromAxisAngle({0, 1, 0}, 3.14159265f);
        }
        const Vector3 axis = cross(forward, to);
        const float length = std::sqrt(dot(axis, axis));
        return length < 1e-6f ? Quaternion{0, 0, 0, 1}
                              : Quaternion::fromAxisAngle(axis / length, std::acos(std::fmin(cosine, 1.0f)));
    }

    // A directional light coming from `towardLight`.
    ecs::Entity sun(Vector3 towardLight, Vector3 color = {1, 0.97f, 0.92f}, float intensity = 1)
    {
        const ecs::ComponentId set[] = {ecs::componentIdOf<scenes::LocalTransform>,
                                        ecs::componentIdOf<scenes::WorldTransform>,
                                        ecs::componentIdOf<render::DirectionalLight>};
        const ecs::Entity entity = ecs::createEntity(*world, set);
        *ecs::get<scenes::LocalTransform>(*world, entity) = {{0, 0, 0}, facing(-towardLight), {1, 1, 1}};
        *ecs::get<render::DirectionalLight>(*world, entity) = {color, intensity};
        return entity;
    }

    ecs::Entity ambient(Vector3 sky, Vector3 ground, float intensity = 1)
    {
        const ecs::ComponentId set[] = {ecs::componentIdOf<render::AmbientLight>};
        const ecs::Entity entity = ecs::createEntity(*world, set);
        *ecs::get<render::AmbientLight>(*world, entity) = {sky, ground, intensity};
        return entity;
    }

    // The sun and ambient light the renderer had before light components
    // (M5 step 6a), for tests written against it.
    void defaultLighting()
    {
        sun({0.32f, 0.86f, -0.4f});
        ambient({0.22f, 0.24f, 0.3f}, {0.22f, 0.24f, 0.3f});
    }

    // One frame as the loop runs it: propagation, then the render system
    // with its last-run version, then a newer version.
    void render(render::SurfaceHandle surface)
    {
        scenes::updateWorldTransforms(*world);
        REQUIRE(render::renderFrame(surface, world, lastRun, 0, 0, 0, 1));
        lastRun = ecs::currentVersion(*world);
        ecs::advanceVersion(*world);
    }
};

inline std::vector<uint8_t> readPixels(render::SurfaceHandle surface, uint32_t width, uint32_t height)
{
    std::vector<uint8_t> pixels(width * height * 4);
    REQUIRE(render::readSurfacePixels(surface, pixels));
    return pixels;
}

// The pixel where a world position lands.
inline Bgra pixelOf(const std::vector<uint8_t>& pixels, const render::ViewConstants& view, Vector3 position)
{
    const Vector4 clip = transform(Vector4{position.x, position.y, position.z, 1}, view.viewProjection);
    const auto x = static_cast<uint32_t>((clip.x / clip.w + 1) * 0.5f * view.viewportSize.x);
    const auto y = static_cast<uint32_t>((1 - clip.y / clip.w) * 0.5f * view.viewportSize.y);
    const size_t at = (size_t{y} * static_cast<uint32_t>(view.viewportSize.x) + x) * 4;
    return {pixels[at], pixels[at + 1], pixels[at + 2], pixels[at + 3]};
}

inline std::vector<uint8_t> readFile(const std::string& path)
{
    std::ifstream file(path, std::ios::binary);
    return {std::istreambuf_iterator<char>(file), std::istreambuf_iterator<char>()};
}

// A material shader's type, from its MTYP section: its index, defaults, and
// the offsets of its named parameters.
struct TypeLayout
{
    uint32_t index = 0;
    std::vector<uint8_t> defaults;
    std::vector<std::pair<std::string, uint32_t>> offsets;

    uint32_t offsetOf(const std::string& name) const
    {
        for (const auto& [parameter, offset] : offsets)
        {
            if (parameter == name)
            {
                return offset;
            }
        }
        FAIL("no parameter " << name);
        return 0;
    }
};

inline TypeLayout typeNamed(const std::vector<uint8_t>& shader, std::string_view name)
{
    Result<binary::File*> file = binary::parseFile(shader, render::shaderFileKind, 1);
    REQUIRE(file);
    const int32_t section = binary::findSection(*file.value(), assets::shaderMaterialTypesTag);
    REQUIRE(section >= 0);
    std::vector<uint8_t> types(binary::sectionInfo(*file.value(), static_cast<uint32_t>(section)).size);
    REQUIRE(binary::readSection(*file.value(), static_cast<uint32_t>(section), types));
    binary::closeFile(file.value());
    uint32_t typeCount;
    std::memcpy(&typeCount, types.data(), 4);
    for (uint32_t t = 0; t < typeCount; ++t)
    {
        assets::MaterialTypeInfo type;
        std::memcpy(&type, types.data() + 8 + t * sizeof(type), sizeof(type));
        if (std::string_view(reinterpret_cast<const char*>(types.data()) + type.nameOffset, type.nameLength) != name)
        {
            continue;
        }
        TypeLayout layout;
        layout.index = t;
        layout.defaults.assign(types.begin() + type.defaultsOffset, types.begin() + type.defaultsOffset + type.size);
        for (uint32_t p = 0; p < type.parameterCount; ++p)
        {
            assets::MaterialParameter parameter;
            std::memcpy(&parameter,
                        types.data() + 8 + typeCount * sizeof(assets::MaterialTypeInfo) +
                            (type.firstParameter + p) * sizeof(parameter),
                        sizeof(parameter));
            layout.offsets.emplace_back(
                std::string(reinterpret_cast<const char*>(types.data()) + parameter.nameOffset, parameter.nameLength),
                parameter.offset);
        }
        return layout;
    }
    FAIL("no material type " << name);
    return {};
}

// A .crmat: one opaque record of the shader's type `type`.
inline std::vector<uint8_t> materialFile(const assets::AssetId& shader, uint32_t type,
                                         const std::vector<uint8_t>& parameters,
                                         assets::AlphaMode alphaMode = assets::AlphaMode::Opaque,
                                         bool doubleSided = false,
                                         const std::vector<assets::MaterialTexture>& textures = {})
{
    assets::MaterialInfo info{};
    std::memcpy(info.shader, shader.bytes, 16);
    info.type = type;
    info.alphaMode = alphaMode;
    info.doubleSided = doubleSided ? 1 : 0;
    info.parameterOffset = sizeof(info);
    info.parameterSize = static_cast<uint32_t>(parameters.size());
    info.textureOffset = (sizeof(info) + info.parameterSize + 3) / 4 * 4;
    info.textureCount = static_cast<uint32_t>(textures.size());
    std::vector<uint8_t> record(info.textureOffset + textures.size() * sizeof(assets::MaterialTexture));
    std::memcpy(record.data(), &info, sizeof(info));
    std::memcpy(record.data() + sizeof(info), parameters.data(), parameters.size());
    if (!textures.empty())
    {
        std::memcpy(record.data() + info.textureOffset, textures.data(),
                    textures.size() * sizeof(assets::MaterialTexture));
    }
    binary::Writer* writer = binary::createWriter(assets::materialFileKind, assets::materialFileVersion);
    addSection(writer, assets::meshMaterialsTag, record);
    return fileBytes(writer);
}

} // namespace crucible::test
