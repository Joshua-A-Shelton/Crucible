#pragma once

// The platform layer (ADR 0018): windows and input, implemented with SDL3
// behind Crucible's own API. SDL never appears in a public header. Names are
// placeholders (docs/naming.md).
//
// Threading: everything here runs on the thread that called initialize(),
// which is the launcher's main thread (SDL requires it for windows and events).

#include <crucible/Export.h>
#include <crucible/Expose.h>
#include <crucible/Math.h>
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
    KeyDown,              // code = scancode, value = 1 if a key repeat; keycode; modifiers
    KeyUp,                // code = scancode; keycode; modifiers
    MouseMotion,          // x, y = position; dx, dy = relative motion (pointer positions in pixels)
    MouseButtonDown,      // code = button, x, y = position
    MouseButtonUp,        // code = button, x, y = position
    LatencyMarker,        // synthetic input for the latency harness; code = sequence number
    // Added for the UI (ADR 0062):
    MouseWheel,          // x, y = position; dx, dy = scrolled (y up, in notches); modifiers
    GamepadAdded,        // value = the gamepad's ID
    GamepadRemoved,      // value = the gamepad's ID
    GamepadButtonDown,   // code = GamepadButton, value = the gamepad's ID
    GamepadButtonUp,     // code = GamepadButton, value = the gamepad's ID
    GamepadAxis,         // code = GamepadAxis, value = the gamepad's ID, x = -1 to 1 (triggers 0 to 1)
    DisplayScaleChanged, // (window), x = its new display scale
    // Added for text editing (ADR 0065):
    TextInput, // (window), code = offset, value = length of its UTF-8 in polledText()
    // Added for the editor (ADR 0066):
    DragMoved,        // a window drag over (window), 0 for none of ours; x, y = position in it
    DragDropped,      // released over (window); x, y = position in it
    DragCanceled,     // released elsewhere, or ended by the system
    FileDialogResult, // code = offset, value = length (0: none chosen) of the path's UTF-8 in polledText()
    // Added for the asset browser (ADR 0071):
    WindowFocusGained, // (window) became the one with keyboard focus
};

// What startWindowDrag can do here (ADR 0066).
enum class CRUCIBLE_EXPOSE WindowDragSupport : uint32_t
{
    None,     // no drags between windows (a platform without them, or headless)
    DropOnly, // drags reach our windows, but the dragged window doesn't move with them
    Full,     // and the dragged window follows the pointer
};

// Held modifier keys, as InputEvent::modifiers bits.
enum class CRUCIBLE_EXPOSE KeyModifiers : uint32_t
{
    None = 0,
    Shift = 1,
    Ctrl = 2,
    Alt = 4,
    Gui = 8, // the Windows or Command key
};

// Numbered as SDL's, by position: South is A on Xbox pads, Cross on
// PlayStation ones, B on Nintendo ones.
enum class CRUCIBLE_EXPOSE GamepadButton : uint32_t
{
    South,
    East,
    West,
    North,
    Back,
    Guide,
    Start,
    LeftStick,
    RightStick,
    LeftShoulder,
    RightShoulder,
    DpadUp,
    DpadDown,
    DpadLeft,
    DpadRight,
};

enum class CRUCIBLE_EXPOSE GamepadAxis : uint32_t
{
    LeftX,
    LeftY, // down is positive
    RightX,
    RightY,
    LeftTrigger,
    RightTrigger,
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
    uint32_t modifiers; // KeyModifiers bits, for key, wheel, and mouse events
    // Added for shortcuts (ADR 0066): the key as the user's layout labels it
    // (SDL's keycode: the character for printable keys, else the scancode
    // with bit 30 set), for key events. Shortcuts match it; navigation keys
    // keep to scancodes, which don't move between layouts.
    uint32_t keycode;
};

// Called by the launcher before the engine starts and after it returns (ADR
// 0011: C++ owns platform setup and shutdown), so not exposed to C#.
CRUCIBLE_EXPORT Result<void> initialize();
CRUCIBLE_EXPORT void shutdown();

