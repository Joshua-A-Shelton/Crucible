#pragma once

// The render schedule (ADR 0015, 0048): the frame as an ordered list of named
// render steps, built on slag::rendergraph. Every frame each step adds its
// graph passes, sharing resources through the blackboard; the graph works out
// barriers, semaphores between queues, and transient memory, and never
// reorders passes. C# steps go through the batched command API
// (crucible/RenderCommands.h, ADR 0058); the blackboard's keys are shared.
//
// Threading: like the renderer, the schedule is used on the launcher's main
// thread. Graph passes record on job threads (their record functions may run
// concurrently with each other).

#include <crucible/DrawList.h>
#include <crucible/Expose.h>
#include <crucible/Memory.h>
#include <crucible/Result.h>
#include <crucible/Vector.h>

#include <slag/rendergraph/RenderGraph.h>

#include <cstdint>
#include <string_view>

namespace crucible::ecs {
struct World;
}

namespace crucible::render {

// Generated from shaders/crucible/shared.slang (the renderer's shared.g.h).
struct ViewConstants;

// A blackboard entry's name, registered once and used as a small ID.
struct CRUCIBLE_EXPOSE BlackboardKey
{
    uint32_t value; // UINT32_MAX: none
};

// The same name always gives the same key.
CRUCIBLE_EXPOSE BlackboardKey registerBlackboardKey(std::string_view name);
CRUCIBLE_EXPOSE std::string_view blackboardKeyName(BlackboardKey key);

// The surface's image, published before the first step runs.
CRUCIBLE_EXPOSE BlackboardKey backbufferKey();

// The frame's ViewConstants (shaders/crucible/shared.slang, ADR 0050), in an
// upload buffer published before the first step runs. Read it as a constant
// buffer: setConstantBuffer(address, size) with the size of ViewConstants
// rounded up to slag::alignment::constantBuffer.
CRUCIBLE_EXPOSE BlackboardKey viewKey();

// The MSAA targets the default steps draw into (ADR 0051), at
// msaaSamples(): SceneDepth (D32Float, from DepthPrepass) and SceneColor
// (R16G16B16A16Float, linear, from Opaque). Tonemap writes SceneColor to the
// backbuffer.
CRUCIBLE_EXPOSE BlackboardKey sceneDepthKey();
CRUCIBLE_EXPOSE BlackboardKey sceneColorKey();

// The frame's lights (ADR 0053), published before the first step runs: the
// visible lights' LightData (an upload buffer, under Lights), and
// LightClustering's output (LightClusters: each cluster's light count, then
// each cluster's list of light indices). Shaders find them through
// ViewConstants' lights, clusterRanges, and clusterLights slots.
CRUCIBLE_EXPOSE BlackboardKey lightsKey();
CRUCIBLE_EXPOSE BlackboardKey lightClustersKey();

// The Shadows step's maps (ADR 0054), in the shadow-map depth format:
// ShadowCascades, the sun's cascades as a 4-layer array, made each frame and
// published only when the sun casts shadows; and ShadowAtlas, the point and
// spot lights' tiles, kept across frames, published only while a light has
// a tile. Shaders find them through ViewConstants' cascadeMap and
// shadowAtlas slots (0 when not published).
CRUCIBLE_EXPOSE BlackboardKey shadowCascadesKey();
CRUCIBLE_EXPOSE BlackboardKey shadowAtlasKey();

// The Bloom step's chain (ADR 0055): half size, one mip per level, the
// summed glow in mip 0, which Tonemap mixes in. Published only when the
// camera's PostProcess has bloom.
CRUCIBLE_EXPOSE BlackboardKey bloomKey();

// What a step sees while it adds its graph passes for one frame.
class RenderStepContext
{
public:
    slag::rendergraph::RenderGraph& graph;
    slag::GraphicsDevice& device;
    uint32_t width;
    uint32_t height;
    uint64_t frame;      // counts the surface's rendered frames from 1
    float clearColor[4]; // renderFrame's color, which Opaque clears to
    ecs::World* world;   // the world drawn, or null
    // The frame's view (its GPU copy is under viewKey()), and the visible
    // instances (ADR 0051).
    const ViewConstants& view;
    const DrawList& draws;

