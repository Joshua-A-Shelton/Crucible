// GPU assets (ADR 0049): models and textures loaded from their files, read
// back from the GPU byte for byte, through both upload paths; acquiring by
// asset ID with counted uses; stale handles. The "gpu" suite runs on every
// backend this build has.

#include "RenderInternal.h"

#include <crucible/BinaryFile.h>
#include <crucible/Memory.h>
#include <crucible/MeshFile.h>
#include <crucible/Platform.h>
#include <crucible/Render.h>
#include <crucible/RenderAssets.h>
#include <crucible/TextureFile.h>
#include <crucible/Vector.h>

#include <doctest/doctest.h>
#include <slag/Slag.h>

#include <cstring>
#include <map>
#include <vector>

using namespace crucible;

namespace {

std::vector<uint8_t> fileBytes(binary::Writer* writer)
{
    Vector<std::byte> bytes(MemoryTag::General);
    REQUIRE(binary::writeToMemory(*writer, binary::Compression::Fast, bytes));
    binary::destroyWriter(writer);
    const auto* data = reinterpret_cast<const uint8_t*>(bytes.data());
    return {data, data + bytes.size()};
}

template <class T>
void addSection(binary::Writer* writer, const char (&tag)[5], const std::vector<T>& values)
{
    REQUIRE(binary::addSection(*writer, binary::tag(tag), 1,
                               {reinterpret_cast<const uint8_t*>(values.data()), values.size() * sizeof(T)}, 16));
}

// A model of one triangle: positions, normals, 16-bit indices, one node.
struct TestModel
{
    std::vector<float> positions = {0, 0, 0, 1, 0, 0, 0, 1, 0};
    std::vector<int16_t> normals = {0, 0, 1, 0, 2, 0};
    std::vector<uint16_t> indices = {0, 1, 2};

