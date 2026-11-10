// The batched command API (ADR 0058) from C++, as the engine's C# uses it:
// steps called through the trampoline, command records written into the
// memory passCommands hands out, checked when the step returns, and played
// back on every backend.

#include "TestModels.h" // readTestAsset, readFile

#include <crucible/Platform.h>
#include <crucible/Render.h>
#include <crucible/RenderCommands.h>

#include <doctest/doctest.h>
#include <slag/Instance.h>

#include <cstring>
#include <functional>
#include <ostream>
#include <string>
#include <vector>

using namespace crucible;
using namespace crucible::render;

namespace {

// The steps' bodies by ID, called by the trampoline as C#'s would be.
std::function<int32_t(StepCall, StepFrame*)> bodies[4];
std::vector<std::string> calls;

int32_t trampoline(uint64_t id, uint32_t call, StepFrame* frame)
{
    return bodies[id](static_cast<StepCall>(call), frame);
}

std::vector<std::string> scheduleNames()
{
    std::vector<std::string> names;
    for (uint32_t i = 0; i < stepCount(0); ++i)
    {
        names.emplace_back(stepName(0, i));
    }
    return names;
}

// Writes a pass's records as RenderCommands.cs does.
class Records
{
public:
    Records(StepFrame& frame, uint32_t pass)
        : frame(frame)
        , pass(pass)
    {}

    template <class T>
    void add(RenderCommandType type, const T& command, const void* extra = nullptr, uint32_t extraSize = 0)
    {
        const uint32_t size = static_cast<uint32_t>(sizeof(RenderCommandHeader) + sizeof(T)) + (extraSize + 3) / 4 * 4;
        uint8_t* at = reserve(size);
        const RenderCommandHeader header{type, static_cast<uint16_t>(size / 4)};
        std::memcpy(at, &header, sizeof(header));
        std::memcpy(at + sizeof(header), &command, sizeof(T));
        if (extraSize > 0)
        {
            std::memcpy(at + sizeof(header) + sizeof(T), extra, extraSize);
        }
    }

    void endLabel()
    {
        const RenderCommandHeader header{RenderCommandType::EndLabel, 1};
        std::memcpy(reserve(4), &header, sizeof(header));
    }

    void label(const char* name)
    {
        const auto length = static_cast<uint32_t>(std::strlen(name) + 1);
        add(RenderCommandType::BeginLabel, BeginLabelCommand{length}, name, length);
    }

    // A raw header, for malformed records.
    void header(RenderCommandType type, uint16_t words)
    {
        const RenderCommandHeader header{type, words};
        uint8_t* at = reserve(words * 4u < 4 ? 4 : words * 4u);
        std::memcpy(at, &header, sizeof(header));
    }

    void finish()
    {
        (void)passCommands(frame, pass, used, 0);
    }

private:
    uint8_t* reserve(uint32_t size)
    {
        if (memory.size - used < size)
        {
            memory = passCommands(frame, pass, used, size);
        }
        uint8_t* at = reinterpret_cast<uint8_t*>(memory.address) + used;
        used += size;
        return at;
    }

