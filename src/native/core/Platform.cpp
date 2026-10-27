#include <crucible/Platform.h>

#include <SDL3/SDL.h>
#include <slag/Swapchain.h>

#include <cstdio>
#include <cstring>
#include <string>

namespace crucible::platform {

namespace {

SDL_Window* find(WindowHandle window)
{
    return window.value == 0 ? nullptr : SDL_GetWindowFromID(window.value);
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
        out.window = event.window.windowID;
        return true;
    case SDL_EVENT_WINDOW_PIXEL_SIZE_CHANGED:
        out.type = InputEventType::WindowResized;
        out.window = event.window.windowID;
        out.code = event.window.data1;
        out.value = event.window.data2;
        return true;
    case SDL_EVENT_KEY_DOWN:
    case SDL_EVENT_KEY_UP:
        out.type = event.type == SDL_EVENT_KEY_DOWN ? InputEventType::KeyDown : InputEventType::KeyUp;
        out.window = event.key.windowID;
        out.code = static_cast<int32_t>(event.key.scancode);
        out.value = event.key.repeat ? 1 : 0;
        return true;
    case SDL_EVENT_MOUSE_MOTION:
        out.type = InputEventType::MouseMotion;
        out.window = event.motion.windowID;
        out.x = event.motion.x;
        out.y = event.motion.y;
        out.dx = event.motion.xrel;
        out.dy = event.motion.yrel;
        return true;
    case SDL_EVENT_MOUSE_BUTTON_DOWN:
    case SDL_EVENT_MOUSE_BUTTON_UP:
        out.type =
            event.type == SDL_EVENT_MOUSE_BUTTON_DOWN ? InputEventType::MouseButtonDown : InputEventType::MouseButtonUp;
        out.window = event.button.windowID;
        out.code = event.button.button;
        out.x = event.button.x;
        out.y = event.button.y;
        return true;
    default:
        return false;
    }
}

} // namespace

Result<void> initialize()
{
    if (!SDL_Init(SDL_INIT_VIDEO))
    {
        std::fprintf(stderr, "crucible: SDL_Init failed: %s\n", SDL_GetError());
        return Status::Unsupported;
    }
    return {};
}

void shutdown()
{
    SDL_Quit();
}

Result<WindowHandle> createWindow(uint32_t width, uint32_t height, std::string_view title)
{
    // SDL takes a null-terminated title; a string_view may not be one.
    const std::string terminated(title);
    SDL_Window* window = SDL_CreateWindow(terminated.c_str(), static_cast<int>(width), static_cast<int>(height),
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

void windowPixelSize(WindowHandle window, uint32_t& width, uint32_t& height)
{
    int w = 0;
    int h = 0;
    if (SDL_Window* sdlWindow = find(window))
    {
        SDL_GetWindowSizeInPixels(sdlWindow, &w, &h);
    }
    width = static_cast<uint32_t>(w);
    height = static_cast<uint32_t>(h);
}

uint32_t pollEvents(std::span<InputEvent> events)
{
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

uint64_t timestampNow()
{
    return SDL_GetTicksNS();
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

} // namespace crucible::platform
