#include "PlatformInternal.h"

#include <crucible/Platform.h>
#include <crucible/SmallVector.h>
#include <crucible/Vector.h>

#include <SDL3/SDL.h>
#include <slag/Swapchain.h>

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cstdio>
#include <cstring>
#include <random>
#include <thread>

namespace crucible::platform {

namespace {

// The latency harness's marker thread and the SDL event type it pushes.
struct LatencyMarkers
{
    std::thread thread;
    std::atomic<bool> running{false};
    uint32_t eventType = 0;
};

LatencyMarkers markers;

// The last pollEvents' typed text, which its TextInput events point into.
Vector<char>& polled()
{
    static Vector<char> text(MemoryTag::Platform);
    return text;
}

// clipboardText's copy of SDL's.
Vector<char>& clipboard()
{
    static Vector<char> text(MemoryTag::Platform);
    return text;
}

// SDL event types of the platform's own: window drags and file dialogs'
// results, queued with SDL's events so they keep their order.
uint32_t dragEventType = 0;
uint32_t dialogEventType = 0;

// A window drag tracked with SDL (Windows, X11): the pointer on the desktop,
// the dragged window moved with it, our window under it.
struct SdlDrag
{
    bool active = false;
    SDL_WindowID dragged = 0;
    float offsetX = 0.0f; // window coordinates
    float offsetY = 0.0f;
    SDL_WindowID over = 0;
    float x = -1.0f; // the last position reported, desktop coordinates
    float y = -1.0f;
};

SdlDrag sdlDrag;

// preferencesPath's copy of SDL's.
Vector<char>& preferences()
{
    static Vector<char> path(MemoryTag::Platform);
    return path;
}

// The open dialog's filter, kept until its answer comes.
struct DialogFilter
{
    Vector<char> name{MemoryTag::Platform};
    Vector<char> pattern{MemoryTag::Platform};
    Vector<char> location{MemoryTag::Platform};
    SDL_DialogFileFilter filter{};
};

DialogFilter& dialogFilter()
{
    static DialogFilter filter;
    return filter;
}

// Keeps a dialog's strings, NUL-terminated, until its answer comes.
DialogFilter& keepFilter(std::string_view filterName, std::string_view pattern, std::string_view location)
{
    DialogFilter& kept = dialogFilter();
    for (auto [text, into] :
         {std::pair{filterName, &kept.name}, std::pair{pattern, &kept.pattern}, std::pair{location, &kept.location}})
    {
        into->assign(std::span<const char>(text.data(), text.size()));
        into->pushBack('\0');
    }
    kept.filter = {kept.name.data(), kept.pattern.data()};
    return kept;
}

// The gamepads opened (SDL sends a pad's events only once it's open).
SDL_Gamepad* gamepads[16] = {};

uint32_t modifiersOf(SDL_Keymod mods)
{
    uint32_t bits = 0;
    bits |= (mods & SDL_KMOD_SHIFT) != 0 ? static_cast<uint32_t>(KeyModifiers::Shift) : 0;
    bits |= (mods & SDL_KMOD_CTRL) != 0 ? static_cast<uint32_t>(KeyModifiers::Ctrl) : 0;
    bits |= (mods & SDL_KMOD_ALT) != 0 ? static_cast<uint32_t>(KeyModifiers::Alt) : 0;
    bits |= (mods & SDL_KMOD_GUI) != 0 ? static_cast<uint32_t>(KeyModifiers::Gui) : 0;
    return bits;
}

void openGamepad(SDL_JoystickID id)
{
    for (SDL_Gamepad*& slot : gamepads)
    {
        if (slot == nullptr)
        {
            slot = SDL_OpenGamepad(id); // null for a pad that's already gone
            return;
        }
    }
}

void closeGamepad(SDL_JoystickID id)
{
    for (SDL_Gamepad*& slot : gamepads)
    {
        if (slot != nullptr && SDL_GetGamepadID(slot) == id)
        {
            SDL_CloseGamepad(slot);
            slot = nullptr;
        }
    }
}

SDL_Window* find(WindowHandle window)
{
    return window.value == 0 ? nullptr : SDL_GetWindowFromID(window.value);
}

// Physical pixels per SDL window coordinate: 1 on Windows and X11, the
// scale on Wayland, where window coordinates are logical (ADR 0066).
float pixelDensity(SDL_WindowID id)
{
    SDL_Window* window = SDL_GetWindowFromID(id);
    const float density = window != nullptr ? SDL_GetWindowPixelDensity(window) : 0.0f;
    return density > 0.0f ? density : 1.0f;
}

// Translates one SDL event; returns false for events Crucible doesn't use.
bool translate(const SDL_Event& event, InputEvent& out)
{
    out = InputEvent{};
    out.timestampNs = event.common.timestamp;
    switch (event.type)
    {
    case SDL_EVENT_QUIT:
        out.type = InputEventType::QuitRequested;
        return true;
    case SDL_EVENT_WINDOW_CLOSE_REQUESTED:
        out.type = InputEventType::WindowCloseRequested;
        out.window.value = event.window.windowID;
        return true;
    case SDL_EVENT_WINDOW_FOCUS_GAINED:
        out.type = InputEventType::WindowFocusGained;
        out.window.value = event.window.windowID;
        return true;
    case SDL_EVENT_WINDOW_PIXEL_SIZE_CHANGED:
        out.type = InputEventType::WindowResized;
        out.window.value = event.window.windowID;
        out.code = event.window.data1;
        out.value = event.window.data2;
        return true;
    case SDL_EVENT_KEY_DOWN:
    case SDL_EVENT_KEY_UP:
        out.type = event.type == SDL_EVENT_KEY_DOWN ? InputEventType::KeyDown : InputEventType::KeyUp;
        out.window.value = event.key.windowID;
        out.code = static_cast<int32_t>(event.key.scancode);
        out.value = event.key.repeat ? 1 : 0;
        out.modifiers = modifiersOf(event.key.mod);
        out.keycode = event.key.key;
        return true;
    case SDL_EVENT_MOUSE_MOTION:
    {
        out.type = InputEventType::MouseMotion;
        out.window.value = event.motion.windowID;
        const float density = pixelDensity(event.motion.windowID);
        out.x = event.motion.x * density;
        out.y = event.motion.y * density;
        out.dx = event.motion.xrel * density;
        out.dy = event.motion.yrel * density;
        out.modifiers = modifiersOf(SDL_GetModState()); // a drag with Ctrl snaps (ADR 0067)
        return true;
    }
    case SDL_EVENT_MOUSE_BUTTON_DOWN:
    case SDL_EVENT_MOUSE_BUTTON_UP:
    {
        out.type =
            event.type == SDL_EVENT_MOUSE_BUTTON_DOWN ? InputEventType::MouseButtonDown : InputEventType::MouseButtonUp;
        out.window.value = event.button.windowID;
        out.code = event.button.button;
        const float density = pixelDensity(event.button.windowID);
        out.x = event.button.x * density;
        out.y = event.button.y * density;
        out.modifiers = modifiersOf(SDL_GetModState()); // Alt+click orbits, Shift+click extends
        return true;
    }
    case SDL_EVENT_MOUSE_WHEEL:
    {
        out.type = InputEventType::MouseWheel;
        out.window.value = event.wheel.windowID;
        const float density = pixelDensity(event.wheel.windowID);
        out.x = event.wheel.mouse_x * density;
        out.y = event.wheel.mouse_y * density;
        const float flip = event.wheel.direction == SDL_MOUSEWHEEL_FLIPPED ? -1.0f : 1.0f;
        out.dx = event.wheel.x * flip;
        out.dy = event.wheel.y * flip;
        out.modifiers = modifiersOf(SDL_GetModState());
        return true;
    }
    case SDL_EVENT_GAMEPAD_ADDED:
        openGamepad(event.gdevice.which);
        out.type = InputEventType::GamepadAdded;
        out.value = static_cast<int32_t>(event.gdevice.which);
        return true;
    case SDL_EVENT_GAMEPAD_REMOVED:
        closeGamepad(event.gdevice.which);
        out.type = InputEventType::GamepadRemoved;
        out.value = static_cast<int32_t>(event.gdevice.which);
        return true;
    case SDL_EVENT_GAMEPAD_BUTTON_DOWN:
    case SDL_EVENT_GAMEPAD_BUTTON_UP:
        if (event.gbutton.button > SDL_GAMEPAD_BUTTON_DPAD_RIGHT)
        {
            return false; // paddles, touchpads, and the like: not mapped
        }
        out.type = event.type == SDL_EVENT_GAMEPAD_BUTTON_DOWN ? InputEventType::GamepadButtonDown
                                                               : InputEventType::GamepadButtonUp;
        out.code = event.gbutton.button;
        out.value = static_cast<int32_t>(event.gbutton.which);
        return true;
    case SDL_EVENT_GAMEPAD_AXIS_MOTION:
        out.type = InputEventType::GamepadAxis;
        out.code = event.gaxis.axis;
        out.value = static_cast<int32_t>(event.gaxis.which);
        // Sticks -32768 to 32767, triggers 0 to 32767.
        out.x = std::max(static_cast<float>(event.gaxis.value) / 32767.0f, -1.0f);
        return true;
    case SDL_EVENT_TEXT_INPUT:
    {
        out.type = InputEventType::TextInput;
        out.window.value = event.text.windowID;
        const size_t length = event.text.text != nullptr ? std::strlen(event.text.text) : 0;
        out.code = static_cast<int32_t>(polled().size());
        out.value = static_cast<int32_t>(length);
        for (size_t i = 0; i < length; ++i)
        {
            polled().pushBack(event.text.text[i]);
        }
        return true;
    }
    case SDL_EVENT_WINDOW_DISPLAY_SCALE_CHANGED:
        out.type = InputEventType::DisplayScaleChanged;
        out.window.value = event.window.windowID;
        out.x = SDL_GetWindowDisplayScale(SDL_GetWindowFromID(event.window.windowID));
        return true;
    default:
        if (dragEventType != 0 && event.type == dragEventType)
        {
            out.type = static_cast<InputEventType>(event.user.code);
            out.window.value = event.user.windowID;
            const auto x = static_cast<uint32_t>(reinterpret_cast<uintptr_t>(event.user.data1));
            const auto y = static_cast<uint32_t>(reinterpret_cast<uintptr_t>(event.user.data2));
            std::memcpy(&out.x, &x, sizeof(float));
            std::memcpy(&out.y, &y, sizeof(float));
            return true;
        }
        if (dialogEventType != 0 && event.type == dialogEventType)
        {
            // The path SDL_strdup'd on the dialog's thread, into the polled text.
            out.type = InputEventType::FileDialogResult;
            const char* path = static_cast<const char*>(event.user.data1);
            const size_t length = path != nullptr ? std::strlen(path) : 0;
            out.code = static_cast<int32_t>(polled().size());
            out.value = static_cast<int32_t>(length);
            for (size_t i = 0; i < length; ++i)
            {
                polled().pushBack(path[i]);
            }
            SDL_free(event.user.data1);
            return true;
        }
        if (markers.eventType != 0 && event.type == markers.eventType)
        {
            out.type = InputEventType::LatencyMarker;
            out.code = event.user.code;
            return true;
        }
        return false;
    }
}

// Our window under a desktop point, leaving out the dragged one; 0 for none.
SDL_WindowID windowAt(float x, float y, SDL_WindowID except)
{
    int count = 0;
    SDL_Window** windows = SDL_GetWindows(&count);
    SDL_WindowID found = 0;
    for (int i = 0; i < count && found == 0; ++i)
    {
        int left = 0, top = 0, width = 0, height = 0;
        const SDL_WindowID id = SDL_GetWindowID(windows[i]);
        if (id == except || (SDL_GetWindowFlags(windows[i]) & (SDL_WINDOW_HIDDEN | SDL_WINDOW_MINIMIZED)) != 0 ||
            !SDL_GetWindowPosition(windows[i], &left, &top) || !SDL_GetWindowSize(windows[i], &width, &height))
        {
            continue;
        }
        if (x >= left && y >= top && x < left + width && y < top + height)
        {
            found = id;
        }
    }
    SDL_free(windows);
    return found;
}

// Each poll during an SDL drag: the dragged window follows the pointer, and
// the window under it hears where it is, until the button is released.
void trackSdlDrag()
{
    if (!sdlDrag.active)
    {
        return;
    }
    float x = 0.0f, y = 0.0f;
    const SDL_MouseButtonFlags buttons = SDL_GetGlobalMouseState(&x, &y);
    if (SDL_Window* dragged = SDL_GetWindowFromID(sdlDrag.dragged))
    {
        SDL_SetWindowPosition(dragged, static_cast<int>(x - sdlDrag.offsetX), static_cast<int>(y - sdlDrag.offsetY));
    }
    const SDL_WindowID over = windowAt(x, y, sdlDrag.dragged);
    int left = 0, top = 0;
    if (over != 0)
    {
        SDL_GetWindowPosition(SDL_GetWindowFromID(over), &left, &top);
    }
    const float density = pixelDensity(over);
    const float localX = (x - static_cast<float>(left)) * density, localY = (y - static_cast<float>(top)) * density;
    if ((buttons & SDL_BUTTON_LMASK) == 0)
    {
        sdlDrag.active = false;
        if (over != 0)
        {
            pushDragEvent(InputEventType::DragDropped, over, localX, localY);
        }
        else
        {
            pushDragEvent(InputEventType::DragCanceled, 0, 0.0f, 0.0f);
        }
        return;
    }
    if (over != sdlDrag.over || x != sdlDrag.x || y != sdlDrag.y)
    {
        sdlDrag.over = over;
        sdlDrag.x = x;
        sdlDrag.y = y;
        pushDragEvent(InputEventType::DragMoved, over, localX, localY);
    }
}

// The resize border's width in window coordinates, carried in the hit
// test's data pointer (no allocation per window).
SDL_HitTestResult SDLCALL resizeHitTest(SDL_Window* window, const SDL_Point* point, void* data)
{
    const auto bits = static_cast<uint32_t>(reinterpret_cast<uintptr_t>(data));
    float border = 0.0f;
    std::memcpy(&border, &bits, sizeof(float));
    int width = 0, height = 0;
    if ((SDL_GetWindowFlags(window) & (SDL_WINDOW_MAXIMIZED | SDL_WINDOW_FULLSCREEN)) != 0 ||
        !SDL_GetWindowSize(window, &width, &height))
    {
        return SDL_HITTEST_NORMAL;
    }
    const auto x = static_cast<float>(point->x), y = static_cast<float>(point->y);
    const bool left = x < border, right = x >= static_cast<float>(width) - border;
    const bool top = y < border, bottom = y >= static_cast<float>(height) - border;
    if (top)
    {
        return left ? SDL_HITTEST_RESIZE_TOPLEFT : right ? SDL_HITTEST_RESIZE_TOPRIGHT : SDL_HITTEST_RESIZE_TOP;
    }
    if (bottom)
    {
        return left    ? SDL_HITTEST_RESIZE_BOTTOMLEFT
               : right ? SDL_HITTEST_RESIZE_BOTTOMRIGHT
                       : SDL_HITTEST_RESIZE_BOTTOM;
    }
    return left ? SDL_HITTEST_RESIZE_LEFT : right ? SDL_HITTEST_RESIZE_RIGHT : SDL_HITTEST_NORMAL;
}

bool sdlDrivesDrags()
{
    const char* driver = SDL_GetCurrentVideoDriver();
    return driver != nullptr && (std::strcmp(driver, "windows") == 0 || std::strcmp(driver, "x11") == 0);
}

// The dialog's answer, on whatever thread SDL calls it from.
void SDLCALL dialogAnswered(void*, const char* const* files, int)
{
    SDL_Event event{};
    event.type = dialogEventType;
    event.user.data1 = files != nullptr && files[0] != nullptr ? SDL_strdup(files[0]) : nullptr;
    if (!SDL_PushEvent(&event))
    {
        SDL_free(event.user.data1);
    }
}

} // namespace

void pushDragEvent(InputEventType type, SDL_WindowID window, float x, float y)
{
    SDL_Event event{};
    event.type = dragEventType;
    event.user.code = static_cast<Sint32>(type);
    event.user.windowID = window;
    uint32_t bits = 0;
    std::memcpy(&bits, &x, sizeof(float));
    event.user.data1 = reinterpret_cast<void*>(static_cast<uintptr_t>(bits));
    std::memcpy(&bits, &y, sizeof(float));
    event.user.data2 = reinterpret_cast<void*>(static_cast<uintptr_t>(bits));
    (void)SDL_PushEvent(&event);
}

Result<void> initialize()
{
    // Windows: keyboard events from SDL's raw input thread, stamped on
    // arrival to well under a millisecond, instead of window messages
    // (GetMessageTime on a ~15.6 ms tick: about +-7 ms measured; ADR 0018's
    // amendment). Normal priority, so the SDL_WINDOWS_RAW_KEYBOARD
    // environment variable still overrides it. No effect elsewhere.
    SDL_SetHint(SDL_HINT_WINDOWS_RAW_KEYBOARD, "1");
    if (!SDL_Init(SDL_INIT_VIDEO))
    {
        std::fprintf(stderr, "crucible: SDL_Init failed: %s\n", SDL_GetError());
        return Status::Unsupported;
    }
    const uint32_t types = SDL_RegisterEvents(2);
    dragEventType = types != 0 ? types : 0;
    dialogEventType = types != 0 ? types + 1 : 0;
#if defined(CRUCIBLE_PLATFORM_WAYLAND)
    wayland::start();
#endif
    // Gamepads are optional: without them the UI works by mouse and keyboard.
    if (!SDL_InitSubSystem(SDL_INIT_GAMEPAD))
    {
        std::fprintf(stderr, "crucible: no gamepad support: %s\n", SDL_GetError());
    }
    return {};
}

void shutdown()
{
    stopLatencyMarkers();
    markers.eventType = 0;
    for (SDL_Gamepad*& slot : gamepads)
    {
        if (slot != nullptr)
        {
            SDL_CloseGamepad(slot);
            slot = nullptr;
        }
    }
    sdlDrag = SdlDrag{};
#if defined(CRUCIBLE_PLATFORM_WAYLAND)
    wayland::stop();
#endif
    // Their memory goes back with the platform's, not at exit.
    polled() = Vector<char>(MemoryTag::Platform);
    preferences() = Vector<char>(MemoryTag::Platform);
    dialogFilter() = DialogFilter{};
    clipboard() = Vector<char>(MemoryTag::Platform);
    SDL_Quit();
}

Result<WindowHandle> createWindow(uint32_t width, uint32_t height, std::string_view title)
{
    // SDL takes a null-terminated title; a string_view may not be one.
    SmallVector<char, 256> terminated(MemoryTag::Platform);
    terminated.assign(std::span<const char>(title.data(), title.size()));
    terminated.pushBack('\0');
    SDL_Window* window = SDL_CreateWindow(terminated.data(), static_cast<int>(width), static_cast<int>(height),
                                          SDL_WINDOW_RESIZABLE | SDL_WINDOW_HIGH_PIXEL_DENSITY);
    if (window == nullptr)
    {
        std::fprintf(stderr, "crucible: SDL_CreateWindow failed: %s\n", SDL_GetError());
        return Status::Unsupported;
    }
    return WindowHandle{SDL_GetWindowID(window)};
}

void destroyWindow(WindowHandle window)
{
    if (SDL_Window* sdlWindow = find(window))
    {
        SDL_DestroyWindow(sdlWindow);
    }
}

WindowSize windowPixelSize(WindowHandle window)
{
    int w = 0;
    int h = 0;
    if (SDL_Window* sdlWindow = find(window))
    {
        SDL_GetWindowSizeInPixels(sdlWindow, &w, &h);
    }
    return WindowSize{static_cast<uint32_t>(w), static_cast<uint32_t>(h)};
}

bool windowMinimized(WindowHandle window)
{
    SDL_Window* sdlWindow = find(window);
    return sdlWindow != nullptr && (SDL_GetWindowFlags(sdlWindow) & SDL_WINDOW_MINIMIZED) != 0;
}

float windowDisplayScale(WindowHandle window)
{
    SDL_Window* sdlWindow = find(window);
    const float scale = sdlWindow != nullptr ? SDL_GetWindowDisplayScale(sdlWindow) : 0.0f;
    return scale > 0 ? scale : 1.0f;
}

uint32_t pollEvents(std::span<InputEvent> events)
{
    polled().clear();
    // Drag events go into SDL's queue first, after SDL has read the
    // display's events (ours included, which our queue holds).
    SDL_PumpEvents();
#if defined(CRUCIBLE_PLATFORM_WAYLAND)
    wayland::dispatch();
#endif
    trackSdlDrag();
    uint32_t count = 0;
    SDL_Event event;
    while (count < events.size() && SDL_PollEvent(&event))
    {
        if (translate(event, events[count]))
        {
            ++count;
        }
    }
    return count;
}

uint32_t polledText(std::span<uint8_t> out)
{
    const Vector<char>& text = polled();
    std::memcpy(out.data(), text.data(), std::min(out.size(), text.size()));
    return static_cast<uint32_t>(text.size());
}

void startTextInput(WindowHandle window)
{
    if (SDL_Window* sdlWindow = find(window))
    {
        SDL_StartTextInput(sdlWindow);
    }
}

void stopTextInput(WindowHandle window)
{
    if (SDL_Window* sdlWindow = find(window))
    {
        SDL_StopTextInput(sdlWindow);
    }
}

std::string_view clipboardText()
{
    Vector<char>& copy = clipboard();
    copy.clear();
    if (char* text = SDL_GetClipboardText())
    {
        copy.assign(std::span<const char>(text, std::strlen(text)));
        SDL_free(text);
    }
    return {copy.data(), copy.size()};
}

bool setClipboardText(std::string_view text)
{
    SmallVector<char, 256> terminated(MemoryTag::Platform);
    terminated.assign(std::span<const char>(text.data(), text.size()));
    terminated.pushBack('\0');
    return SDL_SetClipboardText(terminated.data());
}

Vector2 windowPosition(WindowHandle window)
{
    int x = 0, y = 0;
    SDL_Window* sdlWindow = find(window);
    if (sdlWindow == nullptr || !sdlDrivesDrags() || !SDL_GetWindowPosition(sdlWindow, &x, &y))
    {
        return {};
    }
    return {static_cast<float>(x), static_cast<float>(y)};
}

void setWindowPosition(WindowHandle window, Vector2 position)
{
    if (SDL_Window* sdlWindow = find(window); sdlWindow != nullptr && sdlDrivesDrags())
    {
        SDL_SetWindowPosition(sdlWindow, static_cast<int>(position.x), static_cast<int>(position.y));
    }
}

Vector2 pointerScreenPosition()
{
    float x = 0.0f, y = 0.0f;
    if (!sdlDrivesDrags())
    {
        return {};
    }
    SDL_GetGlobalMouseState(&x, &y);
    return {x, y};
}

WindowDragSupport windowDragSupport()
{
#if defined(CRUCIBLE_PLATFORM_WAYLAND)
    if (wayland::running())
    {
        return wayland::dragSupport();
    }
#endif
    return sdlDrivesDrags() ? WindowDragSupport::Full : WindowDragSupport::None;
}

bool startWindowDrag(WindowHandle window, WindowHandle draggedWindow, Vector2 offset)
{
    SDL_Window* source = find(window);
    SDL_Window* dragged = find(draggedWindow);
    if (source == nullptr || dragEventType == 0)
    {
        return false;
    }
    // The offset in window coordinates, as the platforms place windows.
    const float density = dragged != nullptr ? pixelDensity(draggedWindow.value) : 1.0f;
#if defined(CRUCIBLE_PLATFORM_WAYLAND)
    if (wayland::running())
    {
        return wayland::startDrag(source, dragged, offset.x / density, offset.y / density);
    }
#endif
    if (!sdlDrivesDrags() || (SDL_GetGlobalMouseState(nullptr, nullptr) & SDL_BUTTON_LMASK) == 0)
    {
        return false;
    }
    sdlDrag = SdlDrag{.active = true,
                      .dragged = dragged != nullptr ? draggedWindow.value : 0,
                      .offsetX = offset.x / density,
                      .offsetY = offset.y / density};
    return true;
}

void stopWindowDrag()
{
    if (sdlDrag.active)
    {
        sdlDrag.active = false;
        pushDragEvent(InputEventType::DragCanceled, 0, 0.0f, 0.0f);
    }
#if defined(CRUCIBLE_PLATFORM_WAYLAND)
    wayland::stopDrag();
#endif
}

void setWindowBordered(WindowHandle window, bool bordered)
{
    if (SDL_Window* sdlWindow = find(window))
    {
        SDL_SetWindowBordered(sdlWindow, bordered);
    }
}

void setWindowParent(WindowHandle window, WindowHandle parent)
{
    if (SDL_Window* sdlWindow = find(window))
    {
        SDL_SetWindowParent(sdlWindow, find(parent));
    }
}

void setWindowResizeBorder(WindowHandle window, float pixels)
{
    SDL_Window* sdlWindow = find(window);
    if (sdlWindow == nullptr)
    {
        return;
    }
    const float border = pixels / pixelDensity(window.value);
    uint32_t bits = 0;
    std::memcpy(&bits, &border, sizeof(float));
    SDL_SetWindowHitTest(sdlWindow, pixels > 0.0f ? resizeHitTest : nullptr,
                         reinterpret_cast<void*>(static_cast<uintptr_t>(bits)));
}

bool startWindowMove(WindowHandle window)
{
    SDL_Window* sdlWindow = find(window);
    const char* driver = SDL_GetCurrentVideoDriver();
    if (sdlWindow == nullptr || driver == nullptr)
    {
        return false;
    }
#if defined(CRUCIBLE_PLATFORM_WAYLAND)
    if (std::strcmp(driver, "wayland") == 0)
    {
        return wayland::startMove(sdlWindow);
    }
#endif
#if defined(CRUCIBLE_PLATFORM_X11)
    if (std::strcmp(driver, "x11") == 0)
    {
        float x = 0.0f, y = 0.0f;
        return (SDL_GetGlobalMouseState(&x, &y) & SDL_BUTTON_LMASK) != 0 && x11::startMove(sdlWindow, x, y);
    }
#endif
#if defined(_WIN32)
    if (std::strcmp(driver, "windows") == 0)
    {
        return (SDL_GetGlobalMouseState(nullptr, nullptr) & SDL_BUTTON_LMASK) != 0 && windows::startMove(sdlWindow);
    }
#endif
    return false;
}

void minimizeWindow(WindowHandle window)
{
    if (SDL_Window* sdlWindow = find(window))
    {
        SDL_MinimizeWindow(sdlWindow);
    }
}

void maximizeWindow(WindowHandle window)
{
    if (SDL_Window* sdlWindow = find(window))
    {
        SDL_MaximizeWindow(sdlWindow);
    }
}

void restoreWindow(WindowHandle window)
{
    if (SDL_Window* sdlWindow = find(window))
    {
        SDL_RestoreWindow(sdlWindow);
    }
}

bool windowMaximized(WindowHandle window)
{
    SDL_Window* sdlWindow = find(window);
    return sdlWindow != nullptr && (SDL_GetWindowFlags(sdlWindow) & SDL_WINDOW_MAXIMIZED) != 0;
}

void showWindowMenu(WindowHandle window, Vector2 position)
{
    if (SDL_Window* sdlWindow = find(window))
    {
        const float density = pixelDensity(window.value);
        SDL_ShowWindowSystemMenu(sdlWindow, static_cast<int>(position.x / density),
                                 static_cast<int>(position.y / density));
    }
}

void showOpenFileDialog(WindowHandle window, std::string_view filterName, std::string_view pattern)
{
    if (dialogEventType == 0)
    {
        return;
    }
    // SDL may read the filter after this returns; it's kept until the next dialog.
    DialogFilter& kept = keepFilter(filterName, pattern, {});
    SDL_ShowOpenFileDialog(dialogAnswered, nullptr, find(window), pattern.empty() ? nullptr : &kept.filter,
                           pattern.empty() ? 0 : 1, nullptr, false);
}

void showSaveFileDialog(WindowHandle window, std::string_view filterName, std::string_view pattern,
                        std::string_view defaultLocation)
{
    if (dialogEventType == 0)
    {
        return;
    }
    DialogFilter& kept = keepFilter(filterName, pattern, defaultLocation);
    SDL_ShowSaveFileDialog(dialogAnswered, nullptr, find(window), pattern.empty() ? nullptr : &kept.filter,
                           pattern.empty() ? 0 : 1, defaultLocation.empty() ? nullptr : kept.location.data());
}

std::string_view preferencesPath()
{
    Vector<char>& path = preferences();
    if (path.empty())
    {
        if (char* sdlPath = SDL_GetPrefPath(nullptr, "Crucible"))
        {
            path.assign(std::span<const char>(sdlPath, std::strlen(sdlPath)));
            SDL_free(sdlPath);
        }
    }
    return {path.data(), path.size()};
}

uint64_t timestampNow()
{
    return SDL_GetTicksNS();
}

bool eventTimestampsAreArrivals()
{
    const char* driver = SDL_GetCurrentVideoDriver();
    return driver == nullptr || std::strcmp(driver, "x11") != 0;
}

Result<void> nativeWindow(WindowHandle window, slag::NativeWindow& native)
{
    SDL_Window* sdlWindow = find(window);
    if (sdlWindow == nullptr)
    {
        return Status::NotFound;
    }

    const SDL_PropertiesID properties = SDL_GetWindowProperties(sdlWindow);
    const char* driver = SDL_GetCurrentVideoDriver();
    if (driver != nullptr && std::strcmp(driver, "windows") == 0)
    {
        native.kind = slag::WindowKind::Win32;
        native.display = SDL_GetPointerProperty(properties, SDL_PROP_WINDOW_WIN32_INSTANCE_POINTER, nullptr);
        native.window =
            reinterpret_cast<uint64_t>(SDL_GetPointerProperty(properties, SDL_PROP_WINDOW_WIN32_HWND_POINTER, nullptr));
    }
    else if (driver != nullptr && std::strcmp(driver, "wayland") == 0)
    {
        native.kind = slag::WindowKind::Wayland;
        native.display = SDL_GetPointerProperty(properties, SDL_PROP_WINDOW_WAYLAND_DISPLAY_POINTER, nullptr);
        native.window = reinterpret_cast<uint64_t>(
            SDL_GetPointerProperty(properties, SDL_PROP_WINDOW_WAYLAND_SURFACE_POINTER, nullptr));
    }
    else if (driver != nullptr && std::strcmp(driver, "x11") == 0)
    {
        native.kind = slag::WindowKind::Xlib;
        native.display = SDL_GetPointerProperty(properties, SDL_PROP_WINDOW_X11_DISPLAY_POINTER, nullptr);
        native.window = static_cast<uint64_t>(SDL_GetNumberProperty(properties, SDL_PROP_WINDOW_X11_WINDOW_NUMBER, 0));
    }
    else
    {
        return Status::Unsupported;
    }
    return native.window != 0 ? Result<void>() : Result<void>(Status::Unsupported);
}

Result<void> startLatencyMarkers(uint32_t meanIntervalUs)
{
    if (markers.running || meanIntervalUs == 0)
    {
        return Status::Unsupported;
    }
    if (markers.eventType == 0)
    {
        markers.eventType = SDL_RegisterEvents(1);
        if (markers.eventType == 0)
        {
            return Status::Unsupported;
        }
    }
    markers.running = true;
    markers.thread = std::thread([meanIntervalUs] {
        // A fixed seed: runs are repeatable.
        std::minstd_rand random(12345);
        std::uniform_int_distribution<uint32_t> interval(meanIntervalUs / 2, meanIntervalUs + meanIntervalUs / 2);
        int32_t sequence = 0;
        while (markers.running)
        {
            std::this_thread::sleep_for(std::chrono::microseconds(interval(random)));
            SDL_Event event{};
            event.type = markers.eventType;
            event.user.code = sequence++;
            // A zero timestamp is filled in by SDL with the time it entered
            // the queue, on the same clock as real events.
            (void)SDL_PushEvent(&event);
        }
    });
    return {};
}

void stopLatencyMarkers()
{
    if (markers.running)
    {
        markers.running = false;
        markers.thread.join();
    }
}

// ---------------------------------------------------------------------------
// Files
// ---------------------------------------------------------------------------

namespace {

// SDL wants NUL-terminated paths.
struct PathString
{
    SmallVector<char, 256> text{MemoryTag::Platform};

