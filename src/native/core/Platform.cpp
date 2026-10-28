#include <crucible/Platform.h>
#include <crucible/SmallVector.h>

#include <SDL3/SDL.h>
#include <slag/Swapchain.h>

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
        return true;
    case SDL_EVENT_MOUSE_MOTION:
        out.type = InputEventType::MouseMotion;
        out.window.value = event.motion.windowID;
        out.x = event.motion.x;
        out.y = event.motion.y;
        out.dx = event.motion.xrel;
        out.dy = event.motion.yrel;
        return true;
    case SDL_EVENT_MOUSE_BUTTON_DOWN:
    case SDL_EVENT_MOUSE_BUTTON_UP:
        out.type =
            event.type == SDL_EVENT_MOUSE_BUTTON_DOWN ? InputEventType::MouseButtonDown : InputEventType::MouseButtonUp;
        out.window.value = event.button.windowID;
        out.code = event.button.button;
        out.x = event.button.x;
        out.y = event.button.y;
        return true;
    default:
        if (markers.eventType != 0 && event.type == markers.eventType)
        {
            out.type = InputEventType::LatencyMarker;
            out.code = event.user.code;
            return true;
        }
        return false;
    }
}

} // namespace

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
    return {};
}

void shutdown()
{
    stopLatencyMarkers();
    markers.eventType = 0;
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