    // Makes a resource available to later steps under `key` this frame.
    void publish(BlackboardKey key, slag::rendergraph::RenderGraphTexture texture);
    void publish(BlackboardKey key, slag::rendergraph::RenderGraphBuffer buffer);
    // What an earlier step published; reported (naming the key and this step)
    // and an invalid handle if none did, or if it is the other kind.
    slag::rendergraph::RenderGraphTexture texture(BlackboardKey key);
    slag::rendergraph::RenderGraphBuffer buffer(BlackboardKey key);

    // The step running, for messages.
    std::string_view stepName() const;

private:
    friend struct FrameBuilder;
    RenderStepContext(slag::rendergraph::RenderGraph& graph, slag::GraphicsDevice& device, const ViewConstants& view,
                      const DrawList& draws)
        : graph(graph)
        , device(device)
        , width(0)
        , height(0)
        , frame(0)
        , clearColor{}
        , world(nullptr)
        , view(view)
        , draws(draws)
    {}
    void* frameState = nullptr;
};

using RenderStepFunction = void (*)(RenderStepContext& context, void* userData);

struct RenderStep
{
    const char* name = nullptr; // copied; unique in the schedule
    RenderStepFunction addPasses = nullptr;
    void* userData = nullptr;
    // Optional. Called with the device when the renderer starts (or when the
    // step is added to a running renderer), and when the step leaves or the
    // renderer stops: create pipelines here, never per frame (ADR 0025).
    Result<void> (*prepare)(slag::GraphicsDevice& device, void* userData) = nullptr;
    void (*release)(slag::GraphicsDevice& device, void* userData) = nullptr;
};

// The renderer's ordered steps, edited by name like frame phases (ADR 0036).
// NotFound when the named step isn't there; InvalidData for a name already
// used; a failed prepare is returned and the step isn't added.
class RenderSchedule
{
public:
    RenderSchedule() = default;
    ~RenderSchedule();
    RenderSchedule(const RenderSchedule&) = delete;
    RenderSchedule& operator=(const RenderSchedule&) = delete;

    Result<void> add(const RenderStep& step);
    Result<void> insertBefore(std::string_view existing, const RenderStep& step);
    Result<void> insertAfter(std::string_view existing, const RenderStep& step);
    Result<void> replace(std::string_view name, const RenderStep& step);
    Result<void> remove(std::string_view name);

    uint32_t stepCount() const;
    std::string_view stepName(uint32_t index) const;
    const RenderStep& step(uint32_t index) const;

    // The renderer calls these when it starts and stops.
    Result<void> prepareAll(slag::GraphicsDevice& device);
    void releaseAll();

private:
    struct Entry
    {
        RenderStep step;
        Vector<char> name{MemoryTag::Render}; // NUL-terminated; step.name points here
    };
    Result<void> insert(uint32_t index, const RenderStep& step);
    int32_t find(std::string_view name) const;

    Vector<Entry*> entries{MemoryTag::Render};
    slag::GraphicsDevice* device = nullptr; // while the renderer runs
};

// The renderer's schedule, holding the default steps (ADR 0051, 0053, 0054, 0055):
// DepthPrepass, LightClustering, Shadows, Opaque, Outlines, Sky, Transparent, Bloom,
// then Tonemap. Surfaces render it unless given another (ADR 0066).
RenderSchedule& renderSchedule();

// Schedules besides the default, by ID (ADR 0066): 0 is renderSchedule().
// A new one holds one step, Clear (the backbuffer cleared to renderFrame's
// color), for surfaces that draw only what their own steps draw, such as an
// editor window's UI. Prepared while the renderer runs, like the default.
uint32_t createSchedule();
void destroySchedule(uint32_t id);
// Null for an ID that was destroyed or never made.
RenderSchedule* scheduleById(uint32_t id);

} // namespace crucible::render
