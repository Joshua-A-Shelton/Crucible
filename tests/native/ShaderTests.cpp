// The shader toolchain (ADR 0047): SHDR files compiled at build time, loaded,
// and run. The "gpu" suite needs a Slag device; it runs on every backend this
// build has (D3D12 and Vulkan on Windows, Vulkan on Linux).

#include <crucible/Shader.h>

#include <LayoutProbe.g.h>

#include <doctest/doctest.h>
#include <slag/Slag.h>

#include <cstring>
#include <fstream>
#include <iterator>
#include <string>
#include <vector>

using namespace crucible;
using crucible::shaders::PackedPair;
using crucible::shaders::Probe;

namespace {

std::vector<uint8_t> readFile(const std::string& path)
{
    std::ifstream file(path, std::ios::binary);
    return {std::istreambuf_iterator<char>(file), std::istreambuf_iterator<char>()};
}

// Backend validation's errors and warnings, reported by the GPU test.
void collectMessage(const slag::DebugMessage& message, void* userData)
{
    if (message.severity == slag::MessageSeverity::Error || message.severity == slag::MessageSeverity::Warning)
    {
        static_cast<std::vector<std::string>*>(userData)->push_back(message.text);
    }
}

std::vector<uint8_t> probeShader()
{
    return readFile(CRUCIBLE_SHADER_DIR "/LayoutProbe.shdr");
}

} // namespace

TEST_CASE("A compiled shader lists its entry points")
{
    const std::vector<uint8_t> bytes = probeShader();
    Result<render::ShaderFile*> file = render::loadShaderFile(bytes);
    REQUIRE(file);
    REQUIRE(render::shaderEntryCount(*file.value()) == 2);
    CHECK(render::shaderEntryName(*file.value(), 0) == "fill");
    CHECK(render::findShaderEntry(*file.value(), "fill") == 0);
    CHECK(render::findShaderEntry(*file.value(), "fillPacked") == 1);
    CHECK(render::findShaderEntry(*file.value(), "missing") == -1);
    const render::ShaderEntryInfo& entry = render::shaderEntry(*file.value(), 0);
    CHECK(entry.stage == render::ShaderStageKind::Compute);
    CHECK(entry.threadGroupSize[0] == 64);
    CHECK(entry.threadGroupSize[1] == 1);
    CHECK(entry.spirvSize > 0);
#ifdef _WIN32
    CHECK(entry.dxilSize > 0);
#else
    CHECK(entry.dxilSize == 0); // no DXC outside Windows
#endif
    slag::ShaderStage stage;
    render::shaderStage(*file.value(), 0, stage);
    CHECK(std::string(stage.entryPoint) == "fill");
    CHECK(stage.binaries.back().format == slag::ShaderFormat::Spirv);
    render::destroyShaderFile(file.value());

    const std::vector<uint8_t> junk = {1, 2, 3, 4};
    CHECK(render::loadShaderFile(junk).status() == Status::InvalidData);
}

