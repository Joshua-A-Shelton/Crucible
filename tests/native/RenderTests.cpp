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
#include <cstdint>
#include <vector>

using namespace crucible;
using slag::PresentMode;

TEST_CASE("Mailbox is chosen with three images when the window supports it")
{
    const std::array modes = {PresentMode::Fifo, PresentMode::Immediate, PresentMode::Mailbox};
    const render::PresentChoice choice = render::choosePresentMode(modes);
    CHECK(choice.mode == PresentMode::Mailbox);
    CHECK(choice.minImageCount == 3);
}

TEST_CASE("Without Mailbox Fifo is chosen with two images and never Immediate")
{
    const std::array modes = {PresentMode::Fifo, PresentMode::Immediate};
    const render::PresentChoice choice = render::choosePresentMode(modes);
    CHECK(choice.mode == PresentMode::Fifo);
    CHECK(choice.minImageCount == 2);
    CHECK(render::choosePresentMode({}).mode == PresentMode::Fifo);
}

TEST_CASE("A present mode preference applies only where the window supports it")
{
    const std::array both = {PresentMode::Fifo, PresentMode::Mailbox};
    const std::array fifoOnly = {PresentMode::Fifo};
    CHECK(render::choosePresentMode(both, render::PresentPreference::Fifo).mode == PresentMode::Fifo);
    CHECK(render::choosePresentMode(both, render::PresentPreference::Fifo).minImageCount == 2);
    CHECK(render::choosePresentMode(both, render::PresentPreference::Mailbox).mode == PresentMode::Mailbox);
    CHECK(render::choosePresentMode(fifoOnly, render::PresentPreference::Mailbox).mode == PresentMode::Fifo);
}

TEST_CASE("Surfaces need the renderer and unknown surfaces are reported")
{
    CHECK(render::createSurface(platform::WindowHandle{1}).status() == Status::Unsupported);
    CHECK(render::waitForNextFrame(render::SurfaceHandle{}).status() == Status::NotFound);
    CHECK(render::clearAndPresent(render::SurfaceHandle{3}, 0, 0, 0, 1).status() == Status::NotFound);
    render::destroySurface(render::SurfaceHandle{3}); // ignored
    CHECK(render::setGpuTiming(render::SurfaceHandle{1}, true).status() == Status::NotFound);
    CHECK(render::gpuFrameDoneNs(render::SurfaceHandle{1}, 1) == 0);
    CHECK(render::framesSubmitted(render::SurfaceHandle{1}) == 0);
    CHECK_FALSE(render::pacingWaitsForPresent());
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

namespace {

// Every backend compiled in; a backend with no supported device is skipped
// with a message (Vulkan without VK_EXT_descriptor_heap, for example).
std::vector<slag::Backend> compiledBackends()
{
    std::vector<slag::Backend> backends;
    for (slag::Backend backend : {slag::Backend::D3D12, slag::Backend::Vulkan})
    {
        if (slag::isBackendAvailable(backend))
        {
            backends.push_back(backend);
        }
    }
    return backends;
}

const char* backendName(slag::Backend backend)
{
    return backend == slag::Backend::D3D12 ? "D3D12" : "Vulkan";
}

// One pixel, B8G8R8A8 as read back.
struct Bgra
{
    uint8_t b, g, r, a;
};

// Every pixel within one step of `expected` (hardware may round the sRGB
// encoding either way).
bool allPixelsNear(const std::vector<uint8_t>& pixels, Bgra expected)
{
    auto near = [](uint8_t value, uint8_t want) {
        return value + 1 >= want && value <= want + 1;
    };
    for (size_t i = 0; i < pixels.size(); i += 4)
    {
        if (!near(pixels[i], expected.b) || !near(pixels[i + 1], expected.g) || !near(pixels[i + 2], expected.r) ||
            !near(pixels[i + 3], expected.a))
        {
            MESSAGE("pixel " << i / 4 << ": " << int(pixels[i]) << " " << int(pixels[i + 1]) << " "
                             << int(pixels[i + 2]) << " " << int(pixels[i + 3]));
            return false;
        }
    }
    return true;
}

} // namespace

TEST_SUITE("gpu")
{
    TEST_CASE("Offscreen frames clear to the color and read back sRGB-encoded on every backend")
    {
        REQUIRE(platform::initialize());
        for (slag::Backend backend : compiledBackends())
        {
            CAPTURE(backendName(backend));
            if (!render::initialize(backend))
            {
                MESSAGE("no supported device for " << backendName(backend) << "; skipped");
                continue;
            }

            constexpr uint32_t width = 64;
            constexpr uint32_t height = 32;
            crucible::Result<render::SurfaceHandle> surface = render::createOffscreenSurface(width, height);
            REQUIRE(surface);
            std::vector<uint8_t> pixels(width * height * 4);
            CHECK(render::readSurfacePixels(surface.value(), pixels).status() == Status::NotFound); // no frame yet

            // Linear 0.5 is about 188 once sRGB-encoded; alpha isn't encoded.
            REQUIRE(render::clearAndPresent(surface.value(), 1.0f, 0.0f, 0.5f, 1.0f));
            REQUIRE(render::readSurfacePixels(surface.value(), pixels));
            CHECK(allPixelsNear(pixels, {188, 0, 255, 255}));

            // A later frame replaces it, through both command buffers, paced.
            REQUIRE(render::setGpuTiming(surface.value(), true));
            for (int frame = 0; frame < 5; ++frame)
            {
                REQUIRE(render::waitForNextFrame(surface.value()));
                REQUIRE(render::clearAndPresent(surface.value(), 0.0f, 1.0f, 0.0f, 0.25f));
            }
            REQUIRE(render::readSurfacePixels(surface.value(), pixels));
            CHECK(allPixelsNear(pixels, {0, 255, 0, 64}));
            CHECK(render::framesSubmitted(surface.value()) == 6);
            // The GPU timing thread stamped the finished frames.
            CHECK(render::gpuFrameDoneNs(surface.value(), 6) != 0);

            std::vector<uint8_t> tooSmall(16);
            CHECK(render::readSurfacePixels(surface.value(), tooSmall).status() == Status::InvalidData);

            render::destroySurface(surface.value());
            CHECK(render::readSurfacePixels(surface.value(), pixels).status() == Status::NotFound);
            render::shutdown();
        }
        platform::shutdown();
    }
}

// By hand, on a desktop (skipped otherwise; needs a real video driver):
//     crucible_native_tests --no-skip --test-suite=window
// Clears a real window through a color cycle, resizes it halfway through, and
// prints the present mode and frame times.
TEST_SUITE("window" * doctest::skip())
{
    TEST_CASE("A window clears and presents through a resize")
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