    std::vector<uint8_t> bytes() const
    {
        binary::Writer* writer = binary::createWriter(assets::meshFileKind, assets::meshFileVersion);
        addSection(writer, "INFO", std::vector<assets::MeshFileInfo>{{1, 1, 0, 3, 1, 0}});
        assets::MeshEntry entry{};
        entry.vertexCount = 3;
        entry.indexSize = 2;
        entry.attributes = 1;
        entry.lodCount = 1;
        entry.submeshCount = 1;
        addSection(writer, "MSHS", std::vector<assets::MeshEntry>{entry});
        addSection(writer, "LODS", std::vector<assets::MeshLod>{{0, 0.0f}});
        addSection(writer, "RNGS", std::vector<assets::MeshRange>{{0, 3, -1}});
        addSection(writer, "POSN", positions);
        addSection(writer, "NRML", normals);
        addSection(writer, "INDX", indices);
        assets::MeshNode node{};
        node.parent = -1;
        addSection(writer, "NODE", std::vector<assets::MeshNode>{node});
        return fileBytes(writer);
    }
};

// An 8x4 RGBA8 texture with three mips, each texel distinct.
std::vector<std::vector<uint8_t>> testMips()
{
    std::vector<std::vector<uint8_t>> mips;
    for (uint32_t mip = 0, width = 8, height = 4; mip < 3; ++mip, width /= 2, height /= 2)
    {
        std::vector<uint8_t> pixels(width * height * 4);
        for (size_t i = 0; i < pixels.size(); ++i)
        {
            pixels[i] = static_cast<uint8_t>(i * 7 + mip * 31 + 1);
        }
        mips.push_back(pixels);
    }
    return mips;
}

std::vector<uint8_t> textureBytes(const std::vector<std::vector<uint8_t>>& mips)
{
    binary::Writer* writer = binary::createWriter(assets::textureFileKind, assets::textureFileVersion);
    addSection(writer, "INFO", std::vector<assets::TextureInfo>{{assets::TextureFormat::Rgba8, 8, 4, 3, 0}});
    for (uint32_t mip = 0; mip < mips.size(); ++mip)
    {
        REQUIRE(binary::addSection(*writer, assets::textureMipTag(mip), 1, mips[mip], 16));
    }
    return fileBytes(writer);
}

// Runs commands on the renderer's queue and waits.
template <class Record>
void runAndWait(Record&& record)
{
    slag::GraphicsDevice* device = render::renderDevice();
    slag::CommandBuffer* commands = device->createCommandBuffer({}).value();
    REQUIRE(commands->begin());
    record(commands);
    REQUIRE(commands->end());
    slag::Semaphore* done = device->createSemaphore({}).value();
    const slag::SemaphoreValue signal = {done, 1};
    slag::CommandBuffer* const submitted[] = {commands};
    REQUIRE(render::renderQueue()->submit({.commandBuffers = submitted, .signals = {&signal, 1}}));
    REQUIRE(done->wait(1, UINT64_MAX));
    device->destroy(done);
    device->destroy(commands);
}

std::vector<uint8_t> readBuffer(slag::Buffer* buffer, uint64_t offset, uint64_t size)
{
    slag::GraphicsDevice* device = render::renderDevice();
    slag::Buffer* readback =
        device->createBuffer({.size = size, .placement = slag::Placement::automatic(slag::MemoryKind::Readback)})
            .value();
    runAndWait([&](slag::CommandBuffer* commands) { commands->copyBuffer(readback, 0, buffer, offset, size); });
    const auto* data = static_cast<const uint8_t*>(readback->mappedData());
    std::vector<uint8_t> bytes(data, data + size);
    device->destroy(readback);
    return bytes;
}

std::vector<uint8_t> asBytes(const void* data, size_t size)
{
    const auto* begin = static_cast<const uint8_t*>(data);
    return {begin, begin + size};
}

// Test assets for the reader, by the first byte of their ID.
std::map<uint8_t, std::vector<uint8_t>> library;

int32_t readTestAsset(const assets::AssetId* id, void* context,
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

assets::AssetId idOf(uint8_t first)
{
    assets::AssetId id{};
    id.bytes[0] = first;
    id.bytes[15] = 0xA5;
    return id;
}

} // namespace

TEST_CASE("GPU assets need the renderer")
{
    CHECK(render::loadMesh(TestModel().bytes()).status() == Status::Unsupported);
    CHECK(render::acquireTexture(idOf(1)).status() == Status::Unsupported);
    CHECK(render::meshData({0, 1}) == nullptr);
    CHECK(render::textureDescriptor({0, 1}) == 0);
    CHECK(render::samplerDescriptor(render::Sampler::LinearRepeat) !=
          render::samplerDescriptor(render::Sampler::PointClamp));
}

TEST_SUITE("gpu")
{
    TEST_CASE("Models and textures reach the GPU byte for byte on every backend")
    {
        REQUIRE(platform::initialize());
        for (slag::Backend backend : {slag::Backend::D3D12, slag::Backend::Vulkan})
        {
            if (!slag::isBackendAvailable(backend) || !render::initialize(backend))
            {
                continue;
            }
            CAPTURE(static_cast<int>(backend));
            MESSAGE("direct uploads available: " << render::renderDevice()->capabilities().gpuUploadMemory);
            const TestModel model;
            for (bool staging : {false, true})
            {
                CAPTURE(staging);
                render::forceStagingUploads(staging);
                Result<render::MeshHandle> mesh = render::loadMesh(model.bytes());
                REQUIRE(mesh);
                const render::MeshData* data = render::meshData(mesh.value());
                REQUIRE(data != nullptr);
                CHECK(data->info.vertexCount == 3);
                REQUIRE(data->meshes.size() == 1);
                CHECK(data->meshes[0].indexSize == 2);
                CHECK(data->ranges.size() == 1);
                CHECK(data->nodes.size() == 1);
                CHECK(data->materials.empty());
                CHECK(data->tangents.size == 0); // not in the file
                CHECK(data->normals.offset % 256 == 0);
                CHECK(data->indices.offset % 256 == 0);
                CHECK(readBuffer(data->buffer, data->positions.offset, data->positions.size) ==
                      asBytes(model.positions.data(), model.positions.size() * 4));
                CHECK(readBuffer(data->buffer, data->normals.offset, data->normals.size) ==
                      asBytes(model.normals.data(), model.normals.size() * 2));
                CHECK(readBuffer(data->buffer, data->indices.offset, data->indices.size) ==
                      asBytes(model.indices.data(), model.indices.size() * 2));
                render::unloadMesh(mesh.value());
                CHECK(render::meshData(mesh.value()) == nullptr); // stale now
            }
            render::forceStagingUploads(false);

            const std::vector<std::vector<uint8_t>> mips = testMips();
            Result<render::TextureHandle> texture = render::loadTexture(textureBytes(mips));
            REQUIRE(texture);
            CHECK(render::textureDescriptor(texture.value()) >= 1024); // after the steps' slots
            // Each mip read back through a copy, out of ShaderRead and back.
            slag::GraphicsDevice* device = render::renderDevice();
            slag::Texture* gpuTexture = render::textureOf(texture.value());
            REQUIRE(gpuTexture != nullptr);
            for (uint32_t mip = 0; mip < mips.size(); ++mip)
            {
                CAPTURE(mip);
                const slag::CopyFootprint footprint = device->copyFootprint(gpuTexture, mip);
                slag::Buffer* readback =
                    device
                        ->createBuffer({.size = footprint.size,
                                        .placement = slag::Placement::automatic(slag::MemoryKind::Readback)})
                        .value();
                const slag::SubresourceRange range = {.baseMip = mip, .mipCount = 1};
                runAndWait([&](slag::CommandBuffer* commands) {
                    const slag::TextureBarrier toCopy = {.syncAfter = slag::BarrierSync::Copy,
                                                         .accessAfter = slag::BarrierAccess::CopySource,
                                                         .layoutBefore = slag::TextureLayout::ShaderRead,
                                                         .layoutAfter = slag::TextureLayout::CopySource,
                                                         .texture = gpuTexture,
                                                         .range = range};
                    commands->barrier({}, {}, {&toCopy, 1});
                    commands->copyTextureToBuffer({readback, 0, footprint.rowPitch},
                                                  {.texture = gpuTexture, .mip = mip}, footprint.width,
                                                  footprint.height);
                    const slag::TextureBarrier back = {.syncBefore = slag::BarrierSync::Copy,
                                                       .accessBefore = slag::BarrierAccess::CopySource,
                                                       .accessAfter = slag::BarrierAccess::NoAccess,
                                                       .layoutBefore = slag::TextureLayout::CopySource,
                                                       .layoutAfter = slag::TextureLayout::ShaderRead,
                                                       .texture = gpuTexture,
                                                       .range = range};
                    commands->barrier({}, {}, {&back, 1});
                });
                const auto* rows = static_cast<const uint8_t*>(readback->mappedData());
                std::vector<uint8_t> pixels;
                for (uint32_t row = 0; row < footprint.height; ++row)
                {
                    pixels.insert(pixels.end(), rows + row * footprint.rowPitch,
                                  rows + row * footprint.rowPitch + footprint.width * 4);
                }
                CHECK(pixels == mips[mip]);
                device->destroy(readback);
            }
            render::unloadTexture(texture.value());
            CHECK(render::textureDescriptor(texture.value()) == 0);

            CHECK(render::loadMesh(textureBytes(mips)).status() == Status::InvalidData); // not a model
            CHECK(render::loadTexture(model.bytes()).status() == Status::InvalidData);

            render::shutdown();
        }
        platform::shutdown();
    }

    TEST_CASE("Assets are acquired by ID with counted uses on every backend")
    {
        REQUIRE(platform::initialize());
        library = {{1, TestModel().bytes()}, {2, textureBytes(testMips())}, {3, {1, 2, 3}}};
        render::setAssetReader(reinterpret_cast<uint64_t>(&readTestAsset));
        for (slag::Backend backend : {slag::Backend::D3D12, slag::Backend::Vulkan})
        {
            if (!slag::isBackendAvailable(backend) || !render::initialize(backend))
            {
                continue;
            }
            CAPTURE(static_cast<int>(backend));
            Result<render::MeshHandle> first = render::acquireMesh(idOf(1));
            Result<render::MeshHandle> second = render::acquireMesh(idOf(1));
            REQUIRE(first);
            REQUIRE(second);
            CHECK(first.value().index == second.value().index); // loaded once
            CHECK(first.value().generation == second.value().generation);
            render::unloadMesh(first.value()); // wrong call for an acquired one: reported, ignored
            CHECK(render::meshData(first.value()) != nullptr);
            render::releaseMesh(first.value());
            CHECK(render::meshData(first.value()) != nullptr); // one use left
            render::releaseMesh(second.value());
            CHECK(render::meshData(first.value()) == nullptr);

            // A new load reuses the slot under a new generation.
            Result<render::MeshHandle> again = render::acquireMesh(idOf(1));
            REQUIRE(again);
            CHECK(again.value().index == first.value().index);
            CHECK(again.value().generation != first.value().generation);
            CHECK(render::meshData(first.value()) == nullptr);
            render::releaseMesh(again.value());

            Result<render::TextureHandle> texture = render::acquireTexture(idOf(2));
            REQUIRE(texture);
            CHECK(render::textureDescriptor(texture.value()) != 0);
            render::releaseTexture(texture.value());

            CHECK(render::acquireMesh(idOf(9)).status() == Status::NotFound);
            CHECK(render::acquireTexture(idOf(3)).status() == Status::InvalidData);
            CHECK(render::acquireMesh(idOf(2)).status() == Status::InvalidData); // a texture, not a model

            // Still held when the renderer stops: freed with it.
            REQUIRE(render::acquireTexture(idOf(2)));
            render::shutdown();
        }
        render::setAssetReader(0);
        platform::shutdown();
    }
}
