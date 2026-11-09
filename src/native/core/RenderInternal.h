#pragma once

// Between the renderer (Render.cpp), the render schedule, and the default
// steps (ADR 0048). Internal.

#include <crucible/Camera.h>
#include <crucible/MaterialFile.h>
#include <crucible/RenderAssets.h>
#include <crucible/RenderSchedule.h>
#include <crucible/Text.h>

#include "shared.g.h" // ViewConstants, generated from shaders/crucible/shared.slang

#include <crucible/Ecs.h>
#include <crucible/Math.h>

#include <array>

namespace crucible::render {

// The CPU builds one frame while the GPU may still run the previous one.
inline constexpr uint32_t framesInFlight = 2;

// Each surface's reserved resource-heap slots, below the loaded textures'
// (which start at 1024): `descriptorsPerSurface` from 8 + surface index *
// descriptorsPerSurface, one per frame slot for each of these.
inline constexpr uint32_t descriptorsPerSurface = 120;
// (LightClustering writes the cluster lists through the RW slots, Opaque
// reads them through the others. The shadow atlas outlives frames, so it
// has one slot.)
inline constexpr uint32_t transformsDescriptor = 0;                       // + frame slot
inline constexpr uint32_t instancesDescriptor = framesInFlight;           // + frame slot
inline constexpr uint32_t tonemapDescriptor = 2 * framesInFlight;         // + frame slot
inline constexpr uint32_t lightsDescriptor = 3 * framesInFlight;          // + frame slot
inline constexpr uint32_t clusterRangesDescriptor = 4 * framesInFlight;   // + frame slot
inline constexpr uint32_t clusterLightsDescriptor = 5 * framesInFlight;   // + frame slot
inline constexpr uint32_t rwClusterRangesDescriptor = 6 * framesInFlight; // + frame slot
inline constexpr uint32_t rwClusterLightsDescriptor = 7 * framesInFlight; // + frame slot
inline constexpr uint32_t shadowViewsDescriptor = 8 * framesInFlight;     // + frame slot
inline constexpr uint32_t cascadeMapDescriptor = 9 * framesInFlight;      // + frame slot
inline constexpr uint32_t shadowAtlasDescriptor = 10 * framesInFlight;
// The bloom chain (ADR 0055): SceneColor as the prefilter reads it, and
// each level on its own.
inline constexpr uint32_t maxBloomLevels = 6;
inline constexpr uint32_t bloomSourceDescriptor = 11 * framesInFlight; // + frame slot
inline constexpr uint32_t bloomLevelsDescriptor = 12 * framesInFlight; // + level * framesInFlight + frame slot
// C# steps (ADR 0058): the frame data's ByteAddressBuffer, and the slots
// their passes read and write graph resources through, handed out in order
// each frame.
inline constexpr uint32_t frameDataDescriptor = (12 + maxBloomLevels) * framesInFlight; // + frame slot
inline constexpr uint32_t stepDescriptorsPerFrame = 40;
// The editor's SelectionOutline (ADR 0067): the Pick step's EntityIds.
inline constexpr uint32_t entityIdsDescriptor = frameDataDescriptor + framesInFlight; // + frame slot
inline constexpr uint32_t stepDescriptors = entityIdsDescriptor + framesInFlight;     // + frame slot * per frame
static_assert(stepDescriptors + stepDescriptorsPerFrame * framesInFlight <= descriptorsPerSurface);
// 8 surfaces from slot 8: they must stay below the loaded textures' 1024.
static_assert(8 + 8 * descriptorsPerSurface <= 1024);
// The glyph cache's pages on the GPU (ADR 0059), after the surfaces': the
// exact pages, then the distance-field pages.
inline constexpr uint32_t glyphPageDescriptors = 8 + 8 * descriptorsPerSurface;
static_assert(glyphPageDescriptors + 8 + 4 <= 1024);

// The material buffer's slot (a ByteAddressBuffer), below the surfaces'.
inline constexpr uint32_t materialBufferDescriptor = 1;

// The surface's slot `which` (one of the above, plus the frame slot).
uint32_t surfaceDescriptor(const RenderStepContext& context, uint32_t which);

// A view's planes for row vectors (Gribb and Hartmann): left, right, bottom,
// top, then w - z and z, normalized so a sphere's radius compares with the
// distance. Inside is 0 <= z <= w: for reverse-Z, w - z is the near plane
// and z the far one (an infinite perspective's is 0 = near, always inside);
// for conventional depth, the other way round. (Camera.cpp)
struct Plane
{
    Vector3 normal;
    float distance;
};
std::array<Plane, 6> frustumPlanes(const Matrix4x4& viewProjection);

struct ShadowFrame;
struct DrawState;
struct ManagedState;

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
    const ShadowFrame& shadows;
    const DrawState& drawState;
    ManagedState& managed;
    uint32_t frameSlot;
    const RenderSchedule& schedule; // the surface's (ADR 0066)
    uint32_t surface;               // its handle's value
};