CRUCIBLE_EXPORT CRUCIBLE_EXPOSE Result<WindowHandle> createWindow(uint32_t width, uint32_t height,
                                                                  std::string_view title);
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE void destroyWindow(WindowHandle window);

// The window's size in pixels, which can differ from the size it was created
// with on high-DPI displays. Zero for a window that doesn't exist.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE WindowSize windowPixelSize(WindowHandle window);

// Whether the window is minimized; the renderer skips frames meanwhile.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE bool windowMinimized(WindowHandle window);
// Physical pixels per logical one the OS asks for on the window's display
// (its DPI scale); 1 for a window that doesn't exist.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE float windowDisplayScale(WindowHandle window);

// Fills events with pending events and returns how many it wrote. Events that
// don't fit stay queued for the next call. They come from the installed
// input source: the platform's, unless something replaced it.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE uint32_t pollEvents(std::span<InputEvent> events);

// The UTF-8 text of the last pollEvents' TextInput events, which point into
// it: copied into `out` (as much as fits); returns its whole length.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE uint32_t polledText(std::span<uint8_t> out);

// The input contract (ADR 0076): what fills each frame's event buffer. The
// Input phase's system polls it (pollEvents, polledText); the UI, the game,
// and the editor read the buffer. The platform's source (SDL) is the
// default; a replacement (recorded input played back, a network or test
// stream) is installed in its place, and can wrap platformInputSource() to
// keep the window's own events. Main thread.
struct InputSource
{
    uint32_t (*poll)(std::span<InputEvent> events); // as pollEvents
    uint32_t (*text)(std::span<uint8_t> out);       // as polledText
};

CRUCIBLE_EXPORT void installInputSource(const InputSource& source);
// The installed source.
CRUCIBLE_EXPORT const InputSource& inputSource();
// The platform's own source, which is installed until something replaces it.
CRUCIBLE_EXPORT const InputSource& platformInputSource();

// Typed text arrives as TextInput events while text input is on for the
// window (on-screen keyboards show; ADR 0065); the UI turns it on while a
// text field has focus.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE void startTextInput(WindowHandle window);
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE void stopTextInput(WindowHandle window);

// The system clipboard's text ("" when empty or unavailable), and setting
// it; false if the platform couldn't.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE std::string_view clipboardText();
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE bool setClipboardText(std::string_view text);

// Window placement, in the desktop's coordinates (Windows and X11). Wayland
// doesn't let apps place their windows or know where they are: windowPosition
// is (0, 0) there, and setWindowPosition does nothing.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE Vector2 windowPosition(WindowHandle window);
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE void setWindowPosition(WindowHandle window, Vector2 position);
// The pointer on the desktop, in the same coordinates; (0, 0) on Wayland.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE Vector2 pointerScreenPosition();

// Window drags (ADR 0066): a drag of something between our windows, such
// as a panel torn off one window and docked in another. Started while a
// mouse button pressed in `window` is held; the platform then sends
// DragMoved as the pointer moves over our windows, and DragDropped or
// DragCanceled when it's released. Where windowDragSupport() is Full,
// `draggedWindow` (0 for none) moves with the pointer, `offset` (pixels from
// its top left) under it; give a new window before its first frame is
// drawn. False if no drag could start (no press held, or None).
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE WindowDragSupport windowDragSupport();
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE bool startWindowDrag(WindowHandle window, WindowHandle draggedWindow, Vector2 offset);
// Cancels a drag in progress (DragCanceled follows), where the platform
// lets apps; on Wayland the compositor ends it (Escape, or the release).
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE void stopWindowDrag();

