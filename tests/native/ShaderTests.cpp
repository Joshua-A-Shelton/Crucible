// The shader toolchain (ADR 0047): SHDR files compiled at build time, loaded,
// and run. The "gpu" suite needs a Slag device; it runs on every backend this
// build has (D3D12 and Vulkan on Windows, Vulkan on Linux).

#include <crucible/BinaryFile.h>
#include <crucible/Memory.h>
#include <crucible/Shader.h>
#include <crucible/Vector.h>

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
    return readFile(CRUCIBLE_SHADER_DIR "/LayoutProbe.crshader");
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
    // The build's formats (CRUCIBLE_SHADER_FORMATS), in order of preference.
    const std::span<const uint8_t> formats = render::shaderFormats(*file.value());
#ifdef _WIN32
    REQUIRE(formats.size() == 2);
    CHECK(formats[0] == static_cast<uint8_t>(slag::ShaderFormat::Dxil));
    CHECK(formats[1] == static_cast<uint8_t>(slag::ShaderFormat::Spirv));
#else
    REQUIRE(formats.size() == 1); // Linux ships SPIR-V only
    CHECK(formats[0] == static_cast<uint8_t>(slag::ShaderFormat::Spirv));
#endif
    slag::ShaderStage stage;
    render::shaderStage(*file.value(), 0, stage);
    CHECK(std::string(stage.entryPoint) == "fill");
    REQUIRE(stage.binaries.size() == formats.size());
    CHECK(stage.binaries.back().format == slag::ShaderFormat::Spirv);
    CHECK(stage.binaries.back().size > 0);
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

// A format no public code names (a private backend's, 0x80 and up) is just
// another code section, offered to Slag in file order; an entry point with
// no code in a format is left out of that format.
TEST_CASE("Shader files carry any format in order of preference")
{
    const render::ShaderEntryInfo entries[2] = {{render::ShaderStageKind::Compute, 0, 1, {1, 1, 1}},
                                                {render::ShaderStageKind::Compute, 1, 1, {1, 1, 1}}};
    auto codeSection = [](std::vector<uint8_t> first, std::vector<uint8_t> second) {
        std::vector<uint8_t> section(2 * sizeof(render::ShaderCode));
        const render::ShaderCode where[2] = {
            {static_cast<uint32_t>(section.size()), static_cast<uint32_t>(first.size())},
            {static_cast<uint32_t>(section.size() + first.size()), static_cast<uint32_t>(second.size())}};
        std::memcpy(section.data(), where, sizeof(where));
        section.insert(section.end(), first.begin(), first.end());
        section.insert(section.end(), second.begin(), second.end());
        return section;
    };
    const std::vector<uint8_t> privateCode = codeSection({0x80, 0x81, 0x82, 0x83}, {});
    const std::vector<uint8_t> spirvCode = codeSection({1, 2, 3, 4}, {5, 6, 7, 8});
    const uint8_t names[] = {'a', 'b'};
    binary::Writer* writer = binary::createWriter(render::shaderFileKind, render::shaderFileVersion);
    REQUIRE(binary::addSection(*writer, binary::tag("ENTS"), 1,
                               {reinterpret_cast<const uint8_t*>(entries), sizeof(entries)}, 16));
    REQUIRE(binary::addSection(*writer, binary::tag("NAME"), 1, names, 16));
    REQUIRE(binary::addSection(*writer, render::shaderCodeTag(0x80), 1, privateCode, 16));
    REQUIRE(binary::addSection(*writer, render::shaderCodeTag(1), 1, spirvCode, 16));
    Vector<std::byte> bytes(MemoryTag::Render);
    REQUIRE(binary::writeToMemory(*writer, binary::Compression::None, bytes));
    binary::destroyWriter(writer);

    Result<render::ShaderFile*> file =
        render::loadShaderFile({reinterpret_cast<const uint8_t*>(bytes.data()), bytes.size()});
    REQUIRE(file);
    const std::span<const uint8_t> formats = render::shaderFormats(*file.value());
    REQUIRE(formats.size() == 2);
    CHECK(formats[0] == 0x80);
    CHECK(formats[1] == 1);
    slag::ShaderStage stage;
    render::shaderStage(*file.value(), 0, stage);
    REQUIRE(stage.binaries.size() == 2);
    CHECK(static_cast<uint8_t>(stage.binaries[0].format) == 0x80);
    CHECK(static_cast<const uint8_t*>(stage.binaries[0].data)[3] == 0x83);
    CHECK(stage.binaries[1].format == slag::ShaderFormat::Spirv);
    render::shaderStage(*file.value(), 1, stage);
    REQUIRE(stage.binaries.size() == 1); // no private-format code for this one
    CHECK(static_cast<const uint8_t*>(stage.binaries[0].data)[0] == 5);
    CHECK(std::string(stage.entryPoint) == "b");
    render::destroyShaderFile(file.value());
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
