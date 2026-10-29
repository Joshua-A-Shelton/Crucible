// How accurate SDL's event timestamps are (ADR 0018, settled at the end of
// M2). Real OS input is injected at known times (uinput on Linux, SendInput
// on Windows), so it travels the whole input stack, and each injected input
// is paired with the SDL event it becomes.
//
// By hand, on a desktop:
//     crucible_native_tests --no-skip --test-suite=input-timing
// The window may need a click to get focus (a compositor can refuse focus to
// a background process). Then it captures the mouse for about 3 seconds,
// moving it a pixel back and forth, and presses Left Shift 100 times, which
// types nothing. Set SDL_VIDEO_DRIVER=x11 to measure X11 (XWayland), and on
// Windows SDL_WINDOWS_RAW_KEYBOARD=1 to measure SDL's raw keyboard path.

#include <crucible/Platform.h>
#include <crucible/Render.h>

#include <SDL3/SDL.h>
#include <doctest/doctest.h>
#include <slag/Instance.h>

#include <algorithm>
#include <array>
#include <chrono>
#include <cstdint>
#include <cstring>
#include <functional>
#include <string>
#include <thread>
#include <vector>

#if defined(_WIN32)
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#elif defined(__linux__)
#include <fcntl.h>
#include <linux/uinput.h>
#include <sys/ioctl.h>
#include <unistd.h>
#endif

using namespace crucible::platform;

namespace {

// Moves the mouse and presses Left Shift through the OS, as real devices do.
class InputInjector
{
public:
#if defined(_WIN32)
    bool ready() const
    {
        return true;
    }

    void move(int dx)
    {
        INPUT input{};
        input.type = INPUT_MOUSE;
        input.mi.dx = dx;
        input.mi.dwFlags = MOUSEEVENTF_MOVE;
        SendInput(1, &input, sizeof(input));
    }

    void shift(bool down)
    {
        INPUT input{};
        input.type = INPUT_KEYBOARD;
        input.ki.wVk = VK_LSHIFT;
        input.ki.wScan = static_cast<WORD>(MapVirtualKeyW(VK_LSHIFT, MAPVK_VK_TO_VSC));
        input.ki.dwFlags = down ? 0 : KEYEVENTF_KEYUP;
        SendInput(1, &input, sizeof(input));
    }
#elif defined(__linux__)
    InputInjector()
    {
        fd = open("/dev/uinput", O_WRONLY | O_NONBLOCK);
        if (fd < 0)
        {
            return;
        }
        // libinput only treats a device as a mouse if it also has a button.
        ioctl(fd, UI_SET_EVBIT, EV_KEY);
        ioctl(fd, UI_SET_KEYBIT, BTN_LEFT);
        ioctl(fd, UI_SET_KEYBIT, KEY_LEFTSHIFT);
        ioctl(fd, UI_SET_EVBIT, EV_REL);
        ioctl(fd, UI_SET_RELBIT, REL_X);
        ioctl(fd, UI_SET_RELBIT, REL_Y);
        uinput_setup setup{};
        setup.id.bustype = BUS_USB;
        setup.id.vendor = 0x1209; // pid.codes' test vendor
        setup.id.product = 0x0001;
        std::strncpy(setup.name, "Crucible input timing", UINPUT_MAX_NAME_SIZE - 1);
        if (ioctl(fd, UI_DEV_SETUP, &setup) != 0 || ioctl(fd, UI_DEV_CREATE) != 0)
        {
            close(fd);
            fd = -1;
            return;
        }
        // The compositor needs a moment to notice the new device.
        std::this_thread::sleep_for(std::chrono::milliseconds(1000));
    }

    ~InputInjector()
    {
        if (fd >= 0)
        {
            ioctl(fd, UI_DEV_DESTROY);
            close(fd);
        }
    }

    bool ready() const
    {
        return fd >= 0;
    }

    void move(int dx)
    {
        emit(EV_REL, REL_X, dx);
    }

    void shift(bool down)
    {
        emit(EV_KEY, KEY_LEFTSHIFT, down ? 1 : 0);
    }

private:
    void emit(uint16_t type, uint16_t code, int32_t value)
    {
        const input_event events[] = {{{}, type, code, value}, {{}, EV_SYN, SYN_REPORT, 0}};
        (void)write(fd, events, sizeof(events));
    }

    int fd = -1;
#else
    bool ready() const
    {
        return false;
    }

    void move(int) {}

    void shift(bool) {}
#endif
};

struct Sample
{
    uint64_t sentNs;
    uint64_t stampNs;
    uint64_t seenNs;
};

double percentile(std::vector<double> values, double fraction)
{
    std::sort(values.begin(), values.end());
    const size_t rank = static_cast<size_t>(fraction * static_cast<double>(values.size()) + 0.999999);
    return values[std::clamp<size_t>(rank, 1, values.size()) - 1];
}

void report(const std::string& name, const std::vector<double>& values)
{
    MESSAGE(name << " ms: median " << percentile(values, 0.5) << ", p5 " << percentile(values, 0.05) << ", p95 "
                 << percentile(values, 0.95) << ", max " << percentile(values, 1.0));
}

// The window under test: rendered throughout, since a Wayland window is only
// shown once something is presented to it (not part of the measurement).
struct TestWindow
{
    SDL_Window* sdl = nullptr;
    crucible::render::SurfaceHandle surface{};
    std::array<InputEvent, 64> events{};

    void present()
    {
        (void)crucible::render::renderFrame(surface, nullptr, 0.05f, 0.1f, 0.2f, 1.0f);
    }

