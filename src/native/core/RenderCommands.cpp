// The batched command API (ADR 0058): C# render steps, their passes'
// command records checked on the main thread when the step returns and
// played back as Slag calls when the graph records the pass, their
// pipelines, and the frame data they upload.

#include "RenderInternal.h"

#include <crucible/Memory.h>
#include <crucible/Render.h>
#include <crucible/RenderCommands.h>
#include <crucible/Shader.h>
#include <crucible/Vector.h>

#include <slag/Limits.h>

#include <algorithm>
#include <cstdio>
#include <cstring>
#include <new>

namespace crucible::render {

namespace {

constexpr uint32_t roundUp(uint32_t value, uint32_t alignment)
{
    return (value + alignment - 1) / alignment * alignment;
}

// The view's constants as a constant buffer, bound when every C# pass starts.
constexpr uint32_t viewConstantsSize = roundUp(sizeof(ViewConstants), slag::alignment::constantBuffer);

slag::PixelFormat pixelFormat(RenderFormat format)
{
    switch (format)
    {
    case RenderFormat::R8Unorm:
        return slag::PixelFormat::R8Unorm;
    case RenderFormat::R8G8B8A8Unorm:
        return slag::PixelFormat::R8G8B8A8Unorm;
    case RenderFormat::R8G8B8A8UnormSrgb:
        return slag::PixelFormat::R8G8B8A8UnormSrgb;
    case RenderFormat::B8G8R8A8UnormSrgb:
        return slag::PixelFormat::B8G8R8A8UnormSrgb;
    case RenderFormat::R16G16B16A16Float:
        return slag::PixelFormat::R16G16B16A16Float;
    case RenderFormat::R32Float:
        return slag::PixelFormat::R32Float;
    case RenderFormat::R32Uint:
        return slag::PixelFormat::R32Uint;
    case RenderFormat::D32Float:
        return slag::PixelFormat::D32Float;
    case RenderFormat::Undefined:
        break;
    }
    return slag::PixelFormat::Undefined;
}

bool validSampleCount(uint32_t count)
{
    return count == 1 || count == 2 || count == 4 || count == 8;
}

// ---------------------------------------------------------------------------
// Pipelines
// ---------------------------------------------------------------------------

struct PipelineEntry
{
    slag::Pipeline* pipeline = nullptr;
    uint32_t generation = 0; // odd while alive
    bool compute = false;
};

struct Pipelines
{
    Vector<PipelineEntry> entries{MemoryTag::Render};
    Vector<uint32_t> free{MemoryTag::Render};
    uint32_t alive = 0;
};

Pipelines& pipelines()
{
    static Pipelines table;
    return table;
}

const PipelineEntry* pipelineEntry(PipelineHandle handle)
{
    const Pipelines& table = pipelines();
    if (handle.index >= table.entries.size() || handle.generation == 0)
    {
        return nullptr;
    }
    const PipelineEntry& entry = table.entries[handle.index];
    return entry.generation == handle.generation ? &entry : nullptr;
}

PipelineHandle addPipeline(slag::Pipeline* pipeline, bool compute)
{
    Pipelines& table = pipelines();
    uint32_t index;
    if (!table.free.empty())
    {
        index = table.free.back();
        table.free.popBack();
    }
    else
    {
        index = static_cast<uint32_t>(table.entries.size());
        table.entries.pushBack({});
    }
    PipelineEntry& entry = table.entries[index];
    entry.pipeline = pipeline;
    entry.compute = compute;
    entry.generation += 1; // odd: alive
    ++table.alive;
    return {index, entry.generation};
}

// The shader asset, and the entry point `name` of `stage` in it.
struct LoadedShader
{
    ShaderFile* file = nullptr;
    ~LoadedShader()
    {
        if (file != nullptr)
        {
            destroyShaderFile(file);
        }
    }
};

Result<void> loadShader(const assets::AssetId& id, LoadedShader& out)
{
    Vector<uint8_t> bytes{MemoryTag::Render};
    if (Status read = readAsset(id, bytes); read != Status::Ok)
    {
        return read;
    }
    Result<ShaderFile*> file = loadShaderFile(bytes.span());
    if (!file)
    {
        return file.status();
    }
    out.file = file.value();
    return {};
}

int32_t findEntry(const ShaderFile& file, std::string_view name, ShaderStageKind stage)
{
    const int32_t index = findShaderEntry(file, name);
    return index >= 0 && shaderEntry(file, static_cast<uint32_t>(index)).stage == stage ? index : -1;
}

// A NUL-terminated copy, for Slag's debug names.
void terminated(std::string_view text, Vector<char>& out)
{
    out.assign(std::span<const char>(text.data(), text.size()));
    out.pushBack('\0');
}

slag::ColorTargetBlend blendOf(BlendMode mode)
{
    using F = slag::BlendFactor;
    switch (mode)
    {
    case BlendMode::Alpha:
        return {.blendEnable = true,
                .srcColorFactor = F::SrcAlpha,
                .destColorFactor = F::InvSrcAlpha,
                .srcAlphaFactor = F::One,
                .destAlphaFactor = F::InvSrcAlpha};
    case BlendMode::Premultiplied:
        return {.blendEnable = true,
                .srcColorFactor = F::One,
                .destColorFactor = F::InvSrcAlpha,
                .srcAlphaFactor = F::One,
                .destAlphaFactor = F::InvSrcAlpha};
    case BlendMode::Additive:
        return {.blendEnable = true,
                .srcColorFactor = F::One,
                .destColorFactor = F::One,
                .srcAlphaFactor = F::One,
                .destAlphaFactor = F::One};
    case BlendMode::Opaque:
        break;
    }
    return {};
}

// ---------------------------------------------------------------------------
// Steps
// ---------------------------------------------------------------------------

using StepTrampoline = int32_t (*)(uint64_t id, uint32_t call, StepFrame* frame);
StepTrampoline stepTrampoline = nullptr;

uint64_t stepId(void* userData)
{
    return static_cast<uint64_t>(reinterpret_cast<uintptr_t>(userData));
}

int32_t callStep(uint64_t id, StepCall call, StepFrame* frame)
{
    return stepTrampoline != nullptr ? stepTrampoline(id, static_cast<uint32_t>(call), frame) : 1;
}

} // namespace

// A resource-heap slot a pass reads or writes a graph resource through,
// written when the pass records.
struct DescriptorUse
{
    uint32_t slot;
    uint32_t resource;
    bool texture;
    bool write;
    uint32_t size; // buffers
};

// One C# pass this frame. Pooled: the vectors keep their capacity.
struct ManagedPass
{
    Vector<char> name{MemoryTag::Render}; // NUL-terminated; the graph keeps a pointer to it
    Vector<uint8_t> commands{MemoryTag::Render};
    uint32_t used = 0;
    PassQueue queue = PassQueue::Graphics;
    Vector<DescriptorUse> descriptors{MemoryTag::Render};
    Vector<uint32_t> constantBuffers{MemoryTag::Render}; // graph buffers used as ConstantBuffer
};

struct FrameDataChunk
{
    Vector<uint8_t> bytes{MemoryTag::Render};
    uint32_t used = 0;
    uint32_t base = 0; // its offset in the frame data
};

struct ManagedState
{
    uint32_t descriptorBase = 0;
    Vector<ManagedPass*> passes{MemoryTag::Render};
    uint32_t passCount = 0; // this frame's
    Vector<FrameDataChunk*> chunks{MemoryTag::Render};
    uint32_t chunkCount = 0;
    uint32_t frameDataSize = 0;
    // Transient buffers C# made this frame, with their sizes.
    Vector<DescriptorUse> buffers{MemoryTag::Render};
    uint32_t nextDescriptor = 0;
    uint32_t frameSlot = 0;
    std::array<slag::Buffer*, framesInFlight> frameData{};
    std::array<uint32_t, framesInFlight> frameDataCapacity{};

