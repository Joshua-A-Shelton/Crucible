#pragma once

// The platform layer (ADR 0018): windows and input, implemented with SDL3
// behind Crucible's own API. SDL never appears in a public header. Names are
// placeholders (docs/naming.md).
//
// Threading: everything here runs on the thread that called initialize(),
// which is the launcher's main thread (SDL requires it for windows and events).

#include <crucible/Export.h>
#include <crucible/Result.h>

#include <cstdint>
#include <span>
#include <string_view>

namespace slag {
struct NativeWindow;
}

namespace crucible::platform {

// A window, identified by SDL's window ID (never 0). An ID, not a pointer,
// so it can cross to C# as plain data (ADR 0011).
struct WindowHandle
{
    uint32_t value = 0;
};

enum class InputEventType : uint32_t
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
};

// One input or window event. Flat plain data, so a frame's events cross the
// boundary as one span. timestampNs is when SDL received the event from the
// OS, on SDL's clock (ADR 0018); the latency harness compares it with frame
// times on the same clock (timestampNow).
struct InputEvent
{
    InputEventType type = InputEventType::None;
    uint32_t window = 0;
    uint64_t timestampNs = 0;
    int32_t code = 0;
    int32_t value = 0;
    float x = 0;
    float y = 0;
    float dx = 0;
    float dy = 0;
};

// Called by the launcher before the engine starts and after it returns (ADR
// 0011: C++ owns platform setup and shutdown).
CRUCIBLE_EXPORT Result<void> initialize();
CRUCIBLE_EXPORT void shutdown();

Result<WindowHandle> createWindow(uint32_t width, uint32_t height, std::string_view title);
void destroyWindow(WindowHandle window);

// The window's size in pixels, which can differ from the size it was created
// with on high-DPI displays.
void windowPixelSize(WindowHandle window, uint32_t& width, uint32_t& height);

// Fills events with pending events and returns how many it wrote. Events that
// don't fit stay queued for the next call.
uint32_t pollEvents(std::span<InputEvent> events);

// Now, on the same clock as InputEvent::timestampNs.
uint64_t timestampNow();

// The OS handles Slag needs to create a swapchain (ADR 0018). Unsupported for
// video drivers Slag can't present to, such as the offscreen test driver.
Result<void> nativeWindow(WindowHandle window, slag::NativeWindow& native);

} // namespace crucible::platform