    StepFrame& frame;
    uint32_t pass;
    StepMemory memory{};
    uint32_t used = 0;
};

constexpr assets::AssetId probeShader{{0xC5, 1}};
PipelineHandle fillPipeline{};
PipelineHandle quadPipeline{};

struct ProbeConstants
{
    uint32_t buffer;
    uint32_t frameData;
    uint32_t offset;
};

enum class Mistake
{
    None,
    DrawWithoutPipeline,
    ComputePipelineInGraphicsPass,
    DeclaringAnEarlierPass,
    Throwing,
    BadLength,
    OpenLabel,
};
Mistake mistake = Mistake::None;

int32_t probeStep(StepCall call, StepFrame* frame)
{
    calls.emplace_back(call == StepCall::Prepare ? "Prepare" : call == StepCall::Release ? "Release" : "AddPasses");
    if (call == StepCall::Prepare)
    {
        Result<PipelineHandle> fill = createComputePipeline(probeShader, "fillColors");
        Result<PipelineHandle> quads = createGraphicsPipeline(probeShader, "quadVertex", "quadPixel",
                                                              {.blend = BlendMode::Opaque,
                                                               .cull = CullMode::None,
                                                               .topology = Topology::TriangleStrip,
                                                               .colorFormats = {RenderFormat::B8G8R8A8UnormSrgb},
                                                               .colorCount = 1});
        if (!fill || !quads)
        {
            return 1;
        }
        fillPipeline = fill.value();
        quadPipeline = quads.value();
        return 0;
    }
    if (call == StepCall::Release)
    {
        destroyPipeline(fillPipeline);
        destroyPipeline(quadPipeline);
        return 0;
    }
    if (mistake == Mistake::Throwing)
    {
        return 1; // what C# returns when the step threw
    }

    const StepFrameInfo info = stepFrameInfo(*frame);
    const float colors[16] = {1, 0, 0, 1, 0, 1, 0, 1, 0, 0, 1, 1, 1, 1, 1, 1};
    const StepMemory colorData = uploadFrameData(*frame, sizeof(colors));
    std::memcpy(reinterpret_cast<void*>(colorData.address), colors, sizeof(colors));
    const float tint[4] = {1, 1, 1, 1};
    const StepMemory tintData = uploadFrameData(*frame, sizeof(tint));
    std::memcpy(reinterpret_cast<void*>(tintData.address), tint, sizeof(tint));
    const GraphBuffer buffer = createStepBuffer(*frame, sizeof(colors));

    const uint32_t fill = addStepPass(*frame, "Test.Fill", PassQueue::Compute);
    const uint32_t writeSlot = useBuffer(*frame, fill, buffer, ResourceUsage::ShaderWrite);
    Records fillRecords(*frame, fill);
    fillRecords.add(RenderCommandType::SetPipeline, SetPipelineCommand{fillPipeline});
    const ProbeConstants fillConstants{writeSlot, info.frameDataDescriptor, colorData.offset};
    fillRecords.add(RenderCommandType::PushConstants, PushConstantsCommand{0, sizeof(fillConstants)}, &fillConstants,
                    sizeof(fillConstants));
    fillRecords.add(RenderCommandType::Dispatch, DispatchCommand{1, 1, 1});

    const uint32_t quads = addStepPass(*frame, "Test.Quads", PassQueue::Graphics);
    const uint32_t readSlot = useBuffer(*frame, quads, buffer, ResourceUsage::ShaderRead);
    passRenderTarget(*frame, quads, stepTexture(*frame, backbufferKey()), LoadAction::Load, StoreAction::Store, {});
    if (mistake == Mistake::DeclaringAnEarlierPass)
    {
        (void)useBuffer(*frame, fill, buffer, ResourceUsage::ShaderRead);
    }
    Records quadRecords(*frame, quads);
    quadRecords.label("Test.Quads");
    if (mistake != Mistake::DrawWithoutPipeline)
    {
        quadRecords.add(
            RenderCommandType::SetPipeline,
            SetPipelineCommand{mistake == Mistake::ComputePipelineInGraphicsPass ? fillPipeline : quadPipeline});
    }
    const ProbeConstants quadConstants{readSlot, 0, 0};
    quadRecords.add(RenderCommandType::PushConstants, PushConstantsCommand{0, sizeof(quadConstants)}, &quadConstants,
                    sizeof(quadConstants));
    quadRecords.add(RenderCommandType::SetConstantBuffer, SetConstantBufferCommand{{UINT32_MAX}, tintData.offset, 16});
    // Three quarters, then the fourth (bottom right) scissored away.
    quadRecords.add(RenderCommandType::Draw, DrawCommand{4, 3, 0, 0});
    quadRecords.add(RenderCommandType::SetScissor, SetScissorCommand{0, 0, info.width / 2, info.height});
    quadRecords.add(RenderCommandType::Draw, DrawCommand{4, 1, 0, 3});
    if (mistake == Mistake::BadLength)
    {
        quadRecords.header(RenderCommandType::Draw, 1);
    }
    if (mistake != Mistake::OpenLabel)
    {
        quadRecords.endLabel();
    }
    fillRecords.finish();
    quadRecords.finish();
    return 0;
}

const char* backendName(slag::Backend backend)
{
    return backend == slag::Backend::D3D12 ? "D3D12" : "Vulkan";
}

} // namespace

