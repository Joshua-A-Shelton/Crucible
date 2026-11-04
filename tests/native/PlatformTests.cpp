// Platform layer tests (ADR 0018). Run with SDL's offscreen video driver
// (tests/native/CMakeLists.txt), so they need no display: real SDL windows
// that are never shown.

#include "../../src/native/core/PlatformInternal.h"

#include <crucible/Platform.h>

#include <SDL3/SDL.h>
#include <doctest/doctest.h>
#include <slag/Swapchain.h>

#include <array>
#include <chrono>
#include <cstdio>
#include <cstdlib>
#include <string_view>
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

TEST_CASE("A window can be created and measured and destroyed")
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

TEST_CASE("Wheels modifiers gamepads and display scales come back as Crucible events")
{
    PlatformScope platform;
    drain();

    SDL_Event key{};
    key.type = SDL_EVENT_KEY_DOWN;
    key.key.scancode = SDL_SCANCODE_TAB;
    key.key.mod = SDL_KMOD_LSHIFT | SDL_KMOD_RCTRL;
    REQUIRE(SDL_PushEvent(&key));
    SDL_Event wheel{};
    wheel.type = SDL_EVENT_MOUSE_WHEEL;
    wheel.wheel.windowID = 3;
    wheel.wheel.y = 2;
    wheel.wheel.direction = SDL_MOUSEWHEEL_FLIPPED;
    wheel.wheel.mouse_x = 10;
    wheel.wheel.mouse_y = 20;
    REQUIRE(SDL_PushEvent(&wheel));
    SDL_Event button{};
    button.type = SDL_EVENT_GAMEPAD_BUTTON_DOWN;
    button.gbutton.which = 42;
    button.gbutton.button = SDL_GAMEPAD_BUTTON_EAST;
    REQUIRE(SDL_PushEvent(&button));
    SDL_Event axis{};
    axis.type = SDL_EVENT_GAMEPAD_AXIS_MOTION;
    axis.gaxis.which = 42;
    axis.gaxis.axis = SDL_GAMEPAD_AXIS_LEFTY;
    axis.gaxis.value = -32768;
    REQUIRE(SDL_PushEvent(&axis));
    SDL_Event paddle{};
    paddle.type = SDL_EVENT_GAMEPAD_BUTTON_DOWN;
    paddle.gbutton.button = SDL_GAMEPAD_BUTTON_RIGHT_PADDLE1; // not mapped: dropped
    REQUIRE(SDL_PushEvent(&paddle));
    SDL_Event added{};
    added.type = SDL_EVENT_GAMEPAD_ADDED;
    added.gdevice.which = 99; // no such pad: opening it fails quietly
    REQUIRE(SDL_PushEvent(&added));

    std::array<InputEvent, 8> events;
    REQUIRE(pollEvents(events) == 5);
    CHECK(events[0].modifiers ==
          (static_cast<uint32_t>(KeyModifiers::Shift) | static_cast<uint32_t>(KeyModifiers::Ctrl)));
    CHECK(events[1].type == InputEventType::MouseWheel);
    CHECK(events[1].window.value == 3);
    CHECK(events[1].dy == -2); // flipped back
    CHECK(events[1].x == 10);
    CHECK(events[2].type == InputEventType::GamepadButtonDown);
    CHECK(events[2].code == static_cast<int32_t>(GamepadButton::East));
    CHECK(events[2].value == 42);
    CHECK(events[3].type == InputEventType::GamepadAxis);
    CHECK(events[3].code == static_cast<int32_t>(GamepadAxis::LeftY));
    CHECK(events[3].x == -1);
    CHECK(events[4].type == InputEventType::GamepadAdded);
    CHECK(events[4].value == 99);

    // SDL's numbering is Crucible's.
    static_assert(static_cast<int>(GamepadButton::DpadRight) == SDL_GAMEPAD_BUTTON_DPAD_RIGHT);
    static_assert(static_cast<int>(GamepadAxis::RightTrigger) == SDL_GAMEPAD_AXIS_RIGHT_TRIGGER);
    CHECK(windowDisplayScale({}) == 1);
}

TEST_CASE("Typed text comes back as TextInput events pointing into the polled text")
{
    PlatformScope platform;
    drain();

    // SDL owns an event's text; these literals outlive the poll.
    SDL_Event first{};
    first.type = SDL_EVENT_TEXT_INPUT;
    first.text.windowID = 5;
    first.text.text = "h\xC3\xA9";
    REQUIRE(SDL_PushEvent(&first));
    SDL_Event second{};
    second.type = SDL_EVENT_TEXT_INPUT;
    second.text.windowID = 5;
    second.text.text = "llo";
    REQUIRE(SDL_PushEvent(&second));

    std::array<InputEvent, 8> events;
    REQUIRE(pollEvents(events) == 2);
    CHECK(events[0].type == InputEventType::TextInput);
    CHECK(events[0].window.value == 5);
    CHECK(events[0].code == 0);
    CHECK(events[0].value == 3); // é is two bytes
    CHECK(events[1].code == 3);
    CHECK(events[1].value == 3);

    std::array<uint8_t, 16> text{};
    REQUIRE(polledText(text) == 6);
    CHECK(std::string_view(reinterpret_cast<const char*>(text.data()), 6) == "h\xC3\xA9llo");
    // A short buffer gets what fits; the length is still the whole.
    std::array<uint8_t, 2> part{};
    CHECK(polledText(part) == 6);
    CHECK(part[0] == 'h');

    // The next poll starts afresh.
    CHECK(pollEvents(events) == 0);
    CHECK(polledText(text) == 0);
}

