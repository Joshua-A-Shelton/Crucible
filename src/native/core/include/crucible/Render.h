#pragma once

// The renderer's foundation (ADR 0015): one Slag device, and a surface per
// window that clears and presents with the latency rules of ADR 0003. Until
// the render graph arrives, a frame is a clear to one color. Names are
// placeholders (docs/naming.md).
//
// Threading: like the platform layer, everything runs on the launcher's main
// thread.

#include <crucible/Export.h>
#include <crucible/Expose.h>
#include <crucible/Platform.h>
#include <crucible/Result.h>

#include <cstdint>
#include <span>

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
CRUCIBLE_EXPOSE void destroySurface(SurfaceHandle surface);

// The start of a frame (ADR 0003's just-in-time pacing): waits until a new
// frame may start, so input read afterwards is as fresh as possible. Uses
// Slag's frame latency capability, or else waits for the GPU to finish the
// previous frame.
CRUCIBLE_EXPOSE_STATUS Result<void> waitForNextFrame(SurfaceHandle surface);

// Renders one frame: clears the window to a linear color and presents it.
// Resizes the swapchain when the window's size changed; skips the frame
// (still Ok) while the window is minimized or hidden.
CRUCIBLE_EXPOSE_STATUS Result<void> clearAndPresent(SurfaceHandle surface, float red, float green, float blue,
                                                    float alpha);

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