    explicit PathString(std::string_view path)
    {
        text.resize(path.size() + 1);
        std::memcpy(text.data(), path.data(), path.size());
        text[path.size()] = '\0';
    }

    const char* c_str() const
    {
        return text.data();
    }
};

// Opening failed: NotFound if there's nothing there, IoError otherwise.
Status openFailure(const char* path)
{
    SDL_PathInfo info;
    return SDL_GetPathInfo(path, &info) ? Status::IoError : Status::NotFound;
}

std::atomic<uint32_t> temporaryCounter{0};

} // namespace

Result<uint64_t> fileSize(std::string_view utf8Path)
{
    const PathString path(utf8Path);
    SDL_PathInfo info;
    if (!SDL_GetPathInfo(path.c_str(), &info))
    {
        return Status::NotFound;
    }
    if (info.type != SDL_PATHTYPE_FILE)
    {
        return Status::IoError;
    }
    return info.size;
}

Result<void> readFile(std::string_view utf8Path, std::span<std::byte> destination)
{
    const PathString path(utf8Path);
    SDL_IOStream* file = SDL_IOFromFile(path.c_str(), "rb");
    if (file == nullptr)
    {
        return openFailure(path.c_str());
    }
    size_t done = 0;
    while (done < destination.size())
    {
        const size_t read = SDL_ReadIO(file, destination.data() + done, destination.size() - done);
        if (read == 0)
        {
            break;
        }
        done += read;
    }
    const bool failed = SDL_GetIOStatus(file) == SDL_IO_STATUS_ERROR;
    SDL_CloseIO(file);
    if (failed)
    {
        return Status::IoError;
    }
    return done == destination.size() ? Result<void>() : Result<void>(Status::InvalidData);
}

Result<void> writeFileAtomically(std::string_view utf8Path, std::span<const std::byte> contents)
{
    // Unique among threads and, through the clock, among processes.
    char suffix[64];
    std::snprintf(suffix, sizeof(suffix), ".tmp-%u-%llu", temporaryCounter.fetch_add(1),
                  static_cast<unsigned long long>(SDL_GetTicksNS()));
    SmallVector<char, 256> temporary(MemoryTag::Platform);
    temporary.resize(utf8Path.size() + std::strlen(suffix) + 1);
    std::memcpy(temporary.data(), utf8Path.data(), utf8Path.size());
    std::memcpy(temporary.data() + utf8Path.size(), suffix, std::strlen(suffix) + 1);

    SDL_IOStream* file = SDL_IOFromFile(temporary.data(), "wb");
    if (file == nullptr)
    {
        return Status::IoError;
    }
    const bool written = SDL_WriteIO(file, contents.data(), contents.size()) == contents.size();
    // Closing flushes; a full disk can show up only here.
    const bool closed = SDL_CloseIO(file);
    const PathString path(utf8Path);
    if (!written || !closed || !SDL_RenamePath(temporary.data(), path.c_str()))
    {
        (void)SDL_RemovePath(temporary.data());
        return Status::IoError;
    }
    return {};
}

} // namespace crucible::platform