// Runs one of LayoutProbe's entry points for `count` threads, into a
// structured buffer of `stride`-byte elements, and returns its bytes.
std::vector<uint8_t> runFill(slag::GraphicsDevice* device, const render::ShaderFile& file, const char* entry,
                             uint32_t count, uint32_t stride)
{
    slag::ShaderStage stage;
    render::shaderStage(file, static_cast<uint32_t>(render::findShaderEntry(file, entry)), stage);
    slag::Result<slag::Pipeline*> pipeline = device->createComputePipeline({.compute = stage, .debugName = entry});
    REQUIRE(static_cast<bool>(pipeline));
    constexpr uint32_t outputSlot = 3;
    const uint64_t size = uint64_t{count} * stride;
    slag::Buffer* output =
        device->createBuffer({.size = size, .placement = slag::Placement::automatic(slag::MemoryKind::GpuOnly)})
            .value();
    slag::Buffer* readback =
        device->createBuffer({.size = size, .placement = slag::Placement::automatic(slag::MemoryKind::Readback)})
            .value();
    REQUIRE(output);
    REQUIRE(readback);
    device->writeRWStructuredBuffer(outputSlot, {.buffer = output, .count = count, .stride = stride});

    slag::CommandBuffer* commands = device->createCommandBuffer({.queueType = slag::CommandQueueType::Compute}).value();
    REQUIRE(commands);
    REQUIRE(static_cast<bool>(commands->begin()));
    commands->bindPipeline(pipeline.value());
    const uint32_t constants[2] = {outputSlot, count};
    commands->pushConstants(constants, sizeof(constants));
    commands->dispatch((count + 63) / 64, 1, 1);
    const slag::BufferBarrier toCopy = {.syncBefore = slag::BarrierSync::ComputeShading,
                                        .syncAfter = slag::BarrierSync::Copy,
                                        .accessBefore = slag::BarrierAccess::ShaderWrite,
                                        .accessAfter = slag::BarrierAccess::CopySource,
                                        .buffer = output};
    commands->barrier({}, {&toCopy, 1});
    commands->copyBuffer(readback, 0, output, 0, size);
    REQUIRE(static_cast<bool>(commands->end()));
    slag::Semaphore* done = device->createSemaphore({}).value();
    REQUIRE(done);
    slag::SemaphoreValue signal = {done, 1};
    slag::CommandBuffer* submitted[] = {commands};
    REQUIRE(static_cast<bool>(device->commandQueue(slag::CommandQueueType::Compute)
                                  ->submit({.commandBuffers = submitted, .signals = {&signal, 1}})));
    REQUIRE(static_cast<bool>(done->wait(1, 5'000'000'000)));
    std::vector<uint8_t> bytes(size);
    std::memcpy(bytes.data(), readback->mappedData(), size);

    device->destroy(done);
    device->destroy(commands);
    device->destroy(readback);
    device->destroy(output);
    device->destroy(pipeline.value());
    return bytes;
}

TEST_SUITE("gpu")
{
    // The GPU fills structs the C++ mirrors read back: the same offsets, sizes,
    // and row-major matrix on every backend, and D3D12's packing on Vulkan.
    TEST_CASE("Shared structs read back on every backend")
    {
        const std::vector<uint8_t> bytes = probeShader();
        Result<render::ShaderFile*> file = render::loadShaderFile(bytes);
        REQUIRE(file);

        int backends = 0;
        for (slag::Backend backend : {slag::Backend::D3D12, slag::Backend::Vulkan})
        {
            if (!slag::isBackendAvailable(backend))
            {
                continue;
            }
            CAPTURE(static_cast<int>(backend));
            slag::InstanceDesc desc;
            desc.backend = backend;
            desc.applicationName = "crucible_shader_tests";
            // Backend validation where its layers are installed (best effort).
            desc.validationFlags = slag::ValidationFlags::Api;
            std::vector<std::string> messages;
            desc.messageCallback = collectMessage;
            desc.messageUserData = &messages;
            slag::Result<slag::Instance*> instance = slag::createInstance(desc);
            if (!instance || instance.value()->graphicsDevices().empty())
            {
                slag::destroy(instance ? instance.value() : nullptr);
                continue; // compiled in, but no device here
            }
            ++backends;
            slag::GraphicsDevice* device = instance.value()->graphicsDevices()[0];
            MESSAGE("device: " << std::string(device->info().name));
            REQUIRE(static_cast<bool>(device->initialize({})));

            constexpr uint32_t count = 100;
            const std::vector<uint8_t> probeBytes = runFill(device, *file.value(), "fill", count, sizeof(Probe));
            std::vector<Probe> probes(count);
            std::memcpy(probes.data(), probeBytes.data(), probeBytes.size());
            for (uint32_t i = 0; i < count; ++i)
            {
                CAPTURE(i);
                const Probe& probe = probes[i];
                const float f = static_cast<float>(i);
                CHECK(probe.position.x == f);
                CHECK(probe.position.y == f + 0.5f);
                CHECK(probe.position.z == -f);
                CHECK(probe.radius == f * 0.25f);
                // Row-major: the rows as the shader wrote them, translation in the last.
                CHECK(probe.transform.rows[0].x == 1.0f);
                CHECK(probe.transform.rows[0].w == 4.0f);
                CHECK(probe.transform.rows[1].x == 5.0f);
                CHECK(probe.transform.rows[3].x == f);
                CHECK(probe.transform.rows[3].w == 16.0f);
                CHECK(probe.tintColor.y == 0.5f);
                CHECK(probe.tintStrength == f);
                CHECK(probe.uv.x == 0.125f);
                CHECK(probe.uv.y == f);
                CHECK(probe.index == -static_cast<int32_t>(i));
                CHECK(probe.flags == (i | 0x80000000u));
            }

            static_assert(sizeof(PackedPair) == 16);
            const std::vector<uint8_t> pairBytes =
                runFill(device, *file.value(), "fillPacked", count, sizeof(PackedPair));
            std::vector<PackedPair> pairs(count);
            std::memcpy(pairs.data(), pairBytes.data(), pairBytes.size());
            for (uint32_t i = 0; i < count; ++i)
            {
                CAPTURE(i);
                CHECK(pairs[i].a == static_cast<float>(i));
                CHECK(pairs[i].b.x == 1.0f);
                CHECK(pairs[i].b.z == static_cast<float>(i) * 2.0f);
            }
            device->shutdown();
            slag::destroy(instance.value());
            for (const std::string& message : messages)
            {
                MESSAGE("validation: " << message);
            }
        }
        CHECK(backends > 0);
        render::destroyShaderFile(file.value());
    }
}
