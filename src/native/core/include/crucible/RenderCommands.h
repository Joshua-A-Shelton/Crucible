#pragma once

// The batched command API (ADR 0058): render steps written in C#. A C# step
// runs on the main thread while the frame is built, like a C++ step: it adds
// graph passes, declares what each uses, and writes each pass's commands
// into native memory as compact records. When the graph records the pass on
// a job thread, native code plays the records back as Slag calls, so C#
// never runs during recording and crosses into native code a few times per
// pass, not per draw.
//
// Per-frame data (instances, constants) goes into the frame data: uploads
// collected on the CPU while steps run, copied into one upload buffer per
// frame slot, which shaders read as a ByteAddressBuffer through
// StepFrameInfo::frameDataDescriptor. A constant buffer can be a slice of it.
//
// The engine's C# wraps these in Crucible.Render's RenderSchedule,
// IRenderStep, RenderStepContext, RenderPass, and RenderCommands; the
// functions marked internal are for it alone. Main thread only, like the
// renderer. Names are in docs/naming.md.

#include <crucible/Expose.h>
#include <crucible/Math.h>
#include <crucible/RenderAssets.h>
#include <crucible/RenderSchedule.h>
#include <crucible/Result.h>

#include <cstdint>
#include <span>
#include <string_view>

namespace crucible::render {

// ---------------------------------------------------------------------------
// Pipelines
// ---------------------------------------------------------------------------

// A pipeline made from a shader asset. Generation 0 is no pipeline.
struct CRUCIBLE_EXPOSE PipelineHandle
{
    uint32_t index;
    uint32_t generation;
};

// The texture formats C# steps can name: the engine's targets' and a few
// common ones. Undefined is no format (no depth target).
enum class CRUCIBLE_EXPOSE RenderFormat : uint32_t
{
    Undefined,
    R8Unorm,
    R8G8B8A8Unorm,
    R8G8B8A8UnormSrgb,
    B8G8R8A8UnormSrgb, // the backbuffer's
    R16G16B16A16Float, // SceneColor's
    R32Float,
    R32Uint,
    D32Float, // SceneDepth's
};

// How a pipeline's pixels combine with the target's.
enum class CRUCIBLE_EXPOSE BlendMode : uint32_t
{
    Opaque,        // replaces
    Alpha,         // straight alpha: color * a + target * (1 - a)
    Premultiplied, // color + target * (1 - a)
    Additive,      // color + target
};

enum class CRUCIBLE_EXPOSE CullMode : uint32_t
{
    None,
    Front,
    Back, // front faces are clockwise (ADR 0024)
};

// Numbered as Slag's CompareOp.
enum class CRUCIBLE_EXPOSE CompareOp : uint32_t
{
    Never,
    Less,
    Equal,
    LessEqual,
    Greater,
    NotEqual,
    GreaterEqual, // reverse-Z's nearer-or-equal
    Always,
};

enum class CRUCIBLE_EXPOSE Topology : uint32_t
{
    TriangleList,
    TriangleStrip,
};

// A graphics pipeline's fixed state. Vertices come from the shader (it reads
// the frame data or a buffer by index): there are no vertex buffers.
struct CRUCIBLE_EXPOSE GraphicsPipelineState
{
    BlendMode blend;
    CullMode cull;
    Topology topology;
    bool depthTest;
    bool depthWrite;
    CompareOp depthCompare;
    RenderFormat colorFormats[4];
    uint32_t colorCount;
    RenderFormat depthFormat;
    uint32_t sampleCount; // 0 counts as 1
};

// From the shader asset's entry points (read through the asset reader, like
// materials' shaders). Create pipelines in a step's prepare, never per frame
// (ADR 0025). InvalidData if the asset isn't a shader or lacks an entry
// point; Unsupported without a renderer.
CRUCIBLE_EXPOSE Result<PipelineHandle> createGraphicsPipeline(assets::AssetId shader, std::string_view vertexEntry,
                                                              std::string_view pixelEntry, GraphicsPipelineState state);
CRUCIBLE_EXPOSE Result<PipelineHandle> createComputePipeline(assets::AssetId shader, std::string_view entry);
// From one of the engine's embedded shaders ("ui"), for the engine's C#.
CRUCIBLE_EXPOSE_INTERNAL Result<PipelineHandle> createEngineGraphicsPipeline(std::string_view shader,
                                                                             std::string_view vertexEntry,
                                                                             std::string_view pixelEntry,
                                                                             GraphicsPipelineState state);
// Destroyed once the GPU has finished every frame that could use it.
CRUCIBLE_EXPOSE void destroyPipeline(PipelineHandle pipeline);
// Pipelines alive, for tests.
CRUCIBLE_EXPOSE_INTERNAL uint32_t pipelineCount();

// ---------------------------------------------------------------------------
// Steps
// ---------------------------------------------------------------------------

// What a C# step sees while it adds passes: valid during that call only.
struct CRUCIBLE_EXPOSE StepFrame;

// Where a C# step goes in the schedule.
enum class CRUCIBLE_EXPOSE StepPlacement : uint32_t
{
    Last,
    Before, // the anchor
    After,
    Replacing,
};

// What the engine's C# is called for, with the step's ID: 0 back means
// success. AddPasses gets the frame; the others get null.
enum class CRUCIBLE_EXPOSE StepCall : uint32_t
{
    Prepare,
    AddPasses,
    Release,
};

// The engine's C# entry for its steps: an [UnmanagedCallersOnly] method
// taking (step ID, StepCall, StepFrame*) and returning an int32.
CRUCIBLE_EXPOSE_INTERNAL void setStepTrampoline(uint64_t trampolineAddress);
// Schedules by ID (ADR 0066; RenderSchedule.h): 0 is the default. A new
// one holds Clear only; destroying one releases its steps, and its surfaces
// go back to the default.
CRUCIBLE_EXPOSE_INTERNAL uint32_t createRenderSchedule();
CRUCIBLE_EXPOSE_INTERNAL void destroyRenderSchedule(uint32_t schedule);
// Adds a C# step to a schedule, as RenderSchedule's add, insertBefore,
// insertAfter, or replace; prepared now when the renderer runs. NotFound for
// an unknown schedule.
CRUCIBLE_EXPOSE_INTERNAL Result<void> insertManagedStep(uint32_t schedule, std::string_view name, uint64_t id,
                                                        StepPlacement placement, std::string_view anchor);
// Any step, C++ or C#; a C# step's Release is called first.
CRUCIBLE_EXPOSE_INTERNAL Result<void> removeStep(uint32_t schedule, std::string_view name);
CRUCIBLE_EXPOSE_INTERNAL uint32_t stepCount(uint32_t schedule);
CRUCIBLE_EXPOSE_INTERNAL std::string_view stepName(uint32_t schedule, uint32_t index);

// ---------------------------------------------------------------------------
// One frame of a step (internal: RenderStepContext wraps these)
// ---------------------------------------------------------------------------

// Slag's RenderGraphTexture and RenderGraphBuffer: this frame only.
struct CRUCIBLE_EXPOSE GraphTexture
{
    uint32_t index;
};

struct CRUCIBLE_EXPOSE GraphBuffer
{
    uint32_t index;
};

struct CRUCIBLE_EXPOSE StepFrameInfo
{
    uint32_t width;
    uint32_t height;
    uint64_t frame;               // counts the surface's frames from 1
    uint32_t frameDataDescriptor; // the frame data's ByteAddressBuffer slot
    uint32_t msaaSamples;         // SceneColor's and SceneDepth's
};

enum class CRUCIBLE_EXPOSE PassQueue : uint32_t
{
    Graphics,
    Compute,
};

// Numbered as Slag's rendergraph::Usage.
enum class CRUCIBLE_EXPOSE ResourceUsage : uint32_t
{
    ShaderRead = 0,
    ShaderWrite = 1, // read-write
    ConstantBuffer = 6,
};

// Numbered as Slag's TextureUsage.
enum class CRUCIBLE_EXPOSE TextureUsage : uint32_t
{
    None = 0,
    RenderTarget = 1,
    DepthStencil = 2,
    ShaderWrite = 4,
};

struct CRUCIBLE_EXPOSE TransientTextureDesc
{
    RenderFormat format;
    uint32_t width;
    uint32_t height;
    uint32_t mipCount;    // 0 counts as 1
    uint32_t sampleCount; // 0 counts as 1
    uint32_t usage;       // TextureUsage bits
};

// Numbered as Slag's LoadOp and StoreOp.
enum class CRUCIBLE_EXPOSE LoadAction : uint32_t
{
    Load,
    Clear,
    Discard,
};

enum class CRUCIBLE_EXPOSE StoreAction : uint32_t
{
    Store,
    Discard,
};

// Memory handed to C#: `address` to `address + size`.
struct CRUCIBLE_EXPOSE StepMemory
{
    uint64_t address;
    uint32_t size;
    uint32_t offset; // frame data: where it starts in the frame data
};

CRUCIBLE_EXPOSE_INTERNAL StepFrameInfo stepFrameInfo(StepFrame& frame);
// The blackboard (ADR 0048); reading what no step published fails the frame,
// as for C++ steps.
CRUCIBLE_EXPOSE_INTERNAL GraphTexture stepTexture(StepFrame& frame, BlackboardKey key);
CRUCIBLE_EXPOSE_INTERNAL GraphBuffer stepBuffer(StepFrame& frame, BlackboardKey key);
CRUCIBLE_EXPOSE_INTERNAL void publishTexture(StepFrame& frame, BlackboardKey key, GraphTexture texture);
CRUCIBLE_EXPOSE_INTERNAL void publishBuffer(StepFrame& frame, BlackboardKey key, GraphBuffer buffer);
// Transients (Slag's): this frame only, contents undefined until written. A
// buffer's size is a multiple of 4.
CRUCIBLE_EXPOSE_INTERNAL GraphTexture createStepTexture(StepFrame& frame, TransientTextureDesc desc);
CRUCIBLE_EXPOSE_INTERNAL GraphBuffer createStepBuffer(StepFrame& frame, uint32_t size);

// A pass, by its number this frame. Its uses and attachments are declared
// before the step's next pass is added; its commands may be written until
// the step returns. Every pass also reads the frame's ViewConstants, bound as
// its constant buffer when it starts.
CRUCIBLE_EXPOSE_INTERNAL uint32_t addStepPass(StepFrame& frame, std::string_view name, PassQueue queue);
// The resource-heap slot the pass reads or writes it through (ShaderRead,
// ShaderWrite), written when the pass records; 0 for ConstantBuffer. A
// buffer gets a slot only if this step's frame made it (its size is known).
CRUCIBLE_EXPOSE_INTERNAL uint32_t useTexture(StepFrame& frame, uint32_t pass, GraphTexture texture,
                                             ResourceUsage usage);
CRUCIBLE_EXPOSE_INTERNAL uint32_t useBuffer(StepFrame& frame, uint32_t pass, GraphBuffer buffer, ResourceUsage usage);
CRUCIBLE_EXPOSE_INTERNAL void passRenderTarget(StepFrame& frame, uint32_t pass, GraphTexture texture, LoadAction load,
                                               StoreAction store, Vector4 clearColor);
CRUCIBLE_EXPOSE_INTERNAL void passDepthTarget(StepFrame& frame, uint32_t pass, GraphTexture texture, LoadAction load,
                                              StoreAction store, float clearDepth, bool readOnly);
// The pass's command records: records `used` bytes as written so far and
// returns room for at least `more` beyond them (the address may move: C#
// writes from the returned one). The step's last call for each pass, with
// `more` 0, is what plays back.
CRUCIBLE_EXPOSE_INTERNAL StepMemory passCommands(StepFrame& frame, uint32_t pass, uint32_t used, uint32_t more);
// `size` bytes of frame data, 256-aligned, valid until the step returns.
CRUCIBLE_EXPOSE_INTERNAL StepMemory uploadFrameData(StepFrame& frame, uint32_t size);

// ---------------------------------------------------------------------------
// Command records
// ---------------------------------------------------------------------------

// Each record is a RenderCommandHeader, then its command's struct (if it has
// one), then any bytes it carries, padded to 4 bytes: `words` is the
// record's whole length in 4-byte words. Records are checked when the step
// returns; a bad one is reported (naming the step, pass, and record) and
// fails the frame, so playback never meets one.
enum class CRUCIBLE_EXPOSE RenderCommandType : uint16_t
{
    SetPipeline,
    PushConstants, // + `size` bytes
    SetConstantBuffer,
    SetViewport,
    SetScissor,
    Draw,
    Dispatch,
    BeginLabel, // + `length` bytes of UTF-8, the last a NUL
    EndLabel,
};

struct CRUCIBLE_EXPOSE RenderCommandHeader
{
    RenderCommandType type;
    uint16_t words;
};

struct CRUCIBLE_EXPOSE SetPipelineCommand
{
    PipelineHandle pipeline;
};

// Up to 128 bytes, at a 4-byte multiple.
struct CRUCIBLE_EXPOSE PushConstantsCommand
{
    uint32_t offset;
    uint32_t size;
};

// A slice of the frame data (buffer index UINT32_MAX; offset 256-aligned),
// or of a buffer the pass uses as ConstantBuffer.
struct CRUCIBLE_EXPOSE SetConstantBufferCommand
{
    GraphBuffer buffer;
    uint32_t offset;
    uint32_t size;
};

struct CRUCIBLE_EXPOSE SetViewportCommand
{
    float x;
    float y;
    float width;
    float height;
    float minDepth;
    float maxDepth;
};

struct CRUCIBLE_EXPOSE SetScissorCommand
{
    int32_t x;
    int32_t y;
    uint32_t width;
    uint32_t height;
};

struct CRUCIBLE_EXPOSE DrawCommand
{
    uint32_t vertexCount;
    uint32_t instanceCount;
    uint32_t firstVertex;
    uint32_t firstInstance;
};

struct CRUCIBLE_EXPOSE DispatchCommand
{
    uint32_t x;
    uint32_t y;
    uint32_t z;
};

struct CRUCIBLE_EXPOSE BeginLabelCommand
{
    uint32_t length;
};

} // namespace crucible::render