// Every schedule's steps, prepared when the renderer starts and released when
// it stops (RenderSchedule.cpp).
Result<void> prepareSchedules(slag::GraphicsDevice& device);
void releaseSchedules();

// The Clear step every new schedule starts with (RenderSteps.cpp).
void addClearStep(RenderSchedule& schedule);

// Runs every step of the schedule for one frame. InvalidData (already
// reported) when a step read a blackboard entry no one published.
Result<void> addStepPasses(const FrameInputs& inputs);

// The frame's shadow passes, for the Shadows step, and its drawing state,
// for Transparent's sorting (RenderSchedule.cpp).
const ShadowFrame& shadowFrame(const RenderStepContext& context);
const DrawState& frameDrawState(const RenderStepContext& context);
// For C# steps (RenderCommands.cpp): the surface's state for them, the graph's
// frame slot, and failing the frame (no later step runs; renderFrame returns
// InvalidData).
ManagedState& managedState(const RenderStepContext& context);
uint32_t frameSlotOf(const RenderStepContext& context);
void failFrame(RenderStepContext& context);

// RenderCommands.cpp (ADR 0058): a surface's state for C# steps (their
// passes' commands and descriptors, and the frame data's buffers), begun
// before the steps run and finished before the graph compiles, which copies
// the frame data into the frame slot's upload buffer.
ManagedState* createManagedState(uint32_t descriptorBase);
void destroyManagedState(ManagedState* state); // the GPU must be done with it
void beginManagedFrame(ManagedState& state, uint32_t frameSlot);
Result<void> finishManagedFrame(ManagedState& state, uint32_t frameSlot);
// Destroys the pipelines C# left behind; before stopAssets.
void stopPipelines();

// GlyphUpload.cpp (ADR 0059): the glyph cache's pages as GPU textures. Begun
// before the steps run (a texture for every page then, imported, and the
// GlyphUpload pass declared), finished after them (the changed rectangles
// staged for the pass to copy).
Result<void> beginGlyphUpload(slag::rendergraph::RenderGraph& graph);
Result<void> finishGlyphUpload();
void stopGlyphUpload(); // the GPU must be idle
// A page's resource-heap slot, or 0 if it has no GPU texture yet.
uint32_t glyphPageDescriptor(text::GlyphRendering rendering, uint32_t page);

// DepthPrepass, LightClustering, Shadows, Opaque, Outlines, Sky, Transparent, Bloom,
// then Tonemap (RenderSteps.cpp).
void addDefaultSteps(RenderSchedule& schedule);

// DrawList.cpp (ADR 0051): a surface's drawing state (its transform buffers
// and instance lists, one per frame slot), and the renderer's cache of
// models acquired for mesh instances.
struct DrawState;
DrawState* createDrawState(uint32_t descriptorBase);
void destroyDrawState(DrawState* state); // the GPU must be done with it
uint32_t drawDescriptorBase(const DrawState& state);
// A view culled for a shadow map besides the camera's (ADR 0054). Cascades
// leave out their near plane, so casters between the light and the cascade
// still cast (the shadow pipelines clamp their depth).
struct ShadowCullView
{
    std::array<Plane, 6> planes;
    uint32_t planeCount;
    bool orthographic;
    Vector3 eye;          // perspective: the light, for choosing levels of detail
    float pixelsPerMeter; // a shadow-map texel per meter (at distance 1 for a perspective view)
};
// Resolves new or changed mesh instances, then culls every instance against
// the view, and those that cast shadows against each shadow view, on the job
// system, and batches the visible ones, in the graph's frame slot
// `frameSlot`. lastRunVersion: the render system's previous run (0: a world
// not seen before), for finding mesh instances whose model changed.
Result<DrawList> buildDrawList(DrawState& state, ecs::World* world, const ViewConstants& view, uint32_t height,
                               uint32_t frameSlot, uint32_t lastRunVersion,
                               std::span<const ShadowCullView> shadowViews);
