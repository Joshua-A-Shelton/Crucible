// Platform layer tests (ADR 0018). Run with SDL's offscreen video driver
// (tests/native/CMakeLists.txt), so they need no display: real SDL windows
// that are never shown.

#include <crucible/Platform.h>

#include <SDL3/SDL.h>
#include <doctest/doctest.h>
#include <slag/Swapchain.h>

#include <array>
#include <chrono>
#include <thread>

using namespace crucible::platform;

namespace {

// Initializes the platform for one test case and shuts it down after.
struct PlatformScope
{
    PlatformScope()
    {
        REQUIRE(static_cast<bool>(initialize()));
    }
    ~PlatformScope()
    {
        shutdown();
    }
};

void drain()
{
    std::array<InputEvent, 64> events;
    while (pollEvents(events) == events.size())
    {
    }
}

} // namespace

TEST_CASE("A window can be created, measured, and destroyed")
{
    PlatformScope platform;
    crucible::Result<WindowHandle> window = createWindow(320, 240, "Crucible test");
    REQUIRE(static_cast<bool>(window));
    CHECK(window.value().value != 0);

    const WindowSize size = windowPixelSize(window.value());
    CHECK(size.width == 320);
    CHECK(size.height == 240);
    CHECK(windowPixelSize(WindowHandle{}).width == 0);

    destroyWindow(window.value());
}

TEST_CASE("SDL events come back as Crucible events with their timestamps")
{
    PlatformScope platform;
    drain();

    SDL_Event key{};
    key.type = SDL_EVENT_KEY_DOWN;
    key.key.timestamp = 1234;
    key.key.windowID = 7;
    key.key.scancode = SDL_SCANCODE_SPACE;
    key.key.repeat = true;
    REQUIRE(SDL_PushEvent(&key));

    SDL_Event quit{};
    quit.type = SDL_EVENT_QUIT;
    quit.quit.timestamp = 5678;
    REQUIRE(SDL_PushEvent(&quit));

    std::array<InputEvent, 8> events;
    const uint32_t count = pollEvents(events);
    REQUIRE(count == 2);

    CHECK(events[0].type == InputEventType::KeyDown);
    CHECK(events[0].timestampNs == 1234);
    CHECK(events[0].window.value == 7);
    CHECK(events[0].code == SDL_SCANCODE_SPACE);
    CHECK(events[0].value == 1);

    CHECK(events[1].type == InputEventType::QuitRequested);
    CHECK(events[1].timestampNs == 5678);
}

TEST_CASE("Events that don't fit stay queued for the next poll")
{
    PlatformScope platform;
    drain();

    for (int i = 0; i < 3; ++i)
    {
        SDL_Event quit{};
        quit.type = SDL_EVENT_QUIT;
        REQUIRE(SDL_PushEvent(&quit));
    }

    std::array<InputEvent, 2> small;
    CHECK(pollEvents(small) == 2);
    CHECK(pollEvents(small) == 1);
    CHECK(pollEvents(small) == 0);
}

TEST_CASE("The offscreen driver has no native window Slag could present to")
{
    PlatformScope platform;
    crucible::Result<WindowHandle> window = createWindow(64, 64, "offscreen");
    REQUIRE(static_cast<bool>(window));

    slag::NativeWindow native;
    CHECK(nativeWindow(window.value(), native).status() == crucible::Status::Unsupported);
    CHECK(nativeWindow(WindowHandle{}, native).status() == crucible::Status::NotFound);

    destroyWindow(window.value());
}

TEST_CASE("The event clock moves forward")
{
    PlatformScope platform;
    const uint64_t first = timestampNow();
    SDL_DelayNS(1'000'000);
    CHECK(timestampNow() > first);
}

TEST_CASE("Latency markers arrive numbered, with queue timestamps")
{
    PlatformScope platform;
    drain();

    const uint64_t before = timestampNow();
    REQUIRE(static_cast<bool>(startLatencyMarkers(2'000)));                      // every 1-3 ms
    CHECK(startLatencyMarkers(2'000).status() == crucible::Status::Unsupported); // already running
    std::this_thread::sleep_for(std::chrono::milliseconds(50));
    stopLatencyMarkers();
    const uint64_t after = timestampNow();

    std::array<InputEvent, 128> events;
    const uint32_t count = pollEvents(events);
    REQUIRE(count >= 5);
    for (uint32_t i = 0; i < count; ++i)
    {
        CHECK(events[i].type == InputEventType::LatencyMarker);
        CHECK(events[i].code == static_cast<int32_t>(i));
        CHECK(events[i].timestampNs >= before);
        CHECK(events[i].timestampNs <= after);
    }
    stopLatencyMarkers(); // stopping twice is harmless
}
