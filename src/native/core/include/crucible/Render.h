#pragma once

// The renderer's foundation (ADR 0015): one Slag device, and a surface per
// window that renders the render schedule's steps (ADR 0048) and presents
// with the latency rules of ADR 0003. Names are placeholders unless listed
// in docs/naming.md.
//
// Threading: like the platform layer, everything runs on the launcher's main
// thread.

#include <crucible/Export.h>
#include <crucible/Expose.h>
#include <crucible/Platform.h>
#include <crucible/Result.h>

#include <cstdint>
#include <span>

namespace crucible::ecs {
struct World;
}

namespace slag {
enum class Backend : uint8_t;
enum class PresentMode : uint8_t;
} // namespace slag

namespace crucible::render {

// A window's swapchain and per-frame resources (never 0; 0 means none).
struct CRUCIBLE_EXPOSE SurfaceHandle
{
    uint32_t value;
};

// The launcher's --present-mode: Default follows ADR 0003 (Mailbox where
// supported, else Fifo); the others force a mode where the window supports it.
enum class PresentPreference : uint8_t
{
    Default,
    Fifo,
    Mailbox,
};

// Called by the launcher after platform::initialize and before the engine
// starts, and after it returns (ADR 0011), so not exposed to C#. Creates the
// Slag instance and initializes the first device it lists.
CRUCIBLE_EXPORT Result<void> initialize(slag::Backend backend,
                                        PresentPreference presentMode = PresentPreference::Default);
CRUCIBLE_EXPORT void shutdown();

// Creates the window's swapchain (ADR 0003: Mailbox, else double-buffered
// Fifo). The surface must be destroyed before its window.
CRUCIBLE_EXPOSE Result<SurfaceHandle> createSurface(platform::WindowHandle window);

// A surface without a window: the same frames, rendered into a texture
// instead of a swapchain, for headless runs and tests. Presenting is skipped,
// and pacing waits for the GPU to finish the previous frame.
CRUCIBLE_EXPOSE Result<SurfaceHandle> createOffscreenSurface(uint32_t width, uint32_t height);

CRUCIBLE_EXPOSE void destroySurface(SurfaceHandle surface);

// Copies an offscreen surface's last frame into `pixels`: rows top to bottom,
// 4 bytes per pixel in B8G8R8A8 order, sRGB-encoded, tightly packed (width * 4
// bytes per row). Waits for the GPU. InvalidData if `pixels` is too small,
// NotFound before the first frame or for a windowed surface. For tests and
// screenshots, not per frame.
CRUCIBLE_EXPOSE Result<void> readSurfacePixels(SurfaceHandle surface, std::span<uint8_t> pixels);

// The start of a frame (ADR 0003's just-in-time pacing): waits until a new
// frame may start, so input read afterwards is as fresh as possible. Uses
// Slag's frame latency capability, or else waits for the GPU to finish the
// previous frame.
CRUCIBLE_EXPOSE_STATUS Result<void> waitForNextFrame(SurfaceHandle surface);

// Renders one frame through the render schedule (ADR 0048), with
// `red`..`alpha` (linear) as the Clear step's color, and presents it. The
// view is the world's camera with the highest priority (ADR 0050); without a
// world (null) or a camera, a default one at the origin looking along +Z.
// Resizes the swapchain when the window's size changed; skips the frame
// (still Ok) while the window is minimized or hidden.
CRUCIBLE_EXPOSE_STATUS Result<void> renderFrame(SurfaceHandle surface, ecs::World* world, float red, float green,
                                                float blue, float alpha);

// Latency harness support (ADR 0003).
//
// How many frames the surface has submitted; a frame that was skipped
// (minimized, occluded) doesn't change it.
CRUCIBLE_EXPOSE uint64_t framesSubmitted(SurfaceHandle surface);
// Whether waitForNextFrame waits for the previous present (Slag's frame
// latency capability), so its return marks that present as done. Otherwise it
// waits for the GPU only.
CRUCIBLE_EXPOSE bool pacingWaitsForPresent();
// While on, a thread waits on the surface's frame semaphore and stamps each
// submitted frame the moment the GPU finishes it.
CRUCIBLE_EXPOSE_STATUS Result<void> setGpuTiming(SurfaceHandle surface, bool enabled);
// When the GPU finished submitted frame `frame` (counting from 1, as
// framesSubmitted does), on the timestampNow clock; 0 if not known (not
// finished yet, timing off, or more than 256 frames ago).
CRUCIBLE_EXPOSE uint64_t gpuFrameDoneNs(SurfaceHandle surface, uint64_t frame);

// The present mode and minimum image count for a window supporting `modes`
// (ADR 0003): Mailbox with three images, else Fifo with two. A preference
// for a mode the window lacks falls back to the default choice.
struct PresentChoice
{
    slag::PresentMode mode;
    uint32_t minImageCount;
};
PresentChoice choosePresentMode(std::span<const slag::PresentMode> modes,
                                PresentPreference preference = PresentPreference::Default);

} // namespace crucible::render