// Shadow view `index`'s batches from the last buildDrawList, and a hash of
// its casters (each one's entity, model, mesh, level, and world matrix), the
// same whichever order they were found in: an atlas tile whose hash is the
// same as when it was drawn still holds the right shadow.
DrawList shadowDrawList(const DrawState& state, uint32_t index);
// Each instance's view depth (its bounds' center along the camera's
// forward), by its position in the instance list, for sorting transparent
// instances (ADR 0055); 0 for shadow views'.
std::span<const float> instanceDistances(const DrawState& state);
uint64_t shadowCasterHash(const DrawState& state, uint32_t index);
// Releases every cached model; before stopAssets.
void stopDrawing();
// Tests: what the last buildDrawList did.
struct DrawStats
{
    uint32_t instances;    // mesh instances seen
    uint32_t visible;      // instances drawn
    uint32_t cachedModels; // models held for instances
    uint64_t cpuNs;        // building the list, uploads included
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
// An offscreen surface's texture in the texture table (ADR 0067), drawn by
// handle; the surface owns it. Replacing points the handle at a new texture.
TextureHandle addSurfaceTexture(slag::Texture* texture);
void replaceSurfaceTexture(TextureHandle handle, slag::Texture* texture);
void removeSurfaceTexture(TextureHandle handle);

// The editor's view of a surface (ADR 0067; RenderSteps.cpp): the grid,
// the selection outlined, and picking. Released with the surface.
void releaseEditorView(uint32_t surface);
Result<void> addEditorStepsTo(RenderSchedule& schedule);
// Frames the GPU has finished on a surface (Render.cpp).
uint64_t surfaceFramesDone(uint32_t surface);

// A surface's own camera (ADR 0067) for viewConstants.
struct CameraOverride
{
    Matrix4x4 world;
    Camera camera;
};

slag::Texture* textureOf(TextureHandle texture);
// A loaded model by its handle's index alone, for handles already checked
// this frame (DrawList batches); null if none is loaded there.
const MeshData* meshDataAt(uint32_t index);

// The frame's view, from the world's chosen camera (Camera.cpp): the one with
// the highest priority, or a default one when the world (which may be null)
// has none.
ViewConstants viewConstants(ecs::World* world, uint32_t width, uint32_t height, float time,
                            const CameraOverride* camera = nullptr);

// Materials.cpp (ADR 0052): the material buffer and the pipelines each kind
// of material draws with, made while loading. Started after the assets,
// stopped after them (models free their materials as they unload).
struct MaterialPipelines
{
    slag::Pipeline* depth;       // DepthPrepass
    slag::Pipeline* forward;     // Opaque
    slag::Pipeline* shadow;      // Shadows: depth only, clamped and biased (ADR 0054)
    slag::Pipeline* transparent; // Transparent; blended materials have only this (ADR 0055)
    slag::Pipeline* outline;     // Outlines, when the material has a width (ADR 0055)
};
// A material's outline (MaterialInfo's), for DrawConstants.
struct MaterialOutline
{
    float width; // pixels; 0: none
    Vector3 color;
    float tint;
};
struct MaterialDraw
{
    const MaterialPipelines* pipelines;
    uint32_t offset;   // the parameters' byte offset in the material buffer
    float alphaCutoff; // 0 unless alpha-tested
    MaterialOutline outline;
};
Result<void> startMaterials(slag::GraphicsDevice& device);
void stopMaterials();
// From a record (MaterialFile.h); textures are acquired by asset ID.
Result<MaterialHandle> createMaterial(const assets::MaterialInfo& info, std::span<const uint8_t> parameters,
                                      std::span<const assets::MaterialTexture> textures);
void destroyMaterial(MaterialHandle material);
// A stale or missing handle draws as the fallback (white toon).
MaterialDraw materialDraw(MaterialHandle material);
// After the sample count or the shadow-map format changes.
Result<void> rebuildMaterialPipelines();
// Record `index` of an MTLS section (MaterialFile.h).
Result<MaterialHandle> createMaterialFromRecords(std::span<const uint8_t> section, uint32_t index);
// Reloading in place (ADR 0072; RenderAssets.cpp calls these once the GPU is
// idle): false if the asset isn't loaded as that kind.
Result<bool> reloadMaterialAsset(const assets::AssetId& id);
Result<bool> reloadGameShader(const assets::AssetId& id);
// Waits for the GPU to finish everything submitted, before a swap.
void waitForReload();
// Counts reloads, so caches built from loaded assets (cached shadow tiles)
// know to build again.
uint64_t renderAssetReloads();
// An asset's bytes through the reader (RenderAssets.cpp): NotFound if it has
// none, Unsupported without a reader.
Status readAsset(const assets::AssetId& id, Vector<uint8_t>& out);
// A loaded model's materials, one per slot, by its handle's index.
std::span<const MaterialHandle> modelMaterials(uint32_t meshIndex);
// The last value of the work semaphore signalled (RenderAssets.cpp).
slag::SemaphoreValue lastWorkSignaled();
// Destroyed once the GPU passes the last work signalled (RenderAssets.cpp).
void destroyAfterWork(slag::Pipeline* pipeline);
// Destroyed once the GPU finishes the frame being built.
void destroyAfterFrame(slag::Buffer* buffer);

// Lights.cpp (ADR 0053): a surface's light buffers, one per frame slot, and
// the frame's lights gathered into them: the counts, ambient light, light
// slots, and cluster parameters written into the view's constants.
// The lights that cast shadows this frame go into `shadows`.
struct ShadowRequests;
struct LightState;
LightState* createLightState(uint32_t descriptorBase);
void destroyLightState(LightState* state);
void gatherLights(LightState& state, ecs::World* world, uint32_t frameSlot, ViewConstants& view,
                  ShadowRequests& shadows);
slag::Buffer* lightBuffer(const LightState& state, uint32_t frameSlot);
// The cluster grid (clusters.slang): screen tiles across and down, depth
// slices.
inline constexpr uint32_t clusterCountX = 16;
inline constexpr uint32_t clusterCountY = 9;
inline constexpr uint32_t clusterCountZ = 24;
inline constexpr uint32_t clusterCount = clusterCountX * clusterCountY * clusterCountZ;

// Shadows.cpp (ADR 0054): the sun's cascades, fitted and snapped each frame,
// and the point and spot lights' tiles in the surface's shadow atlas,
// redrawn only when they change.
//
// LightData::shadowView for a light without shadows (shared.slang's).
inline constexpr uint32_t noShadowView = 0xffffffff;
// A point or spot light that casts shadows, visible this frame.
struct ShadowLight
{
    ecs::Entity entity;
    uint32_t light; // its index in the frame's LightData
    bool point;
    Vector3 position;
    Vector3 axis; // spot: along the cone
    float range;
    float outerConeAngle; // spot: the full angle
};
struct ShadowRequests
{
    bool sun = false;     // whether the first directional light casts
    Vector3 sunDirection; // toward the light
    float sunDistance = 0;
    Vector<ShadowLight> lights{MemoryTag::Render};
};
// One shadow map's pass: a cascade's layer, or an atlas tile.
struct ShadowPass
{
    uint32_t cullView;  // its ShadowCullView, and so its DrawList
    uint32_t layer;     // cascades
    slag::Rect rect;    // atlas tiles: where it is, in texels
    uint64_t constants; // the GPU address of its ViewConstants (the view replaced by the light's)
    DrawList draws;
};
struct ShadowFrame
{
    // Each frame's cascades (none when the sun casts no shadows), and the
    // atlas tiles to draw again.
    std::span<const ShadowPass> cascades;
    std::span<const ShadowPass> tiles;
    uint32_t cascadeSize;
    uint32_t cascadeSlot; // the cascades' Texture2DArray slot this frame
    slag::Texture* atlas; // null: no light has a tile
    slag::AttachmentView* atlasView;
    slag::TextureLayout atlasLayout; // its layout before the frame
};
struct ShadowState;
ShadowState* createShadowState(uint32_t descriptorBase);
void destroyShadowState(ShadowState* state); // the GPU must be done with it
// Where gatherLights puts the frame's requests.
ShadowRequests& shadowRequests(ShadowState& state);
// Before culling: fits the cascades, gives lights their tiles, writes the
// frame's ShadowViews, sets each shadowed light's LightData::shadowView, and
// fills the view's shadow constants.
Result<void> prepareShadows(ShadowState& state, ecs::World* world, const ShadowRequests& requests, LightData* lights,
                            uint32_t frameSlot, ViewConstants& view);
std::span<const ShadowCullView> shadowCullViews(const ShadowState& state);
// After culling: which tiles changed, and each pass's constants.
const ShadowFrame& finishShadows(ShadowState& state, const DrawState& draws, const ViewConstants& view,
                                 uint32_t frameSlot);
// The sun's cascades for a view (written to `views`, shadowCascadeCount
// of them, and the view's cascadeSplits): spheres around slices of the
// view out to `sunDistance`, snapped to whole texels of `size`-texel maps
// in the light's view.
void fitCascades(ViewConstants& view, Vector3 sunDirection, float sunDistance, uint32_t size, ShadowView* views);
// Tests: what the last finishShadows did.
struct ShadowStats
{
    uint32_t cascades;   // drawn
    uint32_t tilesDrawn; // atlas tiles drawn again
    uint32_t tilesKept;  // atlas tiles still right from earlier frames
};
ShadowStats shadowStats();
// The shadow-map format: D16Unorm, or D32Float where D16Unorm can't be
// filtered; Undefined where neither can (no shadows). Chosen at start-up.
slag::PixelFormat shadowFormat();
void chooseShadowFormat(slag::GraphicsDevice& device);

// The scene targets' formats (RenderSteps.cpp, Materials.cpp).
inline constexpr slag::PixelFormat sceneColorFormat = slag::PixelFormat::R16G16B16A16Float;
inline constexpr slag::PixelFormat sceneDepthFormat = slag::PixelFormat::D32Float;

// The backbuffer's view format: the surfaces' sRGB view (Render.cpp).
inline constexpr slag::PixelFormat backbufferFormat = slag::PixelFormat::B8G8R8A8UnormSrgb;

} // namespace crucible::render