TEST_CASE("C# steps go into the schedule by name")
{
    setStepTrampoline(reinterpret_cast<uint64_t>(&trampoline));
    calls.clear();
    bodies[1] = [](StepCall call, StepFrame*) {
        calls.emplace_back(call == StepCall::Prepare ? "Prepare" : "other");
        return 0;
    };
    const std::vector<std::string> before = scheduleNames();
    REQUIRE(insertManagedStep(0, "Test.Last", 1, StepPlacement::Last, ""));
    REQUIRE(insertManagedStep(0, "Test.Before", 1, StepPlacement::Before, "Opaque"));
    REQUIRE(insertManagedStep(0, "Test.After", 1, StepPlacement::After, "Opaque"));
    REQUIRE(insertManagedStep(0, "Test.Replacement", 1, StepPlacement::Replacing, "Test.Last"));
    CHECK(insertManagedStep(0, "Test.Missing", 1, StepPlacement::After, "Nowhere").status() == Status::NotFound);
    CHECK(insertManagedStep(0, "Opaque", 1, StepPlacement::Last, "").status() == Status::InvalidData);
    std::vector<std::string> names = scheduleNames();
    const auto at = [&](const char* name) {
        return std::find(names.begin(), names.end(), name) - names.begin();
    };
    CHECK(at("Test.Before") + 1 == at("Opaque"));
    CHECK(at("Test.After") == at("Opaque") + 1);
    CHECK(names.back() == "Test.Replacement");
    CHECK(calls.empty()); // no renderer: prepared when it starts
    for (const char* name : {"Test.Before", "Test.After", "Test.Replacement"})
    {
        REQUIRE(removeStep(0, name));
    }
    CHECK(removeStep(0, "Test.Before").status() == Status::NotFound);
    CHECK(scheduleNames() == before);
    CHECK(stepName(0, stepCount(0)).empty());
    setStepTrampoline(0);
}

TEST_CASE("New schedules start with Clear and keep their own steps")
{
    setStepTrampoline(reinterpret_cast<uint64_t>(&trampoline));
    bodies[2] = [](StepCall, StepFrame*) {
        return 0;
    };
    const uint32_t schedule = createRenderSchedule();
    REQUIRE(schedule != 0);
    REQUIRE(stepCount(schedule) == 1);
    CHECK(stepName(schedule, 0) == "Clear");
    REQUIRE(insertManagedStep(schedule, "Test.UI", 2, StepPlacement::After, "Clear"));
    CHECK(stepName(schedule, 1) == "Test.UI");
    // The default's steps aren't there, and it doesn't have this one.
    CHECK(insertManagedStep(schedule, "Test.Missing", 2, StepPlacement::After, "Opaque").status() == Status::NotFound);
    const std::vector<std::string> defaults = scheduleNames();
    CHECK(std::find(defaults.begin(), defaults.end(), "Test.UI") == defaults.end());

    destroyRenderSchedule(schedule);
    CHECK(stepCount(schedule) == 0); // stale: nothing
    CHECK(insertManagedStep(schedule, "Test.UI", 2, StepPlacement::Last, "").status() == Status::NotFound);
    CHECK(setSurfaceSchedule({}, 0).status() == Status::NotFound); // no such surface
    setStepTrampoline(0);
}

TEST_CASE("Pipelines need the renderer")
{
    CHECK(createComputePipeline(probeShader, "fillColors").status() == Status::Unsupported);
    CHECK(createGraphicsPipeline(probeShader, "quadVertex", "quadPixel", {}).status() == Status::Unsupported);
    destroyPipeline({}); // nothing to destroy
    CHECK(pipelineCount() == 0);
}

