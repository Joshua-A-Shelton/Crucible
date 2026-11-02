#pragma once

// Models and textures written in memory for the renderer's GPU tests
// (DrawListTests.cpp, MaterialTests.cpp): what the importers would write,
// down to the generated materials (ADR 0052).

#include "RenderInternal.h"

#include <crucible/BinaryFile.h>
#include <crucible/Ecs.h>
#include <crucible/MaterialFile.h>
#include <crucible/Memory.h>
#include <crucible/MeshFile.h>
#include <crucible/MeshInstance.h>
#include <crucible/Render.h>
#include <crucible/RenderAssets.h>
#include <crucible/TextureFile.h>
#include <crucible/Transform.h>
#include <crucible/Vector.h>

#include "default.g.h" // DefaultMaterial

#include <doctest/doctest.h>

#include <cstring>
#include <map>
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
};

// A unit quad in the XY plane facing -Z (clockwise from a camera looking
// along +Z), with normals, half-float UVs (u left to right, v top to
// bottom), and one generated material.
inline std::vector<uint8_t> quadModel(const QuadOptions& options = {})
{
    binary::Writer* writer = binary::createWriter(assets::meshFileKind, assets::meshFileVersion);
    addSection(writer, binary::tag("INFO"), std::vector<assets::MeshFileInfo>{{1, 1, 1, 4, 1 | 4, 0}});
    assets::MeshEntry entry{};
    entry.vertexCount = 4;
    entry.indexSize = 2;
    entry.attributes = 1 | 4; // normals, UV0
    entry.lodCount = 1;
    entry.submeshCount = 1;
    const float boundsMin[3] = {-0.5f, -0.5f, 0}, boundsMax[3] = {0.5f, 0.5f, 0};
    std::memcpy(entry.boundsMin, boundsMin, sizeof(boundsMin));
    std::memcpy(entry.boundsMax, boundsMax, sizeof(boundsMax));
    addSection(writer, binary::tag("MSHS"), std::vector<assets::MeshEntry>{entry});
    addSection(writer, binary::tag("LODS"), std::vector<assets::MeshLod>{{0, 0.0f}});
    addSection(writer, binary::tag("RNGS"), std::vector<assets::MeshRange>{{0, 6, 0}});
    addSection(writer, binary::tag("POSN"),
               std::vector<float>{-0.5f, -0.5f, 0, -0.5f, 0.5f, 0, 0.5f, 0.5f, 0, 0.5f, -0.5f, 0});
    // (0, 0, -1), octahedral: the fold puts it at the square's corner.
    addSection(writer, binary::tag("NRML"), std::vector<int16_t>(8, 32767));
    // Half floats: 0 and 1 (0x3c00).
    addSection(writer, binary::tag("UV0_"), std::vector<uint16_t>{0, 0x3c00, 0, 0, 0x3c00, 0, 0x3c00, 0x3c00});
    addSection(writer, binary::tag("INDX"), std::vector<uint16_t>{0, 1, 2, 0, 2, 3});
    assets::MeshNode node{};
    node.parent = -1;
    node.mesh = 0;
    addSection(writer, binary::tag("NODE"), std::vector<assets::MeshNode>{node});
    assets::MaterialEntry imported{};
    std::memcpy(imported.baseColor, options.baseColor, sizeof(options.baseColor));
    addSection(writer, binary::tag("MATL"), std::vector<assets::MaterialEntry>{imported});

    // The generated material, as the mesh importer writes it.
    shaders::DefaultMaterial parameters{};
    parameters.baseColor = {options.baseColor[0], options.baseColor[1], options.baseColor[2], options.baseColor[3]};
    parameters.roughness = 0.5f;
    parameters.normalScale = 1;
    parameters.occlusionStrength = 1;
    parameters.alphaCutoff = options.alphaMode == assets::AlphaMode::Mask ? options.alphaCutoff : 0.0f;
    assets::MaterialInfo info{};
    info.type = static_cast<uint32_t>(options.type);
    info.alphaMode = options.alphaMode;
    info.doubleSided = options.doubleSided ? 1 : 0;
    info.alphaCutoff = options.alphaCutoff;
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

} // namespace crucible::test
