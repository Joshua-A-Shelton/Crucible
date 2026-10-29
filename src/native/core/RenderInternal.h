#pragma once

// Between the renderer (Render.cpp), the render schedule, and the default
// steps (ADR 0048). Internal.

#include <crucible/RenderAssets.h>
#include <crucible/RenderSchedule.h>

#include "shared.g.h" // ViewConstants, generated from shaders/crucible/shared.slang

namespace crucible::ecs {
struct World;
}

namespace crucible::render {

// The CPU builds one frame while the GPU may still run the previous one.
inline constexpr uint32_t framesInFlight = 2;

// Each surface's reserved resource-heap slots, below the loaded textures'
// (which start at 1024): `descriptorsPerSurface` from 8 + surface index *
// descriptorsPerSurface, one per frame slot for each of these.
inline constexpr uint32_t descriptorsPerSurface = 8;
inline constexpr uint32_t transformsDescriptor = 0;               // + frame slot
inline constexpr uint32_t instancesDescriptor = framesInFlight;   // + frame slot
inline constexpr uint32_t resolveDescriptor = 2 * framesInFlight; // + frame slot
static_assert(3 * framesInFlight <= descriptorsPerSurface);

// The surface's slot `which` (one of the above, plus the frame slot).
uint32_t surfaceDescriptor(const RenderStepContext& context, uint32_t which);

struct FrameInputs
{
    slag::rendergraph::RenderGraph& graph;
    slag::GraphicsDevice& device;
    uint32_t width;
    uint32_t height;
    uint64_t frame;
    const float* clearColor; // 4
    slag::rendergraph::RenderGraphTexture backbuffer;
    slag::rendergraph::RenderGraphBuffer view; // the frame's ViewConstants
    const ViewConstants& viewConstants;
    const DrawList& draws;
    ecs::World* world;
    uint32_t descriptorBase;
};

// Runs every step of the schedule for one frame. InvalidData (already
// reported) when a step read a blackboard entry no one published.
Result<void> addStepPasses(const FrameInputs& inputs);

// DepthPrepass, Opaque, then Resolve (RenderSteps.cpp).
void addDefaultSteps(RenderSchedule& schedule);

// DrawList.cpp (ADR 0051): a surface's drawing state (its transform buffers
// and instance lists, one per frame slot), and the renderer's cache of
// models acquired for mesh instances.
struct DrawState;
DrawState* createDrawState(uint32_t descriptorBase);
void destroyDrawState(DrawState* state); // the GPU must be done with it
uint32_t drawDescriptorBase(const DrawState& state);
// Resolves new or changed mesh instances, then culls every instance against
// the view on the job system and batches the visible ones, in the graph's
// frame slot `frameSlot`. lastRunVersion: the render system's previous run
// (0: a world not seen before, so everything is sent again).
Result<DrawList> buildDrawList(DrawState& state, ecs::World* world, const ViewConstants& view, uint32_t height,
                               uint32_t frameSlot, uint32_t lastRunVersion);
// Releases every cached model; before stopAssets.
void stopDrawing();
// Tests and measurement: send every world matrix each frame, as if every
// WorldTransform had changed.
void forceFullTransformUploads(bool force);
// Tests: what the last buildDrawList did.
struct DrawStats
{
    uint32_t instances;      // mesh instances seen
    uint32_t visible;        // instances drawn
    uint32_t transformsSent; // world matrices written to the GPU copy
    uint32_t cachedModels;   // models held for instances
    uint64_t cpuNs;          // building the list, uploads included
};
DrawStats drawStats();

// RenderAssets.cpp's part of the renderer (ADR 0049): started after the
// device, collected every frame, stopped before the device shuts down.
Result<void> startAssets(slag::GraphicsDevice& device, slag::CommandQueue& queue);
void collectAssets();
void stopAssets();
// The next value of the work semaphore, which every frame and upload
// signals; unloading waits for the last one signalled.
slag::SemaphoreValue workSignal();

// For tests: the renderer's device and queue (null before it starts), and
// uploading through staging even where direct writes are possible.
slag::GraphicsDevice* renderDevice();
slag::CommandQueue* renderQueue();
void forceStagingUploads(bool force);
slag::Texture* textureOf(TextureHandle texture);
// A loaded model by its handle's index alone, for handles already checked
// this frame (DrawList batches); null if none is loaded there.
const MeshData* meshDataAt(uint32_t index);

// The frame's view, from the world's chosen camera (Camera.cpp): the one with
// the highest priority, or a default one when the world (which may be null)
// has none.
ViewConstants viewConstants(ecs::World* world, uint32_t width, uint32_t height, float time);

// The backbuffer's view format: the surfaces' sRGB view (Render.cpp).
inline constexpr slag::PixelFormat backbufferFormat = slag::PixelFormat::B8G8R8A8UnormSrgb;

} // namespace crucible::render