    bool focused() const
    {
        return (SDL_GetWindowFlags(sdl) & SDL_WINDOW_INPUT_FOCUS) != 0;
    }

    void drain()
    {
        while (pollEvents(events) == events.size())
        {
        }
    }

    // Spends `ns` polling and presenting.
    void idle(uint64_t ns)
    {
        const uint64_t until = timestampNow() + ns;
        while (timestampNow() < until)
        {
            (void)pollEvents(events);
            present();
        }
    }
};

// Injects `count` inputs and pairs each with the first event of `type` that
// follows it, waiting up to 100 ms. Stops early if the window loses focus.
std::vector<Sample> measure(TestWindow& test, int count, InputEventType type, const std::function<void(int)>& inject,
                            const std::function<void(int)>& after)
{
    std::vector<Sample> samples;
    for (int i = 0; i < count && test.focused(); ++i)
    {
        test.drain();
        const uint64_t sent = timestampNow();
        inject(i);
        // Poll late, as a game polls once a frame: a timestamp of when the
        // input arrived stays near 0 after sent, one of when SDL processed
        // it moves to about this delay.
        std::this_thread::sleep_for(std::chrono::milliseconds(8));
        bool found = false;
        while (!found && timestampNow() - sent < 100'000'000)
        {
            const uint32_t polled = pollEvents(test.events);
            const uint64_t seen = timestampNow();
            for (uint32_t e = 0; e < polled && !found; ++e)
            {
                if (test.events[e].type == type)
                {
                    samples.push_back({sent, test.events[e].timestampNs, seen});
                    found = true;
                }
            }
        }
        after(i);
        test.present();
        std::this_thread::sleep_for(std::chrono::milliseconds(15));
    }
    return samples;
}

void summarize(const std::string& name, const std::vector<Sample>& samples, int count)
{
    MESSAGE(name << ": " << samples.size() << " of " << count << " arrived");
    if (samples.empty())
    {
        return;
    }
    std::vector<double> stampMinusSent;
    std::vector<double> seenMinusSent;
    bool wholeMilliseconds = true;
    for (const Sample& sample : samples)
    {
        stampMinusSent.push_back((static_cast<double>(sample.stampNs) - static_cast<double>(sample.sentNs)) / 1e6);
        seenMinusSent.push_back(static_cast<double>(sample.seenNs - sample.sentNs) / 1e6);
        // Timestamps converted from millisecond clocks keep a fixed
        // sub-millisecond remainder.
        wholeMilliseconds = wholeMilliseconds && (sample.stampNs - samples[0].stampNs) % 1'000'000 == 0;
    }
    report(name + " timestamp - sent", stampMinusSent);
    report(name + " seen - sent", seenMinusSent);
    MESSAGE((name + (wholeMilliseconds ? ": timestamps are" : ": timestamps are not") +
             " quantized to whole milliseconds"));
}

} // namespace

TEST_SUITE("input-timing" * doctest::skip())
{
    TEST_CASE("SDL timestamps of injected OS mouse motion and key presses")
    {
        InputInjector injector;
        REQUIRE(injector.ready());
        REQUIRE(static_cast<bool>(initialize()));
        MESSAGE("video driver: " << std::string(SDL_GetCurrentVideoDriver()));
        crucible::Result<WindowHandle> window =
            createWindow(640, 360, "Crucible input timing: click here to start (30 s)");
        REQUIRE(window);
        REQUIRE(static_cast<bool>(crucible::render::initialize(slag::InstanceDesc{}.backend)));
        crucible::Result<crucible::render::SurfaceHandle> surface = crucible::render::createSurface(window.value());
        REQUIRE(surface);

        TestWindow test;
        test.sdl = SDL_GetWindowFromID(window.value().value);
        test.surface = surface.value();
        SDL_RaiseWindow(test.sdl);

        // Wait up to 30 s for focus (a click may be needed), then let any
        // click finish before measuring.
        const uint64_t focusDeadline = timestampNow() + 30'000'000'000;
        while (!test.focused() && timestampNow() < focusDeadline)
        {
            test.idle(10'000'000);
        }
        REQUIRE(test.focused());
        test.idle(500'000'000);
        SDL_SetWindowTitle(test.sdl, "Crucible input timing: measuring");

        // Mouse: relative mode captures the pointer, so motion reaches the
        // window; one pixel back and forth, so the cursor doesn't drift.
        REQUIRE(SDL_SetWindowRelativeMouseMode(test.sdl, true));
        test.idle(200'000'000);
        constexpr int motionCount = 150;
        std::vector<Sample> motion = measure(
            test, motionCount, InputEventType::MouseMotion, [&](int i) { injector.move(i % 2 == 0 ? 1 : -1); },
            [](int) {});
        SDL_SetWindowRelativeMouseMode(test.sdl, false);

        // Keyboard: Left Shift, which types nothing. The release is drained
        // with the next sample.
        constexpr int keyCount = 100;
        std::vector<Sample> keys = measure(
            test, keyCount, InputEventType::KeyDown, [&](int) { injector.shift(true); },
            [&](int) { injector.shift(false); });
        injector.shift(false); // never leave Shift held

        crucible::render::destroySurface(surface.value());
        crucible::render::shutdown();
        destroyWindow(window.value());
        shutdown();

        summarize("mouse motion", motion, motionCount);
        summarize("key press", keys, keyCount);
        CHECK(motion.size() >= motionCount / 2);
        CHECK(keys.size() >= keyCount / 2);
    }
}
