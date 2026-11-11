#include "RenderInternal.h"
#include "TextInternal.h" // updateGlyphCache

#include <crucible/Jobs.h>
#include <crucible/Log.h>
#include <crucible/Memory.h>
#include <crucible/Render.h>
#include <crucible/Version.h>

#include <slag/Slag.h>
#include <slag/rendergraph/RenderGraph.h>

#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <cstdio>
#include <cstring>
#include <thread>

namespace crucible::render {

namespace {

constexpr uint32_t maxSurfaces = 8;

constexpr uint32_t viewConstantsSize = (sizeof(ViewConstants) + slag::alignment::constantBuffer - 1) /
                                       slag::alignment::constantBuffer * slag::alignment::constantBuffer;

// Bounds the pacing wait, so a window that stops presenting (hidden, or a
// stalled compositor) slows the loop instead of hanging it.
constexpr uint64_t frameWaitTimeoutNs = 100'000'000;

// While a frame is skipped (minimized, occluded), the loop would otherwise
// spin; this is about one 60 Hz refresh.
constexpr std::chrono::milliseconds skippedFrameDelay(16);

// The latency harness's GPU timing (ADR 0003): a thread waits for each
// submitted frame on the frame semaphore and stamps it as soon as the wait
// returns, so the time is accurate to a thread wake-up, not to a frame. The
// last `capacity` frames are kept.
struct GpuTimer
{
    static constexpr uint32_t capacity = 256;
    // Short, so stopping (destroySurface, setGpuTiming off) is prompt.
    static constexpr uint64_t waitTimeoutNs = 10'000'000;

