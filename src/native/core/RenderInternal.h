#pragma once

// Between the renderer (Render.cpp), the render schedule, and the default
// steps (ADR 0048). Internal.

#include <crucible/MaterialFile.h>
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
inline constexpr uint32_t descriptorsPerSurface = 16;
// (LightClustering writes the cluster lists through the RW slots, Opaque
// reads them through the others.)
inline constexpr uint32_t transformsDescriptor = 0;                       // + frame slot
inline constexpr uint32_t instancesDescriptor = framesInFlight;           // + frame slot
inline constexpr uint32_t resolveDescriptor = 2 * framesInFlight;         // + frame slot
inline constexpr uint32_t lightsDescriptor = 3 * framesInFlight;          // + frame slot
inline constexpr uint32_t clusterRangesDescriptor = 4 * framesInFlight;   // + frame slot
inline constexpr uint32_t clusterLightsDescriptor = 5 * framesInFlight;   // + frame slot
inline constexpr uint32_t rwClusterRangesDescriptor = 6 * framesInFlight; // + frame slot
inline constexpr uint32_t rwClusterLightsDescriptor = 7 * framesInFlight; // + frame slot
static_assert(8 * framesInFlight <= descriptorsPerSurface);
// 8 surfaces from slot 8: they must stay below the loaded textures' 1024.
static_assert(8 + 8 * descriptorsPerSurface <= 1024);

// The material buffer's slot (a ByteAddressBuffer), below the surfaces'.
inline constexpr uint32_t materialBufferDescriptor = 1;

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
    slag::rendergraph::RenderGraphBuffer view;   // the frame's ViewConstants
    slag::rendergraph::RenderGraphBuffer lights; // the frame's LightData
    const ViewConstants& viewConstants;
    const DrawList& draws;
    ecs::World* world;
    uint32_t descriptorBase;
};

// Runs every step of the schedule for one frame. InvalidData (already
// reported) when a step read a blackboard entry no one published.
Result<void> addStepPasses(const FrameInputs& inputs);

// DepthPrepass, LightClustering, Opaque, then Resolve (RenderSteps.cpp).
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

// Materials.cpp (ADR 0052): the material buffer and the pipelines each kind
// of material draws with, made while loading. Started after the assets,
// stopped after them (models free their materials as they unload).
struct MaterialPipelines
{
    slag::Pipeline* depth;   // DepthPrepass
    slag::Pipeline* forward; // Opaque
};
struct MaterialDraw
{
    const MaterialPipelines* pipelines;
    uint32_t offset;   // the parameters' byte offset in the material buffer
    float alphaCutoff; // 0 unless alpha-tested
};
Result<void> startMaterials(slag::GraphicsDevice& device);
void stopMaterials();
// From a record (MaterialFile.h); textures are acquired by asset ID.
Result<MaterialHandle> createMaterial(const assets::MaterialInfo& info, std::span<const uint8_t> parameters,
                                      std::span<const assets::MaterialTexture> textures);
void destroyMaterial(MaterialHandle material);
// A stale or missing handle draws as the fallback (white toon).
MaterialDraw materialDraw(MaterialHandle material);
// After the sample count changes.
Result<void> rebuildMaterialPipelines();
// Record `index` of an MTLS section (MaterialFile.h).
Result<MaterialHandle> createMaterialFromRecords(std::span<const uint8_t> section, uint32_t index);
// An asset's bytes through the reader (RenderAssets.cpp): NotFound if it has
// none, Unsupported without a reader.
Status readAsset(const assets::AssetId& id, Vector<uint8_t>& out);
// A loaded model's materials, one per slot, by its handle's index.
std::span<const MaterialHandle> modelMaterials(uint32_t meshIndex);
// The last value of the work semaphore signalled (RenderAssets.cpp).
slag::SemaphoreValue lastWorkSignaled();

// Lights.cpp (ADR 0053): a surface's light buffers, one per frame slot, and
// the frame's lights gathered into them: the counts, ambient light, light
// slots, and cluster parameters written into the view's constants.
struct LightState;
LightState* createLightState(uint32_t descriptorBase);
void destroyLightState(LightState* state);
void gatherLights(LightState& state, ecs::World* world, uint32_t frameSlot, ViewConstants& view);
slag::Buffer* lightBuffer(const LightState& state, uint32_t frameSlot);
// The cluster grid (clusters.slang): screen tiles across and down, depth
// slices.
inline constexpr uint32_t clusterCountX = 16;
inline constexpr uint32_t clusterCountY = 9;
inline constexpr uint32_t clusterCountZ = 24;
inline constexpr uint32_t clusterCount = clusterCountX * clusterCountY * clusterCountZ;

// The scene targets' formats (RenderSteps.cpp, Materials.cpp).
inline constexpr slag::PixelFormat sceneColorFormat = slag::PixelFormat::R16G16B16A16Float;
inline constexpr slag::PixelFormat sceneDepthFormat = slag::PixelFormat::D32Float;

// The backbuffer's view format: the surfaces' sRGB view (Render.cpp).
inline constexpr slag::PixelFormat backbufferFormat = slag::PixelFormat::B8G8R8A8UnormSrgb;

} // namespace crucible::render
