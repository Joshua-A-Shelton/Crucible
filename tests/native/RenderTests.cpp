// Renderer foundation tests (ADR 0003, 0015). The "gpu" suite needs a device
// Slag supports and becomes a CTest label (tests/native/CMakeLists.txt), so
// machines without one can run `ctest -LE gpu`. Windows and presenting are
// checked by hand: the offscreen video driver has no window Slag can present
// to.

#include <crucible/Render.h>

#include <SDL3/SDL.h>
#include <doctest/doctest.h>
#include <slag/Instance.h>
#include <slag/Swapchain.h>

#include <algorithm>
#include <array>

using namespace crucible;
using slag::PresentMode;

TEST_CASE("Mailbox is chosen with three images when the window supports it")
{
    const std::array modes = {PresentMode::Fifo, PresentMode::Immediate, PresentMode::Mailbox};
    const render::PresentChoice choice = render::choosePresentMode(modes);
    CHECK(choice.mode == PresentMode::Mailbox);
    CHECK(choice.minImageCount == 3);
}

TEST_CASE("Without Mailbox, Fifo is chosen with two images, never Immediate")
{
    const std::array modes = {PresentMode::Fifo, PresentMode::Immediate};
    const render::PresentChoice choice = render::choosePresentMode(modes);
    CHECK(choice.mode == PresentMode::Fifo);
    CHECK(choice.minImageCount == 2);
    CHECK(render::choosePresentMode({}).mode == PresentMode::Fifo);
}

TEST_CASE("Surfaces need the renderer, and unknown surfaces are reported")
{
    CHECK(render::createSurface(platform::WindowHandle{1}).status() == Status::Unsupported);
    CHECK(render::waitForNextFrame(render::SurfaceHandle{}).status() == Status::NotFound);
    CHECK(render::clearAndPresent(render::SurfaceHandle{3}, 0, 0, 0, 1).status() == Status::NotFound);
    render::destroySurface(render::SurfaceHandle{3}); // ignored
}

TEST_SUITE("gpu")
{
    TEST_CASE("The renderer starts and stops on the default backend")
    {
        REQUIRE(platform::initialize());
        REQUIRE(render::initialize(slag::InstanceDesc{}.backend));

        // The offscreen driver's windows have no native handle to present to.
        crucible::Result<platform::WindowHandle> window = platform::createWindow(64, 64, "render test");
        REQUIRE(window);
        CHECK(render::createSurface(window.value()).status() == Status::Unsupported);
        platform::destroyWindow(window.value());

        render::shutdown();
        platform::shutdown();

        // Shutting down twice is harmless, and the renderer can start again.
        render::shutdown();
        REQUIRE(render::initialize(slag::InstanceDesc{}.backend));
        render::shutdown();
    }
}

// By hand, on a desktop (skipped otherwise; needs a real video driver):
//     crucible_native_tests --no-skip --test-suite=window
// Clears a real window through a color cycle, resizes it halfway through, and
// prints the present mode and frame times.
TEST_SUITE("window" * doctest::skip())
{
    TEST_CASE("A window clears and presents, through a resize")
    {
        REQUIRE(platform::initialize());
        REQUIRE(render::initialize(slag::InstanceDesc{}.backend));
        crucible::Result<platform::WindowHandle> window = platform::createWindow(640, 360, "Crucible render test");
        REQUIRE(window);
        crucible::Result<render::SurfaceHandle> surface = render::createSurface(window.value());
        REQUIRE(surface);

        const uint32_t firstWidth = platform::windowPixelSize(window.value()).width;
        constexpr uint32_t frames = 240;
        std::array<platform::InputEvent, 64> events;
        uint64_t start = platform::timestampNow();
        uint64_t longest = 0;
        for (uint32_t frame = 0; frame < frames; ++frame)
        {
            const uint64_t frameStart = platform::timestampNow();
            REQUIRE(render::waitForNextFrame(surface.value()));
            (void)platform::pollEvents(events);
            if (frame == frames / 2)
            {
                SDL_SetWindowSize(SDL_GetWindowFromID(window.value().value), 800, 450);
            }
            const float t = static_cast<float>(frame) / frames;
            REQUIRE(render::clearAndPresent(surface.value(), t, 0.2f, 1.0f - t, 1.0f));
            longest = std::max(longest, platform::timestampNow() - frameStart);
        }
        const double seconds = static_cast<double>(platform::timestampNow() - start) / 1e9;
        MESSAGE(frames << " frames in " << seconds << " s (" << frames / seconds << " fps), longest frame "
                       << static_cast<double>(longest) / 1e6 << " ms");
        // Pixel sizes scale with the display, so only check that it changed.
        CHECK(platform::windowPixelSize(window.value()).width != firstWidth);

        render::destroySurface(surface.value());
        platform::destroyWindow(window.value());
        render::shutdown();
        platform::shutdown();
    }
}