TEST_SUITE("gpu")
{
    TEST_CASE("C# steps draw through command records on every backend")
    {
        REQUIRE(platform::initialize());
        test::library[probeShader.bytes[0]] = test::readFile(CRUCIBLE_SHADER_DIR "/StepProbe.crshader");
        assets::setAssetReader(reinterpret_cast<uint64_t>(&test::readTestAsset));
        setStepTrampoline(reinterpret_cast<uint64_t>(&trampoline));
        bodies[1] = probeStep;
        for (slag::Backend backend : {slag::Backend::D3D12, slag::Backend::Vulkan})
        {
            if (!slag::isBackendAvailable(backend) || !render::initialize(backend))
            {
                continue;
            }
            CAPTURE(backendName(backend));
            constexpr uint32_t size = 32;
            crucible::Result<SurfaceHandle> surface = createOffscreenSurface(size, size);
            REQUIRE(surface);

            // Pipelines: missing entry points and the wrong stage are refused.
            CHECK(createComputePipeline(probeShader, "quadVertex").status() == Status::InvalidData);
            CHECK(createGraphicsPipeline(probeShader, "missing", "quadPixel", {.colorCount = 1}).status() ==
                  Status::InvalidData);
            CHECK(createComputePipeline({{0xEE}}, "fillColors").status() == Status::NotFound);

            calls.clear();
            mistake = Mistake::None;
            REQUIRE(insertManagedStep(0, "Test.Probe", 1, StepPlacement::After, "Tonemap"));
            CHECK(calls == std::vector<std::string>{"Prepare"});
            CHECK(pipelineCount() == 2);

            // Black under the quads: Tonemap of a black clear.
            REQUIRE(renderFrame(surface.value(), nullptr, 0, 0, 0, 0, 1));
            std::vector<uint8_t> pixels(size * size * 4);
            auto pixel = [&](uint32_t x, uint32_t y) {
                const uint8_t* p = &pixels[(y * size + x) * 4];
                return std::vector<int>{p[2], p[1], p[0]}; // B8G8R8A8 to RGB
            };
            auto checkQuads = [&] {
                REQUIRE(readSurfacePixels(surface.value(), pixels));
                CHECK(pixel(8, 8) == std::vector<int>{255, 0, 0});
                CHECK(pixel(24, 8) == std::vector<int>{0, 255, 0});
                CHECK(pixel(8, 24) == std::vector<int>{0, 0, 255});
                CHECK(pixel(24, 24) == std::vector<int>{0, 0, 0}); // scissored away
            };
            checkQuads();

            // Each mistake is reported and fails only its frame.
            for (Mistake bad :
                 {Mistake::DrawWithoutPipeline, Mistake::ComputePipelineInGraphicsPass, Mistake::DeclaringAnEarlierPass,
                  Mistake::Throwing, Mistake::BadLength, Mistake::OpenLabel})
            {
                CAPTURE(static_cast<int>(bad));
                mistake = bad;
                CHECK(renderFrame(surface.value(), nullptr, 0, 0, 0, 0, 1).status() == Status::InvalidData);
                mistake = Mistake::None;
                REQUIRE(renderFrame(surface.value(), nullptr, 0, 0, 0, 0, 1));
            }
            // Through both frame slots again, with their own frame data.
            REQUIRE(renderFrame(surface.value(), nullptr, 0, 0, 0, 0, 1));
            checkQuads();

            // A sample count change prepares the steps again.
            calls.clear();
            REQUIRE(setMsaaSamples(2));
            REQUIRE(setMsaaSamples(4));
            CHECK(calls == std::vector<std::string>{"Release", "Prepare", "Release", "Prepare"});

            calls.clear();
            REQUIRE(removeStep(0, "Test.Probe"));
            CHECK(calls == std::vector<std::string>{"Release"});
            CHECK(pipelineCount() == 0);

            destroySurface(surface.value());
            render::shutdown();
        }
        setStepTrampoline(0);
        assets::setAssetReader(0);
        platform::shutdown();
    }
}
