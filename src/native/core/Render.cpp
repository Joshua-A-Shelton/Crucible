#include <crucible/Render.h>
#include <crucible/Version.h>

#include <slag/Slag.h>

#include <algorithm>
#include <array>
#include <chrono>
#include <cstdio>
#include <thread>

namespace crucible::render {

namespace {

// Two command buffers per surface: the CPU records one frame while the GPU
// may still run the previous one.
constexpr uint32_t framesInFlight = 2;
constexpr uint32_t maxSurfaces = 8;

// Bounds the pacing wait, so a window that stops presenting (hidden, or a
// stalled compositor) slows the loop instead of hanging it.
constexpr uint64_t frameWaitTimeoutNs = 100'000'000;

// While a frame is skipped (minimized, occluded), the loop would otherwise
// spin; this is about one 60 Hz refresh.
constexpr std::chrono::milliseconds skippedFrameDelay(16);

struct Surface
{
    platform::WindowHandle window{};
    slag::Swapchain* swapchain = nullptr;
    slag::CommandBuffer* commandBuffers[framesInFlight] = {};
    slag::Semaphore* frames = nullptr; // reaches N once the GPU has finished frame N
    uint64_t submitted = 0;            // frames submitted so far
    slag::PresentMode presentMode{};
    uint32_t width = 0;
    uint32_t height = 0;
    bool needsResize = false;
    // Whether the last frame was presented: pacing waits behind a present, so
    // after a skipped frame there's nothing to wait for.
    bool presentedLastFrame = false;
};

struct Renderer
{
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
    const char* severity = message.severity == slag::MessageSeverity::Error ? "error" : "warning";
    std::fprintf(stderr, "crucible: [%s] %s\n", severity, message.text);
}

Surface* find(SurfaceHandle surface)
{
    if (surface.value == 0 || surface.value > maxSurfaces)
    {
        return nullptr;
    }
    Surface& found = renderer.surfaces[surface.value - 1];
    return found.swapchain != nullptr ? &found : nullptr;
}

// Destroys whatever the surface holds. The GPU must be done with it.
void release(Surface& surface)
{
    slag::GraphicsDevice* device = renderer.device;
    for (slag::CommandBuffer*& commandBuffer : surface.commandBuffers)
    {
        if (commandBuffer != nullptr)
        {
            device->destroy(commandBuffer);
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
    surface = Surface{};
}

Result<void> skip(Surface& surface)
{
    surface.presentedLastFrame = false;
    std::this_thread::sleep_for(skippedFrameDelay);
    return {};
}

// Records the whole frame: the image's previous contents are discarded
// (Undefined), it's cleared as a render target, then handed to Present.
Result<void> record(slag::CommandBuffer* commandBuffer, Surface& surface, uint32_t image, const float color[4])
{
    if (!commandBuffer->reset() || !commandBuffer->begin())
    {
        return Status::Unsupported;
    }
    slag::Texture* texture = surface.swapchain->texture(image);
    const slag::TextureBarrier toTarget = {.syncBefore = slag::BarrierSync::None,
                                           .syncAfter = slag::BarrierSync::RenderTarget,
                                           .accessAfter = slag::BarrierAccess::RenderTarget,
                                           .layoutBefore = slag::TextureLayout::Undefined,
                                           .layoutAfter = slag::TextureLayout::RenderTarget,
                                           .texture = texture};
    commandBuffer->barrier({}, {}, {&toTarget, 1});

    // The sRGB view: the color is linear and the hardware encodes it.
    slag::ColorAttachment attachment = {.view = surface.swapchain->srgbAttachmentView(image),
                                        .loadOp = slag::LoadOp::Clear};
    std::copy(color, color + 4, attachment.clearValue.color);
    commandBuffer->beginRendering({.colorAttachments = {&attachment, 1}});
    commandBuffer->endRendering();

    const slag::TextureBarrier toPresent = {.syncBefore = slag::BarrierSync::RenderTarget,
                                            .syncAfter = slag::BarrierSync::None,
                                            .accessBefore = slag::BarrierAccess::RenderTarget,
                                            .accessAfter = slag::BarrierAccess::NoAccess,
                                            .layoutBefore = slag::TextureLayout::RenderTarget,
                                            .layoutAfter = slag::TextureLayout::Present,
                                            .texture = texture};
    commandBuffer->barrier({}, {}, {&toPresent, 1});
    if (!commandBuffer->end())
    {
        return Status::Unsupported;
    }
    return {};
}

} // namespace

PresentChoice choosePresentMode(std::span<const slag::PresentMode> modes)
{
    // Mailbox needs a third image to always have one free to render into;
    // Fifo gets only two, since a third would queue a frame (ADR 0003).
    if (std::find(modes.begin(), modes.end(), slag::PresentMode::Mailbox) != modes.end())
    {
        return {slag::PresentMode::Mailbox, 3};
    }
    return {slag::PresentMode::Fifo, 2};
}

Result<void> initialize(slag::Backend backend)
{
    if (!slag::isBackendAvailable(backend))
    {
        std::fprintf(stderr, "crucible: the %s backend isn't available in this build\n",
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
    desc.applicationName = "Crucible";
    desc.engineName = "Crucible";
    desc.engineVersion = CRUCIBLE_VERSION;
    slag::Result<slag::Instance*> instance = slag::createInstance(desc);
    if (!instance)
    {
        std::fprintf(stderr, "crucible: Slag could not start\n");
        return toStatus(instance.status());
    }
    renderer.instance = instance.value();

    std::span<slag::GraphicsDevice* const> devices = renderer.instance->graphicsDevices();
    if (devices.empty())
    {
        std::fprintf(stderr, "crucible: no graphics device meets Slag's requirements\n");
        shutdown();
        return Status::Unsupported;
    }
    slag::GraphicsDevice* device = devices[0];
    if (slag::Result<void> initialized = device->initialize({}); !initialized)
    {
        std::fprintf(stderr, "crucible: %s could not be initialized\n", device->info().name);
        shutdown();
        return toStatus(initialized.status());
    }
    renderer.device = device;
    renderer.queue = device->commandQueue(slag::CommandQueueType::Graphics);
    std::printf("crucible: rendering with %s on %s\n", backend == slag::Backend::D3D12 ? "D3D12" : "Vulkan",
                device->info().name);
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
            if (surface.swapchain != nullptr)
            {
                release(surface);
            }
        }
        renderer.device->shutdown();
    }
    if (renderer.instance != nullptr)
    {
        slag::destroy(renderer.instance);
    }
    renderer = Renderer{};
}

Result<SurfaceHandle> createSurface(platform::WindowHandle window)
{
    slag::GraphicsDevice* device = renderer.device;
    if (device == nullptr)
    {
        return Status::Unsupported;
    }
    auto free = std::find_if(renderer.surfaces.begin(), renderer.surfaces.end(),
                             [](const Surface& surface) { return surface.swapchain == nullptr; });
    if (free == renderer.surfaces.end())
    {
        std::fprintf(stderr, "crucible: at most %u surfaces\n", maxSurfaces);
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
    const PresentChoice choice = choosePresentMode({modes.data(), modeCount});
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

    for (slag::CommandBuffer*& commandBuffer : surface.commandBuffers)
    {
        slag::Result<slag::CommandBuffer*> created = device->createCommandBuffer({.debugName = "crucible frame"});
        if (!created)
        {
            release(surface);
            return toStatus(created.status());
        }
        commandBuffer = created.value();
    }
    slag::Result<slag::Semaphore*> frames = device->createSemaphore({.debugName = "crucible frames"});
    if (!frames)
    {
        release(surface);
        return toStatus(frames.status());
    }
    surface.frames = frames.value();

    if (device->capabilities().frameLatency)
    {
        surface.swapchain->setMaxFrameLatency(1);
    }
    // Drivers may return more images than asked for; a third Fifo image would
    // queue a frame (ADR 0003's open item), so it's worth seeing.
    std::printf("crucible: surface %ux%u, %s, %u images (asked for %u), pacing: %s\n", surface.width, surface.height,
                name(choice.mode), surface.swapchain->imageCount(), choice.minImageCount,
                device->capabilities().frameLatency ? "frame latency" : "previous GPU frame");
    std::fflush(stdout);

    *free = surface;
    return SurfaceHandle{static_cast<uint32_t>(free - renderer.surfaces.begin()) + 1};
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
    if (renderer.device->capabilities().frameLatency)
    {
        slag::Result<void> waited = found->swapchain->waitForNextFrame(frameWaitTimeoutNs);
        return waited ? Result<void>() : Result<void>(toStatus(waited.status()));
    }
    // ADR 0003's fallback: start once the GPU has finished the previous frame.
    // It ignores presentation, but the queue never grows past one frame.
    slag::Result<void> waited = found->frames->wait(found->submitted, frameWaitTimeoutNs);
    return waited ? Result<void>() : Result<void>(toStatus(waited.status()));
}

Result<void> clearAndPresent(SurfaceHandle surface, float red, float green, float blue, float alpha)
{
    Surface* found = find(surface);
    if (found == nullptr)
    {
        return Status::NotFound;
    }
    Surface& target = *found;

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

    // The command buffer was last used framesInFlight frames ago.
    if (target.submitted >= framesInFlight)
    {
        slag::Result<void> waited = target.frames->wait(target.submitted - framesInFlight + 1, UINT64_MAX);
        if (!waited)
        {
            return toStatus(waited.status());
        }
    }
    slag::CommandBuffer* commandBuffer = target.commandBuffers[target.submitted % framesInFlight];
    const float color[4] = {red, green, blue, alpha};
    if (Result<void> recorded = record(commandBuffer, target, acquired.value(), color); !recorded)
    {
        return recorded;
    }

    const slag::SemaphoreValue signal = {target.frames, target.submitted + 1};
    slag::Swapchain* const acquiredSwapchains[] = {target.swapchain};
    slag::CommandBuffer* const commandBuffers[] = {commandBuffer};
    if (slag::Result<void> submitted = renderer.queue->submit(
            {.commandBuffers = commandBuffers, .signals = {&signal, 1}, .acquiredSwapchains = acquiredSwapchains});
        !submitted)
    {
        return toStatus(submitted.status());
    }
    ++target.submitted;

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

} // namespace crucible::render
