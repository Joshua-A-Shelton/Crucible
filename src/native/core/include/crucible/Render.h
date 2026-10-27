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

// Called by the launcher after platform::initialize and before the engine
// starts, and after it returns (ADR 0011), so not exposed to C#. Creates the
// Slag instance and initializes the first device it lists.
CRUCIBLE_EXPORT Result<void> initialize(slag::Backend backend);
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

// The present mode and minimum image count for a window supporting `modes`
// (ADR 0003): Mailbox with three images, else Fifo with two.
struct PresentChoice
{
    slag::PresentMode mode;
    uint32_t minImageCount;
};
PresentChoice choosePresentMode(std::span<const slag::PresentMode> modes);

} // namespace crucible::render
