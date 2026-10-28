#pragma once

// The platform layer (ADR 0018): windows and input, implemented with SDL3
// behind Crucible's own API. SDL never appears in a public header. Names are
// placeholders (docs/naming.md).
//
// Threading: everything here runs on the thread that called initialize(),
// which is the launcher's main thread (SDL requires it for windows and events).

#include <crucible/Export.h>
#include <crucible/Expose.h>
#include <crucible/Result.h>

#include <cstddef>
#include <cstdint>
#include <span>
#include <string_view>

namespace slag {
struct NativeWindow;
}

namespace crucible::platform {

// A window, identified by SDL's window ID (never 0; 0 means no window). An
// ID, not a pointer, so it can cross to C# as plain data (ADR 0011).
struct CRUCIBLE_EXPOSE WindowHandle
{
    uint32_t value;
};

struct CRUCIBLE_EXPOSE WindowSize
{
    uint32_t width;
    uint32_t height;
};

enum class CRUCIBLE_EXPOSE InputEventType : uint32_t
{
    None = 0,
    QuitRequested,        // the OS asked the application to quit
    WindowCloseRequested, // the user closed a window (window)
    WindowResized,        // pixel size changed (window, code = width, value = height)
    KeyDown,              // code = scancode, value = 1 if a key repeat
    KeyUp,                // code = scancode
    MouseMotion,          // x, y = position; dx, dy = relative motion
    MouseButtonDown,      // code = button, x, y = position
    MouseButtonUp,        // code = button, x, y = position
    LatencyMarker,        // synthetic input for the latency harness; code = sequence number
};

// One input or window event. Flat plain data, so a frame's events cross the
// boundary as one span. timestampNs is when SDL received the event from the
// OS, on SDL's clock (ADR 0018); the latency harness compares it with frame
// times on the same clock (timestampNow). No default member initializers, so
// it stays plain data for C# (InputEvent{} zeroes it).
struct CRUCIBLE_EXPOSE InputEvent
{
    InputEventType type;
    WindowHandle window;
    uint64_t timestampNs;
    int32_t code;
    int32_t value;
    float x;
    float y;
    float dx;
    float dy;
};

// Called by the launcher before the engine starts and after it returns (ADR
// 0011: C++ owns platform setup and shutdown), so not exposed to C#.
CRUCIBLE_EXPORT Result<void> initialize();
CRUCIBLE_EXPORT void shutdown();

CRUCIBLE_EXPOSE Result<WindowHandle> createWindow(uint32_t width, uint32_t height, std::string_view title);
CRUCIBLE_EXPOSE void destroyWindow(WindowHandle window);

// The window's size in pixels, which can differ from the size it was created
// with on high-DPI displays. Zero for a window that doesn't exist.
CRUCIBLE_EXPOSE WindowSize windowPixelSize(WindowHandle window);

// Whether the window is minimized; the renderer skips frames meanwhile.
CRUCIBLE_EXPOSE bool windowMinimized(WindowHandle window);

// Fills events with pending events and returns how many it wrote. Events that
// don't fit stay queued for the next call.
CRUCIBLE_EXPOSE uint32_t pollEvents(std::span<InputEvent> events);

// Now, on the same clock as InputEvent::timestampNs.
CRUCIBLE_EXPOSE uint64_t timestampNow();

// Whether InputEvent::timestampNs is when the input arrived (Wayland,
// Windows) rather than when SDL processed it, which can be a frame later.
// False under X11, where SDL doesn't convert the X server's event times
// (ADR 0018's amendment); timing code then treats a timestamp as "no earlier
// than the poll".
CRUCIBLE_EXPOSE bool eventTimestampsAreArrivals();

// The latency harness's synthetic input (ADR 0003): a background thread
// pushes LatencyMarker events into the event queue at random intervals
// (uniform between half and one and a half times the mean), so they arrive
// at random points in the frame, as real input does. Their timestamps are
// when they entered the queue. Stopping waits for the thread.
CRUCIBLE_EXPOSE_STATUS Result<void> startLatencyMarkers(uint32_t meanIntervalUs);
CRUCIBLE_EXPOSE void stopLatencyMarkers();

// Files, by UTF-8 path (ADR 0039). Not exposed: C# has its own file API, and
// the core reads and writes its binary files itself.
//
// The file's size in bytes; NotFound if it doesn't exist.
Result<uint64_t> fileSize(std::string_view utf8Path);
// Reads the file's first destination.size() bytes; InvalidData if it is
// shorter.
Result<void> readFile(std::string_view utf8Path, std::span<std::byte> destination);
// Writes the whole file under a temporary name beside it, then renames it
// over `utf8Path`: a crash or full disk never leaves a half-written file
// under the real name.
Result<void> writeFileAtomically(std::string_view utf8Path, std::span<const std::byte> contents);

// The OS handles Slag needs to create a swapchain (ADR 0018). Unsupported for
// video drivers Slag can't present to, such as the offscreen test driver. Not
// exposed: the core creates swapchains itself.
Result<void> nativeWindow(WindowHandle window, slag::NativeWindow& native);

} // namespace crucible::platform