    ~ManagedState()
    {
        for (ManagedPass* pass : passes)
        {
            destroy(MemoryTag::Render, pass);
        }
        for (FrameDataChunk* chunk : chunks)
        {
            destroy(MemoryTag::Render, chunk);
        }
    }
};

// What a C# step's AddPasses sees.
struct StepFrame
{
    RenderStepContext* context = nullptr;
    ManagedState* state = nullptr;
    std::string_view step;
    uint32_t firstPass = 0; // the step's first pass this frame
    uint32_t declaring = 0; // the pass being declared, if `builder` holds it
    bool hasBuilder = false;
    slag::rendergraph::RenderGraphBuffer view{};
    alignas(slag::rendergraph::PassBuilder) unsigned char builder[sizeof(slag::rendergraph::PassBuilder)];

    slag::rendergraph::PassBuilder& passBuilder()
    {
        return *std::launder(reinterpret_cast<slag::rendergraph::PassBuilder*>(builder));
    }
};

namespace {

void report(const StepFrame& frame, const char* format, const char* detail)
{
    std::fprintf(stderr, "crucible: render step \"%.*s\": ", static_cast<int>(frame.step.size()), frame.step.data());
    std::fprintf(stderr, format, detail);
    std::fputc('\n', stderr);
}

// The pass being declared, or null (reported, the frame failed).
slag::rendergraph::PassBuilder* declaring(StepFrame& frame, uint32_t pass)
{
    if (!frame.hasBuilder || pass != frame.declaring)
    {
        report(frame, "%s", "a pass's uses and attachments are declared before the step's next pass is added");
        failFrame(*frame.context);
        return nullptr;
    }
    return &frame.passBuilder();
}

ManagedPass* stepPass(StepFrame& frame, uint32_t pass)
{
    if (pass < frame.firstPass || pass >= frame.state->passCount)
    {
        report(frame, "%s", "not a pass of this step's frame");
        failFrame(*frame.context);
        return nullptr;
    }
    return frame.state->passes[pass];
}

uint32_t takeDescriptor(StepFrame& frame)
{
    ManagedState& state = *frame.state;
    if (state.nextDescriptor == stepDescriptorsPerFrame)
    {
        report(frame, "%s", "C# passes use more than 40 resource slots this frame");
        failFrame(*frame.context);
        return 0;
    }
    return state.descriptorBase + stepDescriptors + state.frameSlot * stepDescriptorsPerFrame + state.nextDescriptor++;
}

// ---------------------------------------------------------------------------
// Checking records
// ---------------------------------------------------------------------------

const char* commandName(RenderCommandType type)
{
    switch (type)
    {
    case RenderCommandType::SetPipeline:
        return "SetPipeline";
    case RenderCommandType::PushConstants:
        return "PushConstants";
    case RenderCommandType::SetConstantBuffer:
        return "SetConstantBuffer";
    case RenderCommandType::SetViewport:
        return "SetViewport";
    case RenderCommandType::SetScissor:
        return "SetScissor";
    case RenderCommandType::Draw:
        return "Draw";
    case RenderCommandType::Dispatch:
        return "Dispatch";
    case RenderCommandType::BeginLabel:
        return "BeginLabel";
    case RenderCommandType::EndLabel:
        return "EndLabel";
    }
    return "an unknown command";
}

// The fixed part each record type has after its header.
uint32_t payloadSize(RenderCommandType type)
{
    switch (type)
    {
    case RenderCommandType::SetPipeline:
        return sizeof(SetPipelineCommand);
    case RenderCommandType::PushConstants:
        return sizeof(PushConstantsCommand);
    case RenderCommandType::SetConstantBuffer:
        return sizeof(SetConstantBufferCommand);
    case RenderCommandType::SetViewport:
        return sizeof(SetViewportCommand);
    case RenderCommandType::SetScissor:
        return sizeof(SetScissorCommand);
    case RenderCommandType::Draw:
        return sizeof(DrawCommand);
    case RenderCommandType::Dispatch:
        return sizeof(DispatchCommand);
    case RenderCommandType::BeginLabel:
        return sizeof(BeginLabelCommand);
    case RenderCommandType::EndLabel:
        return 0;
    }
    return UINT32_MAX;
}

template <class T>
T readAt(const uint8_t* at)
{
    T value;
    std::memcpy(&value, at, sizeof(T));
    return value;
}

// Every record of the pass, as playback will meet them. Reports the first
// bad one.
bool checkPass(const StepFrame& frame, const ManagedPass& pass)
{
    const uint8_t* const begin = pass.commands.data();
    const bool graphics = pass.queue == PassQueue::Graphics;
    uint32_t at = 0;
    uint32_t record = 0;
    bool pipelineSet = false;
    int32_t labels = 0;
    auto fail = [&](const char* what, RenderCommandType type) {
        std::fprintf(stderr, "crucible: render step \"%.*s\", pass \"%s\", record %u (%s): %s\n",
                     static_cast<int>(frame.step.size()), frame.step.data(), pass.name.data(), record,
                     commandName(type), what);
        return false;
    };
    while (at < pass.used)
    {
        if (pass.used - at < sizeof(RenderCommandHeader))
        {
            return fail("cut short", RenderCommandType::EndLabel);
        }
        const auto header = readAt<RenderCommandHeader>(begin + at);
        const uint32_t length = header.words * 4u;
        const uint32_t payload = payloadSize(header.type);
        if (payload == UINT32_MAX)
        {
            return fail("not a command", header.type);
        }
        if (length < sizeof(RenderCommandHeader) + payload || length > pass.used - at)
        {
            return fail("the wrong length", header.type);
        }
        const uint8_t* data = begin + at + sizeof(RenderCommandHeader);
        const uint32_t extra = length - static_cast<uint32_t>(sizeof(RenderCommandHeader)) - payload;
        switch (header.type)
        {
        case RenderCommandType::SetPipeline:
        {
            const PipelineEntry* entry = pipelineEntry(readAt<SetPipelineCommand>(data).pipeline);
            if (entry == nullptr)
            {
                return fail("a stale or empty pipeline", header.type);
            }
            if (entry->compute == graphics)
            {
                return fail(graphics ? "a compute pipeline in a graphics pass"
                                     : "a graphics pipeline in a compute pass",
                            header.type);
            }
            pipelineSet = true;
            break;
        }
        case RenderCommandType::PushConstants:
        {
            const auto command = readAt<PushConstantsCommand>(data);
            if (command.offset % 4 != 0 || command.size % 4 != 0 ||
                command.offset + command.size > slag::limits::maxPushConstantsSize || command.size > extra)
            {
                return fail("past 128 bytes, or not at 4-byte multiples", header.type);
            }
            break;
        }
        case RenderCommandType::SetConstantBuffer:
        {
            const auto command = readAt<SetConstantBufferCommand>(data);
            if (command.offset % slag::alignment::constantBuffer != 0 || command.size == 0 ||
                command.size > slag::limits::maxConstantBufferSize)
            {
                return fail("its offset isn't 256-aligned, or its size is 0 or past the limit", header.type);
            }
            if (command.buffer.index == UINT32_MAX)
            {
                if (command.offset + command.size > frame.state->frameDataSize)
                {
                    return fail("past the frame data", header.type);
                }
            }
            else if (std::find(pass.constantBuffers.begin(), pass.constantBuffers.end(), command.buffer.index) ==
                     pass.constantBuffers.end())
            {
                return fail("a buffer the pass doesn't use as ConstantBuffer", header.type);
            }
            break;
        }
        case RenderCommandType::SetViewport:
        case RenderCommandType::SetScissor:
            if (!graphics)
            {
                return fail("in a compute pass", header.type);
            }
            break;
        case RenderCommandType::Draw:
        case RenderCommandType::Dispatch:
            if ((header.type == RenderCommandType::Draw) != graphics)
            {
                return fail(graphics ? "in a graphics pass" : "in a compute pass", header.type);
            }
            if (!pipelineSet)
            {
                return fail("no pipeline set", header.type);
            }
            break;
        case RenderCommandType::BeginLabel:
        {
            const auto command = readAt<BeginLabelCommand>(data);
            if (command.length == 0 || command.length > extra || data[payload + command.length - 1] != 0)
            {
                return fail("its text isn't NUL-terminated", header.type);
            }
            ++labels;
            break;
        }
        case RenderCommandType::EndLabel:
            if (--labels < 0)
            {
                return fail("no label to end", header.type);
            }
            break;
        }
        at += length;
        ++record;
    }
    if (labels != 0)
    {
        return fail("a label isn't ended", RenderCommandType::BeginLabel);
    }
    return true;
}

// ---------------------------------------------------------------------------
// Playback, on a job thread
// ---------------------------------------------------------------------------

void writeDescriptors(slag::GraphicsDevice& device, const ManagedPass& pass, slag::rendergraph::PassContext& context)
{
    for (const DescriptorUse& use : pass.descriptors)
    {
        if (use.texture)
        {
            slag::Texture* texture = context.texture({use.resource});
            const bool layered = texture->layerCount() > 1;
            if (use.write)
            {
                device.writeRWTexture(use.slot, {.texture = texture,
                                                 .viewType = layered ? slag::TextureViewType::Texture2DArray
                                                                     : slag::TextureViewType::Texture2D});
            }
            else
            {
                const slag::TextureViewType type = texture->sampleCount() > 1 ? slag::TextureViewType::Texture2DMS
                                                   : layered                  ? slag::TextureViewType::Texture2DArray
                                                                              : slag::TextureViewType::Texture2D;
                device.writeTexture(use.slot, {.texture = texture, .viewType = type});
            }
        }
        else
        {
            const slag::RawBufferDescriptor raw{.buffer = context.buffer({use.resource}), .size = use.size};
            if (use.write)
            {
                device.writeRWRawBuffer(use.slot, raw);
            }
            else
            {
                device.writeRawBuffer(use.slot, raw);
            }
        }
    }
}

void play(const ManagedPass& pass, slag::rendergraph::PassContext& context, slag::CommandBuffer* commands,
          const slag::Buffer* frameData)
{
    const uint8_t* const begin = pass.commands.data();
    for (uint32_t at = 0; at < pass.used;)
    {
        const auto header = readAt<RenderCommandHeader>(begin + at);
        const uint8_t* data = begin + at + sizeof(RenderCommandHeader);
        at += header.words * 4u;
        switch (header.type)
        {
        case RenderCommandType::SetPipeline:
            commands->bindPipeline(pipelineEntry(readAt<SetPipelineCommand>(data).pipeline)->pipeline);
            break;
        case RenderCommandType::PushConstants:
        {
            const auto command = readAt<PushConstantsCommand>(data);
            commands->pushConstants(data + sizeof(command), command.size, command.offset);
            break;
        }
        case RenderCommandType::SetConstantBuffer:
        {
            const auto command = readAt<SetConstantBufferCommand>(data);
            const uint64_t base = command.buffer.index == UINT32_MAX
                                      ? frameData->gpuAddress()
                                      : context.buffer({command.buffer.index})->gpuAddress();
            commands->setConstantBuffer(base + command.offset, roundUp(command.size, slag::alignment::constantBuffer));
            break;
        }
        case RenderCommandType::SetViewport:
        {
            const auto command = readAt<SetViewportCommand>(data);
            const slag::Viewport viewport{command.x,      command.y,        command.width,
                                          command.height, command.minDepth, command.maxDepth};
            commands->setViewports({&viewport, 1});
            break;
        }
        case RenderCommandType::SetScissor:
        {
            const auto command = readAt<SetScissorCommand>(data);
            const slag::Rect rect{command.x, command.y, command.width, command.height};
            commands->setScissors({&rect, 1});
            break;
        }
        case RenderCommandType::Draw:
        {
            const auto command = readAt<DrawCommand>(data);
            commands->draw(command.vertexCount, command.instanceCount, command.firstVertex, command.firstInstance);
            break;
        }
        case RenderCommandType::Dispatch:
        {
            const auto command = readAt<DispatchCommand>(data);
            commands->dispatch(command.x, command.y, command.z);
            break;
        }
        case RenderCommandType::BeginLabel:
            commands->beginLabel(reinterpret_cast<const char*>(data + sizeof(BeginLabelCommand)));
            break;
        case RenderCommandType::EndLabel:
            commands->endLabel();
            break;
        }
    }
}

// ---------------------------------------------------------------------------
// The C# step's RenderStep
// ---------------------------------------------------------------------------

Result<void> prepareManagedStep(slag::GraphicsDevice&, void* userData)
{
    return callStep(stepId(userData), StepCall::Prepare, nullptr) == 0 ? Result<void>() : Status::InvalidData;
}

void releaseManagedStep(slag::GraphicsDevice&, void* userData)
{
    (void)callStep(stepId(userData), StepCall::Release, nullptr);
}

void addManagedPasses(RenderStepContext& context, void* userData)
{
    StepFrame frame;
    frame.context = &context;
    frame.state = &managedState(context);
    frame.step = context.stepName();
    frame.firstPass = frame.state->passCount;
    frame.view = context.buffer(viewKey());
    const int32_t result = callStep(stepId(userData), StepCall::AddPasses, &frame);
    bool good = result == 0;
    for (uint32_t i = frame.firstPass; good && i < frame.state->passCount; ++i)
    {
        good = checkPass(frame, *frame.state->passes[i]);
    }
    if (!good)
    {
        failFrame(context);
    }
}

} // namespace

// ---------------------------------------------------------------------------
// Pipelines
// ---------------------------------------------------------------------------

namespace {

Result<PipelineHandle> graphicsPipeline(const ShaderFile& file, std::string_view vertexEntry,
                                        std::string_view pixelEntry, const GraphicsPipelineState& state)
{
    slag::GraphicsDevice* device = renderDevice();
    const uint32_t samples = state.sampleCount == 0 ? 1 : state.sampleCount;
    if (state.colorCount > 4 || !validSampleCount(samples))
    {
        return Status::InvalidData;
    }
    const int32_t vertex = findEntry(file, vertexEntry, ShaderStageKind::Vertex);
    const int32_t pixel = pixelEntry.empty() ? -1 : findEntry(file, pixelEntry, ShaderStageKind::Pixel);
    if (vertex < 0 || (!pixelEntry.empty() && pixel < 0))
    {
        return Status::InvalidData;
    }
    slag::PixelFormat formats[4] = {};
    slag::GraphicsPipelineDesc desc;
    for (uint32_t i = 0; i < state.colorCount; ++i)
    {
        formats[i] = pixelFormat(state.colorFormats[i]);
        if (formats[i] == slag::PixelFormat::Undefined)
        {
            return Status::InvalidData;
        }
        desc.blend.targets[i] = blendOf(state.blend);
    }
    shaderStage(file, static_cast<uint32_t>(vertex), desc.vertex);
    if (pixel >= 0)
    {
        shaderStage(file, static_cast<uint32_t>(pixel), desc.pixel);
    }
    Vector<char> name{MemoryTag::Render};
    terminated(vertexEntry, name);
    desc.topology = state.topology == Topology::TriangleStrip ? slag::PrimitiveTopology::TriangleStrip
                                                              : slag::PrimitiveTopology::TriangleList;
    desc.rasterizer.cullMode = static_cast<slag::CullMode>(state.cull);
    desc.depthStencil = {.depthTest = state.depthTest,
                         .depthWrite = state.depthWrite,
                         .depthCompareOp = static_cast<slag::CompareOp>(state.depthCompare)};
    desc.colorFormats = {formats, state.colorCount};
    desc.depthStencilFormat = pixelFormat(state.depthFormat);
    desc.sampleCount = samples;
    desc.debugName = name.data();
    slag::Result<slag::Pipeline*> pipeline = device->createGraphicsPipeline(desc);
    if (!pipeline)
    {
        return Status::Unsupported;
    }
    return addPipeline(pipeline.value(), false);
}

} // namespace

Result<PipelineHandle> createGraphicsPipeline(assets::AssetId shader, std::string_view vertexEntry,
                                              std::string_view pixelEntry, GraphicsPipelineState state)
{
    if (renderDevice() == nullptr)
    {
        return Status::Unsupported;
    }
    LoadedShader loaded;
    if (Result<void> read = loadShader(shader, loaded); !read)
    {
        return read.status();
    }
    return graphicsPipeline(*loaded.file, vertexEntry, pixelEntry, state);
}

Result<PipelineHandle> createEngineGraphicsPipeline(std::string_view shader, std::string_view vertexEntry,
                                                    std::string_view pixelEntry, GraphicsPipelineState state)
{
    if (renderDevice() == nullptr)
    {
        return Status::Unsupported;
    }
    Result<ShaderFile*> file = loadShaderFile(engineShader(shader));
    if (!file)
    {
        return file.status();
    }
    LoadedShader loaded;
    loaded.file = file.value();
    return graphicsPipeline(*loaded.file, vertexEntry, pixelEntry, state);
}

Result<PipelineHandle> createComputePipeline(assets::AssetId shader, std::string_view entry)
{
    slag::GraphicsDevice* device = renderDevice();
    if (device == nullptr)
    {
        return Status::Unsupported;
    }
    LoadedShader loaded;
    if (Result<void> read = loadShader(shader, loaded); !read)
    {
        return read.status();
    }
    const int32_t index = findEntry(*loaded.file, entry, ShaderStageKind::Compute);
    if (index < 0)
    {
        return Status::InvalidData;
    }
    Vector<char> name{MemoryTag::Render};
    terminated(entry, name);
    slag::ComputePipelineDesc desc;
    shaderStage(*loaded.file, static_cast<uint32_t>(index), desc.compute);
    desc.debugName = name.data();
    slag::Result<slag::Pipeline*> pipeline = device->createComputePipeline(desc);
    if (!pipeline)
    {
        return Status::Unsupported;
    }
    return addPipeline(pipeline.value(), true);
}

void destroyPipeline(PipelineHandle pipeline)
{
    if (pipelineEntry(pipeline) == nullptr)
    {
        return;
    }
    Pipelines& table = pipelines();
    PipelineEntry& entry = table.entries[pipeline.index];
    destroyAfterWork(entry.pipeline);
    entry.pipeline = nullptr;
    entry.generation += 1; // even: free
    table.free.pushBack(pipeline.index);
    --table.alive;
}

uint32_t pipelineCount()
{
    return pipelines().alive;
}

void stopPipelines()
{
    Pipelines& table = pipelines();
    slag::GraphicsDevice* device = renderDevice();
    for (uint32_t i = 0; i < table.entries.size(); ++i)
    {
        PipelineEntry& entry = table.entries[i];
        if (entry.pipeline != nullptr)
        {
            device->destroy(entry.pipeline); // the GPU is idle
            entry.pipeline = nullptr;
            entry.generation += 1;
            table.free.pushBack(i);
        }
    }
    table.alive = 0;
}

// ---------------------------------------------------------------------------
// Steps
// ---------------------------------------------------------------------------

void setStepTrampoline(uint64_t trampolineAddress)
{
    stepTrampoline = reinterpret_cast<StepTrampoline>(static_cast<uintptr_t>(trampolineAddress));
}

Result<void> insertManagedStep(std::string_view name, uint64_t id, StepPlacement placement, std::string_view anchor)
{
    Vector<char> terminatedName{MemoryTag::Render};
    terminated(name, terminatedName);
    const RenderStep step{.name = terminatedName.data(),
                          .addPasses = addManagedPasses,
                          .userData = reinterpret_cast<void*>(static_cast<uintptr_t>(id)),
                          .prepare = prepareManagedStep,
                          .release = releaseManagedStep};
    RenderSchedule& schedule = renderSchedule();
    switch (placement)
    {
    case StepPlacement::Last:
        return schedule.add(step);
    case StepPlacement::Before:
        return schedule.insertBefore(anchor, step);
    case StepPlacement::After:
        return schedule.insertAfter(anchor, step);
    case StepPlacement::Replacing:
        return schedule.replace(anchor, step);
    }
    return Status::InvalidData;
}

Result<void> removeStep(std::string_view name)
{
    return renderSchedule().remove(name);
}

uint32_t stepCount()
{
    return renderSchedule().stepCount();
}

std::string_view stepName(uint32_t index)
{
    const RenderSchedule& schedule = renderSchedule();
    return index < schedule.stepCount() ? schedule.stepName(index) : std::string_view();
}

// ---------------------------------------------------------------------------
// One frame of a step
// ---------------------------------------------------------------------------

StepFrameInfo stepFrameInfo(StepFrame& frame)
{
    const RenderStepContext& context = *frame.context;
    const ManagedState& state = *frame.state;
    return {.width = context.width,
            .height = context.height,
            .frame = context.frame,
            .frameDataDescriptor = state.descriptorBase + frameDataDescriptor + state.frameSlot,
            .msaaSamples = msaaSamples()};
}

GraphTexture stepTexture(StepFrame& frame, BlackboardKey key)
{
    return {frame.context->texture(key).index};
}

GraphBuffer stepBuffer(StepFrame& frame, BlackboardKey key)
{
    return {frame.context->buffer(key).index};
}

void publishTexture(StepFrame& frame, BlackboardKey key, GraphTexture texture)
{
    frame.context->publish(key, slag::rendergraph::RenderGraphTexture{texture.index});
}

void publishBuffer(StepFrame& frame, BlackboardKey key, GraphBuffer buffer)
{
    frame.context->publish(key, slag::rendergraph::RenderGraphBuffer{buffer.index});
}

GraphTexture createStepTexture(StepFrame& frame, TransientTextureDesc desc)
{
    const uint32_t samples = desc.sampleCount == 0 ? 1 : desc.sampleCount;
    const slag::PixelFormat format = pixelFormat(desc.format);
    if (format == slag::PixelFormat::Undefined || desc.width == 0 || desc.height == 0 || !validSampleCount(samples) ||
        (desc.usage & ~7u) != 0)
    {
        report(frame, "%s", "a transient texture needs a format, a size, and 1, 2, 4, or 8 samples");
        failFrame(*frame.context);
        return {UINT32_MAX};
    }
    return {frame.context->graph
                .createTransientTexture2D({.format = format,
                                           .width = desc.width,
                                           .height = desc.height,
                                           .mipCount = desc.mipCount == 0 ? 1 : desc.mipCount,
                                           .sampleCount = samples,
                                           .usage = static_cast<slag::TextureUsage>(desc.usage)})
                .index};
}

GraphBuffer createStepBuffer(StepFrame& frame, uint32_t size)
{
    if (size == 0 || size % 4 != 0)
    {
        report(frame, "%s", "a transient buffer's size is a nonzero multiple of 4");
        failFrame(*frame.context);
        return {UINT32_MAX};
    }
    const uint32_t index = frame.context->graph.createTransientBuffer({.size = size}).index;
    frame.state->buffers.pushBack({.slot = 0, .resource = index, .texture = false, .write = false, .size = size});
    return {index};
}

uint32_t addStepPass(StepFrame& frame, std::string_view name, PassQueue queue)
{
    ManagedState& state = *frame.state;
    if (state.passCount == state.passes.size())
    {
        state.passes.pushBack(create<ManagedPass>(MemoryTag::Render));
    }
    const uint32_t index = state.passCount++;
    ManagedPass& pass = *state.passes[index];
    terminated(name, pass.name);
    pass.used = 0;
    pass.queue = queue;
    pass.descriptors.clear();
    pass.constantBuffers.clear();

    ManagedState* statePointer = &state;
    slag::GraphicsDevice* device = &frame.context->device;
    const slag::rendergraph::RenderGraphBuffer view = frame.view;
    frame.context->graph.addPass(
        pass.name.data(),
        queue == PassQueue::Compute ? slag::CommandQueueType::Compute : slag::CommandQueueType::Graphics,
        [&](slag::rendergraph::PassBuilder& builder) {
            new (frame.builder) slag::rendergraph::PassBuilder(builder);
            builder.use(view, slag::rendergraph::Usage::ConstantBuffer);
        },
        [statePointer, index, device, view](slag::rendergraph::PassContext& context, slag::CommandBuffer* commands) {
            const ManagedPass& recorded = *statePointer->passes[index];
            writeDescriptors(*device, recorded, context);
            commands->setConstantBuffer(context.buffer(view)->gpuAddress(), viewConstantsSize);
            play(recorded, context, commands, statePointer->frameData[statePointer->frameSlot]);
        });
    frame.hasBuilder = true;
    frame.declaring = index;
    return index;
}

uint32_t useTexture(StepFrame& frame, uint32_t pass, GraphTexture texture, ResourceUsage usage)
{
    slag::rendergraph::PassBuilder* builder = declaring(frame, pass);
    if (builder == nullptr)
    {
        return 0;
    }
    if (usage == ResourceUsage::ConstantBuffer)
    {
        report(frame, "%s", "a texture can't be a constant buffer");
        failFrame(*frame.context);
        return 0;
    }
    builder->use(slag::rendergraph::RenderGraphTexture{texture.index}, static_cast<slag::rendergraph::Usage>(usage));
    const uint32_t slot = takeDescriptor(frame);
    if (slot != 0)
    {
        frame.state->passes[pass]->descriptors.pushBack(
            {.slot = slot, .resource = texture.index, .texture = true, .write = usage == ResourceUsage::ShaderWrite});
    }
    return slot;
}

uint32_t useBuffer(StepFrame& frame, uint32_t pass, GraphBuffer buffer, ResourceUsage usage)
{
    slag::rendergraph::PassBuilder* builder = declaring(frame, pass);
    if (builder == nullptr)
    {
        return 0;
    }
    builder->use(slag::rendergraph::RenderGraphBuffer{buffer.index}, static_cast<slag::rendergraph::Usage>(usage));
    ManagedPass& entry = *frame.state->passes[pass];
    if (usage == ResourceUsage::ConstantBuffer)
    {
        entry.constantBuffers.pushBack(buffer.index);
        return 0;
    }
    const Vector<DescriptorUse>& made = frame.state->buffers;
    const auto found =
        std::find_if(made.begin(), made.end(), [&](const DescriptorUse& use) { return use.resource == buffer.index; });
    if (found == made.end())
    {
        return 0; // not made by a C# step this frame: no size to describe it with
    }
    const uint32_t slot = takeDescriptor(frame);
    if (slot != 0)
    {
        entry.descriptors.pushBack({.slot = slot,
                                    .resource = buffer.index,
                                    .texture = false,
                                    .write = usage == ResourceUsage::ShaderWrite,
                                    .size = found->size});
    }
    return slot;
}

void passRenderTarget(StepFrame& frame, uint32_t pass, GraphTexture texture, LoadAction load, StoreAction store,
                      Vector4 clearColor)
{
    if (slag::rendergraph::PassBuilder* builder = declaring(frame, pass))
    {
        slag::ClearValue clear;
        std::memcpy(clear.color, &clearColor, sizeof(clear.color));
        builder->renderTarget(slag::rendergraph::RenderGraphTexture{texture.index}, static_cast<slag::LoadOp>(load),
                              static_cast<slag::StoreOp>(store), clear);
    }
}

void passDepthTarget(StepFrame& frame, uint32_t pass, GraphTexture texture, LoadAction load, StoreAction store,
                     float clearDepth, bool readOnly)
{
    if (slag::rendergraph::PassBuilder* builder = declaring(frame, pass))
    {
        slag::ClearValue clear;
        clear.depth = clearDepth;
        builder->depthStencil(slag::rendergraph::RenderGraphTexture{texture.index}, static_cast<slag::LoadOp>(load),
                              static_cast<slag::StoreOp>(store), clear, readOnly);
    }
}

StepMemory passCommands(StepFrame& frame, uint32_t pass, uint32_t used, uint32_t more)
{
    ManagedPass* entry = stepPass(frame, pass);
    if (entry == nullptr)
    {
        return {};
    }
    if (used > entry->commands.size() || used % 4 != 0)
    {
        report(frame, "%s", "more command bytes recorded than were handed out");
        failFrame(*frame.context);
        return {};
    }
    entry->used = used;
    const size_t needed = static_cast<size_t>(used) + more;
    if (needed > entry->commands.size())
    {
        entry->commands.resize(std::max<size_t>({needed, entry->commands.size() * 2, 256}));
    }
    return {.address = reinterpret_cast<uint64_t>(entry->commands.data()),
            .size = static_cast<uint32_t>(entry->commands.size()),
            .offset = 0};
}

StepMemory uploadFrameData(StepFrame& frame, uint32_t size)
{
    constexpr uint32_t chunkSize = 64 * 1024;
    ManagedState& state = *frame.state;
    FrameDataChunk* chunk = state.chunkCount > 0 ? state.chunks[state.chunkCount - 1] : nullptr;
    uint32_t local = chunk != nullptr ? roundUp(chunk->used, slag::alignment::constantBuffer) : 0;
    if (chunk == nullptr || local + size > chunk->bytes.size())
    {
        // A new chunk, starting where the last one's data ends.
        const uint32_t base = roundUp(state.frameDataSize, slag::alignment::constantBuffer);
        if (state.chunkCount == state.chunks.size())
        {
            state.chunks.pushBack(create<FrameDataChunk>(MemoryTag::Render));
        }
        chunk = state.chunks[state.chunkCount++];
        if (chunk->bytes.size() < std::max(size, chunkSize))
        {
            chunk->bytes.resize(std::max(size, chunkSize));
        }
        chunk->base = base;
        chunk->used = 0;
        local = 0;
    }
    chunk->used = local + size;
    state.frameDataSize = chunk->base + chunk->used;
    return {.address = reinterpret_cast<uint64_t>(chunk->bytes.data() + local),
            .size = size,
            .offset = chunk->base + local};
}

// ---------------------------------------------------------------------------
// The surface's state
// ---------------------------------------------------------------------------

ManagedState* createManagedState(uint32_t descriptorBase)
{
    ManagedState* state = create<ManagedState>(MemoryTag::Render);
    state->descriptorBase = descriptorBase;
    return state;
}

void destroyManagedState(ManagedState* state)
{
    if (state == nullptr)
    {
        return;
    }
    slag::GraphicsDevice* device = renderDevice();
    for (slag::Buffer* buffer : state->frameData)
    {
        if (buffer != nullptr)
        {
            device->destroy(buffer);
        }
    }
    destroy(MemoryTag::Render, state);
}

void beginManagedFrame(ManagedState& state, uint32_t frameSlot)
{
    state.frameSlot = frameSlot;
    state.passCount = 0;
    state.chunkCount = 0;
    state.frameDataSize = 0;
    state.buffers.clear();
    state.nextDescriptor = 0;
}

Result<void> finishManagedFrame(ManagedState& state, uint32_t frameSlot)
{
    // The frame slot's previous frame has finished (the graph's beginFrame
    // waited), so its buffer can be replaced.
    const uint32_t size = roundUp(state.frameDataSize, slag::alignment::constantBuffer);
    slag::Buffer*& buffer = state.frameData[frameSlot];
    uint32_t& capacity = state.frameDataCapacity[frameSlot];
    if (buffer == nullptr || size > capacity)
    {
        slag::GraphicsDevice* device = renderDevice();
        uint32_t grown = 64 * 1024;
        while (grown < size)
        {
            grown *= 2;
        }
        slag::Result<slag::Buffer*> created =
            device->createBuffer({.size = grown,
                                  .placement = slag::Placement::automatic(slag::MemoryKind::Upload),
                                  .debugName = "crucible frame data"});
        if (!created)
        {
            return Status::OutOfDeviceMemory;
        }
        if (buffer != nullptr)
        {
            device->destroy(buffer);
        }
        buffer = created.value();
        capacity = grown;
        device->writeRawBuffer(state.descriptorBase + frameDataDescriptor + frameSlot,
                               {.buffer = buffer, .size = capacity});
    }
    auto* mapped = static_cast<uint8_t*>(buffer->mappedData());
    for (uint32_t i = 0; i < state.chunkCount; ++i)
    {
        const FrameDataChunk& chunk = *state.chunks[i];
        std::memcpy(mapped + chunk.base, chunk.bytes.data(), chunk.used);
    }
    return {};
}

} // namespace crucible::render