// Drawing a window's frame yourself (ADR 0066): without the system's
// border and title bar, the app draws its own title bar, moves the window
// with startWindowMove when its title bar is dragged, and lets the edges
// resize it (setWindowResizeBorder, pixels; 0 for none, the default).
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE void setWindowBordered(WindowHandle window, bool bordered);
// Makes a window a child of another (0: of none): it stays above its
// parent, goes where it goes (desktops, minimizing), and on Windows leaves
// the task bar to it. For tool windows, such as an editor's floating ones.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE void setWindowParent(WindowHandle window, WindowHandle parent);
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE void setWindowResizeBorder(WindowHandle window, float pixels);
// Starts a move by the system (snapping and tiling as from its own title
// bar), from a mouse button pressed in the window and still held. False if
// no move could start (no press held, or a platform without moves, as
// headless).
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE bool startWindowMove(WindowHandle window);
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE void minimizeWindow(WindowHandle window);
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE void maximizeWindow(WindowHandle window);
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE void restoreWindow(WindowHandle window);
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE bool windowMaximized(WindowHandle window);
// The system's menu for the window (move, size, close ...) at a position in
// it, in pixels, where the platform has one.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE void showWindowMenu(WindowHandle window, Vector2 position);

// The system's open-file dialog (ADR 0066), over `window` (0 for none),
// showing files matching `pattern` (extensions without dots, separated by
// ';', such as "crproject"; empty for all) under `filterName`. The choice
// arrives later as a FileDialogResult event.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE void showOpenFileDialog(WindowHandle window, std::string_view filterName,
                                                        std::string_view pattern);
// The system's save-file dialog (ADR 0068), alike, starting at
// `defaultLocation` (a folder or a file; empty for the system's choice); its
// answer is a FileDialogResult too.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE void showSaveFileDialog(WindowHandle window, std::string_view filterName,
                                                        std::string_view pattern, std::string_view defaultLocation);

// The folder for the user's settings (ADR 0066), created if needed and
// ending in a separator: ~/.local/share/Crucible/ on Linux,
// %APPDATA%\Crucible\ on Windows. Empty if there's none.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE std::string_view preferencesPath();

// Now, on the same clock as InputEvent::timestampNs.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE uint64_t timestampNow();

// Whether InputEvent::timestampNs is when the input arrived (Wayland,
// Windows) rather than when SDL processed it, which can be a frame later.
// False under X11, where SDL doesn't convert the X server's event times
// (ADR 0018's amendment); timing code then treats a timestamp as "no earlier
// than the poll".
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE bool eventTimestampsAreArrivals();

// The latency harness's synthetic input (ADR 0003): a background thread
// pushes LatencyMarker events into the event queue at random intervals
// (uniform between half and one and a half times the mean), so they arrive
// at random points in the frame, as real input does. Their timestamps are
// when they entered the queue. Stopping waits for the thread.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE_STATUS Result<void> startLatencyMarkers(uint32_t meanIntervalUs);
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE void stopLatencyMarkers();

// Files, by UTF-8 path (ADR 0039). Not exposed: C# has its own file API, and
// the core reads and writes its binary files itself.
//
// The file's size in bytes; NotFound if it doesn't exist.
CRUCIBLE_EXPORT Result<uint64_t> fileSize(std::string_view utf8Path);
// Reads the file's first destination.size() bytes; InvalidData if it is
// shorter.
CRUCIBLE_EXPORT Result<void> readFile(std::string_view utf8Path, std::span<std::byte> destination);
// Writes the whole file under a temporary name beside it, then renames it
// over `utf8Path`: a crash or full disk never leaves a half-written file
// under the real name.
CRUCIBLE_EXPORT Result<void> writeFileAtomically(std::string_view utf8Path, std::span<const std::byte> contents);

// The OS handles Slag needs to create a swapchain (ADR 0018). Unsupported for
// video drivers Slag can't present to, such as the offscreen test driver. Not
// exposed: the core creates swapchains itself.
CRUCIBLE_EXPORT Result<void> nativeWindow(WindowHandle window, slag::NativeWindow& native);

} // namespace crucible::platform