    std::thread thread;
    std::atomic<bool> running{true};
    // Slot i holds the frame number last stamped there and its time; the
    // frame is written after the time (release), so a reader that sees its
    // frame number sees its time.
    std::array<std::atomic<uint64_t>, capacity> frames{};
    std::array<std::atomic<uint64_t>, capacity> times{};
};

struct Surface
{
    platform::WindowHandle window{};
    slag::Swapchain* swapchain = nullptr;
    // An offscreen surface has a texture instead of a window and swapchain.
    // Between operations the texture is always in the RenderTarget layout.
    slag::Texture* offscreenTexture = nullptr;
    slag::AttachmentView* offscreenView = nullptr; // the sRGB view
    bool offscreenRendered = false;
    // Once its texture is drawn elsewhere (surfaceTexture), each frame leaves
    // it ready to sample (ShaderRead) rather than as a render target.
    TextureHandle sampled{};
    slag::TextureLayout resting = slag::TextureLayout::RenderTarget;
    bool hasCamera = false;
    CameraOverride camera{}; // its own camera (ADR 0067)
    // The surface's render graph (ADR 0048), rebuilt every frame from the
    // render schedule.
    slag::rendergraph::RenderGraph* graph = nullptr;
    slag::Semaphore* frames = nullptr; // reaches N once the GPU has finished frame N
    // The frames' ViewConstants, one buffer per frame in flight, used in the
    // graph's order of frame slots; beginFrame waits until the GPU is done
    // with the one it hands out.
    std::array<slag::Buffer*, framesInFlight> viewBuffers{};
    uint64_t graphFrames = 0;        // frames begun on the graph, for its frame slot
    DrawState* draws = nullptr;      // its transform buffers and instance lists (ADR 0051)
    LightState* lights = nullptr;    // its light buffers (ADR 0053)
    ShadowState* shadows = nullptr;  // its shadow views and atlas (ADR 0054)
    ManagedState* managed = nullptr; // its C# steps' passes and frame data (ADR 0058)
    uint64_t submitted = 0;          // frames submitted so far
    slag::PresentMode presentMode{};
    uint32_t width = 0;
    uint32_t height = 0;
    bool needsResize = false;
    // Whether the last frame was presented: pacing waits behind a present, so
    // after a skipped frame there's nothing to wait for.
    bool presentedLastFrame = false;
    GpuTimer* gpuTimer = nullptr; // only while the latency harness runs
    uint32_t schedule = 0;        // its render schedule's ID (ADR 0066); 0 the default
};

struct Renderer
{
    PresentPreference presentPreference = PresentPreference::Default;
    std::chrono::steady_clock::time_point started; // ViewConstants' time counts from here
    slag::Instance* instance = nullptr;
    slag::GraphicsDevice* device = nullptr;
    slag::CommandQueue* queue = nullptr;
    std::array<Surface, maxSurfaces> surfaces;
};

Renderer renderer;

Status toStatus(slag::Status status)
{
    switch (status)
    {
    case slag::Status::DeviceLost:
        return Status::DeviceLost;
    case slag::Status::OutOfDeviceMemory:
        return Status::OutOfDeviceMemory;
    case slag::Status::OutOfHostMemory:
        return Status::OutOfHostMemory;
    default:
        return Status::Unsupported;
    }
}

const char* name(slag::PresentMode mode)
{
    switch (mode)
    {
    case slag::PresentMode::Mailbox:
        return "Mailbox";
    case slag::PresentMode::Immediate:
        return "Immediate";
    default:
        return "Fifo";
    }
}

// Info messages are left out: with validation on, the Vulkan loader alone
// sends dozens per start.
void printMessage(const slag::DebugMessage& message, void*)
{
    if (message.severity == slag::MessageSeverity::Info)
    {
        return;
    }
    if (message.severity == slag::MessageSeverity::Error)
    {
        log::error("[error] %s", message.text);
    }
    else
    {
        log::warning("[warning] %s", message.text);
    }
}

bool inUse(const Surface& surface)
{
    return surface.swapchain != nullptr || surface.offscreenTexture != nullptr;
}

Surface* find(SurfaceHandle surface)
{
    if (surface.value == 0 || surface.value > maxSurfaces)
    {
        return nullptr;
    }
    Surface& found = renderer.surfaces[surface.value - 1];
    return inUse(found) ? &found : nullptr;
}

void stopGpuTimer(Surface& surface)
{
    if (surface.gpuTimer != nullptr)
    {
        surface.gpuTimer->running = false;
        surface.gpuTimer->thread.join();
        destroy(MemoryTag::Render, surface.gpuTimer);
        surface.gpuTimer = nullptr;
    }
}

void startGpuTimer(Surface& surface)
{
    surface.gpuTimer = create<GpuTimer>(MemoryTag::Render);
    GpuTimer* timer = surface.gpuTimer;
    slag::Semaphore* frames = surface.frames;
    // Read now, not when the thread first runs: under load it can start after
    // later frames finished, and would never stamp them.
    const uint64_t first = frames->value() + 1;
    timer->thread = std::thread([timer, frames, first] {
        uint64_t next = first;
        while (timer->running)
        {
            // Timeout (nothing submitted yet) is a success status; only Ok
            // means the frame finished.
            if (frames->wait(next, GpuTimer::waitTimeoutNs).status() != slag::Status::Ok)
            {
                continue;
            }
            const uint32_t slot = static_cast<uint32_t>(next % GpuTimer::capacity);
            timer->times[slot].store(platform::timestampNow(), std::memory_order_relaxed);
            timer->frames[slot].store(next, std::memory_order_release);
            ++next;
        }
    });
}

// Destroys whatever the surface holds. The GPU must be done with it.
SurfaceHandle handleOf(const Surface* slot);

void release(Surface& surface)
{
    stopGpuTimer(surface);
    releaseEditorView(handleOf(&surface).value);
    slag::GraphicsDevice* device = renderer.device;
    slag::rendergraph::destroy(surface.graph);
    destroyDrawState(surface.draws);
    destroyLightState(surface.lights);
    destroyShadowState(surface.shadows);
    destroyManagedState(surface.managed);
    for (slag::Buffer* buffer : surface.viewBuffers)
    {
        if (buffer != nullptr)
        {
            device->destroy(buffer);
        }
    }
    if (surface.frames != nullptr)
    {
        device->destroy(surface.frames);
    }
    if (surface.swapchain != nullptr)
    {
        device->destroy(surface.swapchain);
    }
    if (surface.offscreenView != nullptr)
    {
        device->destroy(surface.offscreenView);
    }
    if (surface.sampled.generation != 0)
    {
        removeSurfaceTexture(surface.sampled);
    }
    if (surface.offscreenTexture != nullptr)
    {
        device->destroy(surface.offscreenTexture);
    }
    surface = Surface{};
}

Result<void> skip(Surface& surface)
{
    surface.presentedLastFrame = false;
    std::this_thread::sleep_for(skippedFrameDelay);
    return {};
}

// Slag's CPU memory goes through Crucible's allocator, counted under Render
// (ADR 0032). Crucible's allocate never returns null, so Slag never sees
// OutOfHostMemory from it.
slag::AllocatorCallbacks allocatorCallbacks()
{
    slag::AllocatorCallbacks callbacks;
    callbacks.allocate = [](size_t size, size_t alignment, void*) {
        return allocate(size, alignment, MemoryTag::Render);
    };
    callbacks.free = [](void* pointer, size_t size, void*) {
        free(pointer, size, MemoryTag::Render);
    };
    return callbacks;
}

// The render graph and frame semaphore every surface has.
Result<void> createFrameResources(Surface& surface, uint32_t index)
{
    surface.draws = createDrawState(8 + index * descriptorsPerSurface);
    surface.lights = createLightState(8 + index * descriptorsPerSurface);
    surface.shadows = createShadowState(8 + index * descriptorsPerSurface);
    surface.managed = createManagedState(8 + index * descriptorsPerSurface);
    if (surface.lights == nullptr || surface.shadows == nullptr)
    {
        return Status::OutOfDeviceMemory;
    }
    slag::GraphicsDevice* device = renderer.device;
    slag::Result<slag::rendergraph::RenderGraph*> graph =
        slag::rendergraph::createRenderGraph({.device = device,
                                              .graphicsQueue = renderer.queue,
                                              .framesInFlight = framesInFlight,
                                              .allocatorCallbacks = allocatorCallbacks(),
                                              .messageCallback = printMessage});
    if (!graph)
    {
        return toStatus(graph.status());
    }
    surface.graph = graph.value();
    slag::Result<slag::Semaphore*> frames = device->createSemaphore({.debugName = "crucible frames"});
    if (!frames)
    {
        return toStatus(frames.status());
    }
    surface.frames = frames.value();
    for (slag::Buffer*& buffer : surface.viewBuffers)
    {
        slag::Result<slag::Buffer*> created =
            device->createBuffer({.size = viewConstantsSize,
                                  .placement = slag::Placement::automatic(slag::MemoryKind::Upload),
                                  .debugName = "crucible view constants"});
        if (!created)
        {
            return toStatus(created.status());
        }
        buffer = created.value();
    }
    return {};
}

Surface* freeSlot()
{
    auto found = std::find_if(renderer.surfaces.begin(), renderer.surfaces.end(),
                              [](const Surface& surface) { return !inUse(surface); });
    if (found == renderer.surfaces.end())
    {
        log::warning("at most %u surfaces", maxSurfaces);
        return nullptr;
    }
    return &*found;
}

SurfaceHandle handleOf(const Surface* slot)
{
    return SurfaceHandle{static_cast<uint32_t>(slot - renderer.surfaces.data()) + 1};
}

struct Recording
{
    slag::rendergraph::RenderGraph* graph;
    std::atomic<bool> failed{false};
};

// One frame on the surface's graph (ADR 0048): the image imported and
// published as the backbuffer, every render step's passes, compiled,
// recorded in parallel on the job system, and submitted; then a signal of the
// frame semaphore after all of it, for pacing and GPU timing.
Result<void> renderWithGraph(Surface& surface, slag::Texture* texture, slag::AttachmentView* view,
                             const slag::rendergraph::ImportedTextureDesc& import, ecs::World* world,
                             uint32_t lastRunVersion, const float color[4])
{
    slag::rendergraph::RenderGraph& graph = *surface.graph;
    collectAssets();
    text::updateGlyphCache();
    if (slag::Result<void> begun = graph.beginFrame(); !begun)
    {
        return toStatus(begun.status());
    }
    // The graph's frame slot (Slag's PassContext::frameSlot) is free again.
    const auto frameSlot = static_cast<uint32_t>(surface.graphFrames++ % framesInFlight);
    slag::Buffer* viewBuffer = surface.viewBuffers[frameSlot];
    const float time = std::chrono::duration<float>(std::chrono::steady_clock::now() - renderer.started).count();
    ViewConstants constants =
        viewConstants(world, surface.width, surface.height, time, surface.hasCamera ? &surface.camera : nullptr);
    ShadowRequests& requests = shadowRequests(*surface.shadows);
    gatherLights(*surface.lights, world, frameSlot, constants, requests);
    auto* lights = static_cast<LightData*>(lightBuffer(*surface.lights, frameSlot)->mappedData());
    if (Result<void> prepared = prepareShadows(*surface.shadows, world, requests, lights, frameSlot, constants);
        !prepared)
    {
        return prepared;
    }
    std::memcpy(viewBuffer->mappedData(), &constants, sizeof(constants));
    Result<DrawList> draws = buildDrawList(*surface.draws, world, constants, surface.height, frameSlot, lastRunVersion,
                                           shadowCullViews(*surface.shadows));
    if (!draws)
    {
        return draws.status();
    }
    const ShadowFrame& shadows = finishShadows(*surface.shadows, *surface.draws, constants, frameSlot);

    slag::rendergraph::ImportedTextureDesc desc = import;
    desc.attachmentView = view;
    const slag::rendergraph::RenderGraphTexture backbuffer = graph.importTexture(texture, desc);
    beginManagedFrame(*surface.managed, frameSlot);
    if (Result<void> begun = beginGlyphUpload(graph); !begun)
    {
        return begun;
    }
    // A destroyed schedule leaves its surfaces on the default.
    const RenderSchedule* schedule = scheduleById(surface.schedule);
    if (Result<void> added = addStepPasses(
            {graph, *renderer.device, surface.width, surface.height, surface.submitted + 1, color, backbuffer,
             graph.importBuffer(viewBuffer), graph.importBuffer(lightBuffer(*surface.lights, frameSlot)), constants,
             draws.value(), world, drawDescriptorBase(*surface.draws), shadows, *surface.draws, *surface.managed,
             frameSlot, schedule != nullptr ? *schedule : renderSchedule(), handleOf(&surface).value});
        !added)
    {
        return added;
    }
    if (Result<void> finished = finishManagedFrame(*surface.managed, frameSlot); !finished)
    {
        return finished;
    }
    if (Result<void> finished = finishGlyphUpload(); !finished)
    {
        return finished;
    }
    if (slag::Result<void> compiled = graph.compile(); !compiled)
    {
        return toStatus(compiled.status());
    }
    Recording recording{&graph};
    jobs::parallelFor(
        graph.passCount(), 1,
        [](void* data, uint32_t begin, uint32_t end) {
            auto& job = *static_cast<Recording*>(data);
            for (uint32_t pass = begin; pass < end; ++pass)
            {
                if (!job.graph->recordPass(pass))
                {
                    job.failed = true;
                }
            }
        },
        &recording);
    if (recording.failed)
    {
        return Status::Unsupported;
    }
    if (slag::Result<void> executed = graph.execute(); !executed)
    {
        return toStatus(executed.status());
    }
    const slag::SemaphoreValue signals[] = {{surface.frames, surface.submitted + 1}, workSignal()};
    if (slag::Result<void> signaled = renderer.queue->submit({.signals = signals}); !signaled)
    {
        return toStatus(signaled.status());
    }
    ++surface.submitted;
    return {};
}

} // namespace

uint64_t surfaceFramesDone(uint32_t surface)
{
    const Surface* slot = find(SurfaceHandle{surface});
    return slot != nullptr && slot->frames != nullptr ? slot->frames->value() : 0;
}

slag::GraphicsDevice* renderDevice()
{
    return renderer.device;
}

slag::CommandQueue* renderQueue()
{
    return renderer.queue;
}

PresentChoice choosePresentMode(std::span<const slag::PresentMode> modes, PresentPreference preference)
{
    // Mailbox needs a third image to always have one free to render into;
    // Fifo gets only two, since a third would queue a frame (ADR 0003).
    const bool hasMailbox = std::find(modes.begin(), modes.end(), slag::PresentMode::Mailbox) != modes.end();
    if (preference == PresentPreference::Fifo || !hasMailbox)
    {
        if (preference == PresentPreference::Mailbox)
        {
            log::warning("the window doesn't support Mailbox; using Fifo");
        }
        return {slag::PresentMode::Fifo, 2};
    }
    return {slag::PresentMode::Mailbox, 3};
}

Result<void> initialize(slag::Backend backend, PresentPreference presentMode)
{
    renderer.presentPreference = presentMode;
    renderer.started = std::chrono::steady_clock::now();
    if (!slag::isBackendAvailable(backend))
    {
        log::warning("the %s backend isn't available in this build",
                     backend == slag::Backend::D3D12 ? "D3D12" : "Vulkan");
        return Status::Unsupported;
    }

    slag::InstanceDesc desc;
    desc.backend = backend;
#if !defined(NDEBUG)
    // Backend validation in debug builds, where installed (Slag warns and
    // carries on when the Vulkan layer is missing).
    desc.validationFlags = slag::ValidationFlags::Api;
#endif
    desc.messageCallback = printMessage;
    desc.allocatorCallbacks = allocatorCallbacks();
    desc.applicationName = "Crucible";
    desc.engineName = "Crucible";
    desc.engineVersion = CRUCIBLE_VERSION;
    slag::Result<slag::Instance*> instance = slag::createInstance(desc);
    if (!instance)
    {
        log::error("Slag could not start");
        return toStatus(instance.status());
    }
    renderer.instance = instance.value();

    std::span<slag::GraphicsDevice* const> devices = renderer.instance->graphicsDevices();
    if (devices.empty())
    {
        log::error("no graphics device meets Slag's requirements");
        shutdown();
        return Status::Unsupported;
    }
    slag::GraphicsDevice* device = devices[0];
    if (slag::Result<void> initialized = device->initialize({}); !initialized)
    {
        log::error("%s could not be initialized", device->info().name);
        shutdown();
        return toStatus(initialized.status());
    }
    renderer.device = device;
    renderer.queue = device->commandQueue(slag::CommandQueueType::Graphics);
    if (Result<void> started = startAssets(*device, *renderer.queue); !started)
    {
        log::error("GPU assets could not start");
        shutdown();
        return started;
    }
    chooseShadowFormat(*device); // before materials make their shadow pipelines
    if (Result<void> started = startMaterials(*device); !started)
    {
        log::error("materials could not start");
        shutdown();
        return started;
    }
    // The render steps' pipelines, created now, never during frames (ADR 0025).
    if (Result<void> prepared = prepareSchedules(*device); !prepared)
    {
        log::error("the render steps could not be prepared");
        shutdown();
        return prepared;
    }
    log::info("rendering with %s on %s", backend == slag::Backend::D3D12 ? "D3D12" : "Vulkan", device->info().name);
    // Flushed so it keeps its place among C#'s output, which isn't buffered,
    // when stdout is a pipe.
    std::fflush(stdout);
    return {};
}

void shutdown()
{
    if (renderer.device != nullptr)
    {
        (void)renderer.queue->waitIdle();
        for (Surface& surface : renderer.surfaces)
        {
            if (inUse(surface))
            {
                release(surface);
            }
        }
        stopDrawing();
        stopPipelines();
        stopGlyphUpload();
        stopAssets();
        stopMaterials();
        releaseSchedules();
        renderer.device->shutdown();
    }
    if (renderer.instance != nullptr)
    {
        slag::destroy(renderer.instance);
    }
    renderer.presentPreference = PresentPreference::Default;
    renderer.instance = nullptr;
    renderer.device = nullptr;
    renderer.queue = nullptr;
}

Result<SurfaceHandle> createSurface(platform::WindowHandle window)
{
    slag::GraphicsDevice* device = renderer.device;
    if (device == nullptr)
    {
        return Status::Unsupported;
    }
    Surface* slot = freeSlot();
    if (slot == nullptr)
    {
        return Status::Unsupported;
    }

    slag::NativeWindow native;
    if (Result<void> found = platform::nativeWindow(window, native); !found)
    {
        return found.status();
    }

    std::array<slag::PresentMode, 3> modes{};
    const uint32_t modeCount =
        std::min(device->supportedPresentModes(native, modes), static_cast<uint32_t>(modes.size()));
    const PresentChoice choice = choosePresentMode({modes.data(), modeCount}, renderer.presentPreference);
    const platform::WindowSize size = platform::windowPixelSize(window);

    Surface surface;
    surface.window = window;
    surface.presentMode = choice.mode;
    surface.width = std::max(size.width, 1u);
    surface.height = std::max(size.height, 1u);
    slag::Result<slag::Swapchain*> swapchain = device->createSwapchain({.window = native,
                                                                        .queue = renderer.queue,
                                                                        .width = surface.width,
                                                                        .height = surface.height,
                                                                        .minImageCount = choice.minImageCount,
                                                                        .presentMode = choice.mode,
                                                                        .debugName = "crucible surface"});
    if (!swapchain)
    {
        return toStatus(swapchain.status());
    }
    surface.swapchain = swapchain.value();

    if (Result<void> created = createFrameResources(surface, static_cast<uint32_t>(slot - renderer.surfaces.data()));
        !created)
    {
        release(surface);
        return created.status();
    }

    if (device->capabilities().frameLatency)
    {
        surface.swapchain->setMaxFrameLatency(1);
    }
    // Drivers may return more images than asked for; a third Fifo image would
    // queue a frame (ADR 0003's open item), so it's worth seeing.
    log::info("surface %ux%u, %s, %u images (asked for %u), pacing: %s", surface.width, surface.height,
              name(choice.mode), surface.swapchain->imageCount(), choice.minImageCount,
              device->capabilities().frameLatency ? "frame latency" : "previous GPU frame");
    std::fflush(stdout);

    *slot = surface;
    return handleOf(slot);
}

Result<SurfaceHandle> createOffscreenSurface(uint32_t width, uint32_t height)
{
    slag::GraphicsDevice* device = renderer.device;
    if (device == nullptr || width == 0 || height == 0)
    {
        return Status::Unsupported;
    }
    Surface* slot = freeSlot();
    if (slot == nullptr)
    {
        return Status::Unsupported;
    }

    // The swapchain's format, with the same sRGB view, so offscreen frames
    // are rendered exactly as windowed ones.
    Surface surface;
    surface.width = width;
    surface.height = height;
    const slag::PixelFormat srgb[] = {slag::PixelFormat::B8G8R8A8UnormSrgb};
    slag::Result<slag::Texture*> texture = device->createTexture2D({.format = slag::PixelFormat::B8G8R8A8Unorm,
                                                                    .width = width,
                                                                    .height = height,
                                                                    .usage = slag::TextureUsage::RenderTarget,
                                                                    .castableFormats = srgb,
                                                                    .debugName = "crucible offscreen surface"});
    if (!texture)
    {
        return toStatus(texture.status());
    }
    surface.offscreenTexture = texture.value();
    slag::Result<slag::AttachmentView*> view =
        device->createAttachmentView({.texture = surface.offscreenTexture, .format = srgb[0]});
    if (!view)
    {
        release(surface);
        return toStatus(view.status());
    }
    surface.offscreenView = view.value();
    if (Result<void> created = createFrameResources(surface, static_cast<uint32_t>(slot - renderer.surfaces.data()));
        !created)
    {
        release(surface);
        return created.status();
    }

    *slot = surface;
    return handleOf(slot);
}

Result<void> setSurfaceCamera(SurfaceHandle surface, Vector3 position, Quaternion rotation, Camera camera)
{
    Surface* slot = find(surface);
    if (slot == nullptr)
    {
        return Status::NotFound;
    }
    slot->hasCamera = true;
    slot->camera = {Matrix4x4::rotation(rotation) * Matrix4x4::translation(position), camera};
    return {};
}

void clearSurfaceCamera(SurfaceHandle surface)
{
    if (Surface* slot = find(surface))
    {
        slot->hasCamera = false;
    }
}

Result<TextureHandle> surfaceTexture(SurfaceHandle surface)
{
    Surface* slot = find(surface);
    if (slot == nullptr || slot->offscreenTexture == nullptr)
    {
        return Status::NotFound;
    }
    if (slot->sampled.generation == 0)
    {
        slot->sampled = addSurfaceTexture(slot->offscreenTexture);
        if (slot->sampled.generation == 0)
        {
            return Status::OutOfDeviceMemory;
        }
    }
    return slot->sampled;
}

Result<void> resizeOffscreenSurface(SurfaceHandle surface, uint32_t width, uint32_t height)
{
    Surface* slot = find(surface);
    if (slot == nullptr || slot->offscreenTexture == nullptr)
    {
        return Status::NotFound;
    }
    if (width == 0 || height == 0)
    {
        return Status::InvalidData;
    }
    if (width == slot->width && height == slot->height)
    {
        return {};
    }
    // Rare (a panel resized), so simply after the GPU is done with the old one.
    slag::GraphicsDevice* device = renderer.device;
    (void)renderer.queue->waitIdle();
    const slag::PixelFormat srgb[] = {slag::PixelFormat::B8G8R8A8UnormSrgb};
    slag::Result<slag::Texture*> texture = device->createTexture2D({.format = slag::PixelFormat::B8G8R8A8Unorm,
                                                                    .width = width,
                                                                    .height = height,
                                                                    .usage = slag::TextureUsage::RenderTarget,
                                                                    .castableFormats = srgb,
                                                                    .debugName = "crucible offscreen surface"});
    if (!texture)
    {
        return toStatus(texture.status());
    }
    slag::Result<slag::AttachmentView*> view =
        device->createAttachmentView({.texture = texture.value(), .format = srgb[0]});
    if (!view)
    {
        device->destroy(texture.value());
        return toStatus(view.status());
    }
    device->destroy(slot->offscreenView);
    device->destroy(slot->offscreenTexture);
    slot->offscreenTexture = texture.value();
    slot->offscreenView = view.value();
    slot->width = width;
    slot->height = height;
    slot->offscreenRendered = false;
    slot->resting = slag::TextureLayout::RenderTarget;
    if (slot->sampled.generation != 0)
    {
        replaceSurfaceTexture(slot->sampled, slot->offscreenTexture);
    }
    return {};
}

Result<void> setSurfaceSchedule(SurfaceHandle surface, uint32_t schedule)
{
    Surface* slot = find(surface);
    if (slot == nullptr || scheduleById(schedule) == nullptr)
    {
        return Status::NotFound;
    }
    slot->schedule = schedule;
    return {};
}

void destroySurface(SurfaceHandle surface)
{
    if (Surface* found = find(surface))
    {
        (void)renderer.queue->waitIdle();
        release(*found);
    }
}

Result<void> waitForNextFrame(SurfaceHandle surface)
{
    Surface* found = find(surface);
    if (found == nullptr)
    {
        return Status::NotFound;
    }
    if (!found->presentedLastFrame)
    {
        return {};
    }

    // A Timeout is fine here: the frame starts anyway, just later than ideal.
    if (found->swapchain != nullptr && renderer.device->capabilities().frameLatency)
    {
        slag::Result<void> waited = found->swapchain->waitForNextFrame(frameWaitTimeoutNs);
        return waited ? Result<void>() : Result<void>(toStatus(waited.status()));
    }
    // ADR 0003's fallback: start once the GPU has finished the previous frame.
    // It ignores presentation, but the queue never grows past one frame.
    slag::Result<void> waited = found->frames->wait(found->submitted, frameWaitTimeoutNs);
    return waited ? Result<void>() : Result<void>(toStatus(waited.status()));
}

Result<void> renderFrame(SurfaceHandle surface, ecs::World* world, uint32_t lastRunVersion, float red, float green,
                         float blue, float alpha)
{
    Surface* found = find(surface);
    if (found == nullptr)
    {
        return Status::NotFound;
    }
    Surface& target = *found;
    const float color[4] = {red, green, blue, alpha};
    if (target.offscreenTexture != nullptr)
    {
        // Between frames the texture rests in RenderTarget, or ShaderRead
        // once it's drawn elsewhere (readSurfacePixels follows either);
        // before the first, it's Undefined.
        const slag::TextureLayout initial = target.offscreenRendered ? target.resting : slag::TextureLayout::Undefined;
        const slag::TextureLayout final =
            target.sampled.generation != 0 ? slag::TextureLayout::ShaderRead : slag::TextureLayout::RenderTarget;
        if (Result<void> rendered =
                renderWithGraph(target, target.offscreenTexture, target.offscreenView,
                                {.initialLayout = initial, .finalLayout = final}, world, lastRunVersion, color);
            !rendered)
        {
            return rendered;
        }
        target.offscreenRendered = true;
        target.resting = final;
        target.presentedLastFrame = true; // pacing waits behind it, as behind a present
        return {};
    }

    const platform::WindowSize size = platform::windowPixelSize(target.window);
    if (platform::windowMinimized(target.window) || size.width == 0 || size.height == 0)
    {
        return skip(target);
    }
    if (target.needsResize || size.width != target.width || size.height != target.height)
    {
        // Resizing needs the GPU done with every image (Slag ADR 0058).
        (void)renderer.queue->waitIdle();
        slag::Result<void> resized = target.swapchain->resize(size.width, size.height);
        if (resized.status() == slag::Status::DeviceLost)
        {
            return Status::DeviceLost;
        }
        if (!resized)
        {
            // Some window systems refuse sizes mid-resize; try next frame.
            target.needsResize = true;
            return skip(target);
        }
        target.width = size.width;
        target.height = size.height;
        target.needsResize = false;
    }

    slag::Result<uint32_t> acquired = target.swapchain->acquire();
    if (acquired.status() == slag::Status::OutOfDate)
    {
        target.needsResize = true;
        return skip(target);
    }
    if (!acquired)
    {
        return toStatus(acquired.status());
    }

    // The previous contents are discarded; the graph's first submit waits for
    // the acquired image.
    const uint32_t image = acquired.value();
    if (Result<void> rendered =
            renderWithGraph(target, target.swapchain->texture(image), target.swapchain->srgbAttachmentView(image),
                            {.initialLayout = slag::TextureLayout::Undefined,
                             .finalLayout = slag::TextureLayout::Present,
                             .swapchain = target.swapchain},
                            world, lastRunVersion, color);
        !rendered)
    {
        return rendered;
    }

    const slag::Result<void> presented = renderer.queue->present(target.swapchain, target.presentMode);
    switch (presented.status())
    {
    case slag::Status::Ok:
        target.presentedLastFrame = true;
        return {};
    case slag::Status::Suboptimal:
        target.presentedLastFrame = true;
        target.needsResize = true;
        return {};
    case slag::Status::OutOfDate:
        target.needsResize = true;
        target.presentedLastFrame = false;
        return {};
    case slag::Status::Occluded:
        return skip(target);
    default:
        return toStatus(presented.status());
    }
}

uint64_t framesSubmitted(SurfaceHandle surface)
{
    const Surface* found = find(surface);
    return found != nullptr ? found->submitted : 0;
}

bool pacingWaitsForPresent()
{
    return renderer.device != nullptr && renderer.device->capabilities().frameLatency;
}

Result<void> setGpuTiming(SurfaceHandle surface, bool enabled)
{
    Surface* found = find(surface);
    if (found == nullptr)
    {
        return Status::NotFound;
    }
    if (enabled && found->gpuTimer == nullptr)
    {
        startGpuTimer(*found);
    }
    else if (!enabled)
    {
        stopGpuTimer(*found);
    }
    return {};
}

uint64_t gpuFrameDoneNs(SurfaceHandle surface, uint64_t frame)
{
    const Surface* found = find(surface);
    if (found == nullptr || found->gpuTimer == nullptr || frame == 0)
    {
        return 0;
    }
    const uint32_t slot = static_cast<uint32_t>(frame % GpuTimer::capacity);
    if (found->gpuTimer->frames[slot].load(std::memory_order_acquire) != frame)
    {
        return 0;
    }
    return found->gpuTimer->times[slot].load(std::memory_order_relaxed);
}

Result<void> readSurfacePixels(SurfaceHandle surface, std::span<uint8_t> pixels)
{
    Surface* found = find(surface);
    if (found == nullptr || found->offscreenTexture == nullptr || !found->offscreenRendered)
    {
        return Status::NotFound;
    }
    Surface& target = *found;
    const size_t rowBytes = size_t{target.width} * 4;
    if (pixels.size() < rowBytes * target.height)
    {
        return Status::InvalidData;
    }

    // A one-off copy with its own command buffer and semaphore, so frame
    // counting (framesSubmitted) is untouched. The submit runs after the last
    // frame's, so it sees the frame's writes (Slag ADR 0049).
    slag::GraphicsDevice* device = renderer.device;
    const slag::CopyFootprint footprint = device->copyFootprint(target.offscreenTexture, 0);
    slag::Result<slag::Buffer*> readback = device->createBuffer(
        {.size = footprint.size, .placement = slag::Placement::automatic(slag::MemoryKind::Readback)});
    slag::Result<slag::CommandBuffer*> commandBuffer = device->createCommandBuffer({.debugName = "crucible readback"});
    slag::Result<slag::Semaphore*> done = device->createSemaphore({});
    Result<void> result;
    if (!readback || !commandBuffer || !done || !commandBuffer.value()->begin())
    {
        result = Status::OutOfDeviceMemory;
    }
    else
    {
        slag::CommandBuffer* commands = commandBuffer.value();
        const slag::TextureBarrier toCopy = {.syncAfter = slag::BarrierSync::Copy,
                                             .accessAfter = slag::BarrierAccess::CopySource,
                                             .layoutBefore = target.resting,
                                             .layoutAfter = slag::TextureLayout::CopySource,
                                             .texture = target.offscreenTexture};
        commands->barrier({}, {}, {&toCopy, 1});
        commands->copyTextureToBuffer({readback.value(), 0, footprint.rowPitch}, {.texture = target.offscreenTexture},
                                      target.width, target.height);
        // Back to the layout the texture rests in between operations.
        const bool sampled = target.resting == slag::TextureLayout::ShaderRead;
        const slag::TextureBarrier toTarget = {
            .syncBefore = slag::BarrierSync::Copy,
            .syncAfter = sampled ? slag::BarrierSync::None : slag::BarrierSync::RenderTarget,
            .accessBefore = slag::BarrierAccess::CopySource,
            .accessAfter = sampled ? slag::BarrierAccess::NoAccess : slag::BarrierAccess::RenderTarget,
            .layoutBefore = slag::TextureLayout::CopySource,
            .layoutAfter = target.resting,
            .texture = target.offscreenTexture};
        commands->barrier({}, {}, {&toTarget, 1});
        const slag::SemaphoreValue signal = {done.value(), 1};
        slag::CommandBuffer* const commandBuffers[] = {commands};
        slag::Result<void> submitted = commands->end();
        if (submitted)
        {
            submitted = renderer.queue->submit({.commandBuffers = commandBuffers, .signals = {&signal, 1}});
        }
        if (submitted)
        {
            submitted = done.value()->wait(1, UINT64_MAX);
        }
        if (!submitted)
        {
            result = toStatus(submitted.status());
        }
        else
        {
            const auto* source = static_cast<const uint8_t*>(readback.value()->mappedData());
            for (uint32_t row = 0; row < target.height; ++row)
            {
                std::copy_n(source + size_t{row} * footprint.rowPitch, rowBytes, pixels.begin() + row * rowBytes);
            }
        }
    }

    if (done)
    {
        device->destroy(done.value());
    }
    if (commandBuffer)
    {
        device->destroy(commandBuffer.value());
    }
    if (readback)
    {
        device->destroy(readback.value());
    }
    return result;
}

} // namespace crucible::render