TEST_CASE("Text input turns on and off for a window and the clipboard holds text")
{
    PlatformScope platform;
    crucible::Result<WindowHandle> window = createWindow(64, 64, "Crucible text input");
    REQUIRE(static_cast<bool>(window));
    SDL_Window* sdlWindow = SDL_GetWindowFromID(window.value().value);
    REQUIRE(sdlWindow != nullptr);

    startTextInput(window.value());
    CHECK(SDL_TextInputActive(sdlWindow));
    stopTextInput(window.value());
    CHECK_FALSE(SDL_TextInputActive(sdlWindow));
    startTextInput(WindowHandle{}); // no window: nothing happens

    // The offscreen driver keeps SDL's own clipboard.
    REQUIRE(setClipboardText("copied \xC3\xA9"));
    CHECK(clipboardText() == "copied \xC3\xA9");
    REQUIRE(setClipboardText(""));
    CHECK(clipboardText().empty());

    destroyWindow(window.value());
}

TEST_CASE("Keys carry their keycodes and pointer positions are pixels")
{
    PlatformScope platform;
    drain();
    SDL_Event key{};
    key.type = SDL_EVENT_KEY_DOWN;
    key.key.scancode = SDL_SCANCODE_W;
    key.key.key = SDLK_Z; // AZERTY's Z, where QWERTY has W
    REQUIRE(SDL_PushEvent(&key));
    std::array<InputEvent, 4> events;
    REQUIRE(pollEvents(events) == 1);
    CHECK(events[0].code == SDL_SCANCODE_W);
    CHECK(events[0].keycode == 'z');
}

TEST_CASE("Window drags need a platform that has them")
{
    PlatformScope platform;
    // The offscreen driver has no desktop to drag across.
    CHECK(windowDragSupport() == WindowDragSupport::None);
    crucible::Result<WindowHandle> window = createWindow(64, 64, "Crucible drags");
    REQUIRE(static_cast<bool>(window));
    CHECK_FALSE(startWindowDrag(window.value(), {}, {}));
    CHECK(windowPosition(window.value()).x == 0);
    CHECK(pointerScreenPosition().x == 0);
    stopWindowDrag(); // nothing to stop
    destroyWindow(window.value());
}

TEST_CASE("Drag events come back in order with their positions")
{
    PlatformScope platform;
    drain();
    pushDragEvent(InputEventType::DragMoved, 4, 12.5f, -3.0f);
    pushDragEvent(InputEventType::DragMoved, 0, 0.0f, 0.0f);
    pushDragEvent(InputEventType::DragDropped, 9, 1.0f, 2.0f);
    pushDragEvent(InputEventType::DragCanceled, 0, 0.0f, 0.0f);
    std::array<InputEvent, 8> events;
    REQUIRE(pollEvents(events) == 4);
    CHECK(events[0].type == InputEventType::DragMoved);
    CHECK(events[0].window.value == 4);
    CHECK(events[0].x == 12.5f);
    CHECK(events[0].y == -3.0f);
    CHECK(events[1].window.value == 0);
    CHECK(events[2].type == InputEventType::DragDropped);
    CHECK(events[2].window.value == 9);
    CHECK(events[2].y == 2.0f);
    CHECK(events[3].type == InputEventType::DragCanceled);
}

// By hand, on a Wayland desktop (CRUCIBLE_TEST_WAYLAND=1, and SDL's own
// driver choice): the platform's connection binds what the compositor has.
// No window is opened.
TEST_CASE("Wayland window drags are found on a Wayland desktop")
{
    const char* wanted = std::getenv("CRUCIBLE_TEST_WAYLAND");
    if (wanted == nullptr || std::string_view(wanted) != "1")
    {
        return;
    }
    SDL_SetHint(SDL_HINT_VIDEO_DRIVER, "wayland");
    PlatformScope platform;
    const WindowDragSupport support = windowDragSupport();
    const char* names[] = {"None", "DropOnly", "Full"};
    std::printf("window drag support: %s\n", names[static_cast<int>(support)]);
    CHECK(support != WindowDragSupport::None);
    SDL_ResetHint(SDL_HINT_VIDEO_DRIVER);
}

TEST_CASE("The platform starts gamepad support")
{
    // Synthetic gamepad events reach the UI without it; real pads need SDL
    // built with joystick support and the subsystem started (ADR 0062).
    PlatformScope platform;
    CHECK((SDL_WasInit(SDL_INIT_GAMEPAD) & SDL_INIT_GAMEPAD) != 0);
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

TEST_CASE("Latency markers arrive numbered with queue timestamps")
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

TEST_CASE("Event timestamps are arrival times except under X11")
{
    PlatformScope platform;
    // The tests run on the offscreen driver; only x11 reports false.
    CHECK(eventTimestampsAreArrivals());
    const char* rawKeyboard = SDL_GetHint(SDL_HINT_WINDOWS_RAW_KEYBOARD);
    REQUIRE(rawKeyboard != nullptr);
    CHECK(std::string_view(rawKeyboard) == "1");
}
