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
    CHECK(render::renderFrame(render::SurfaceHandle{3}, nullptr, 0, 0, 0, 1).status() == Status::NotFound);
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

bool near(Bgra pixel, Bgra expected)
{
    auto close = [](uint8_t value, uint8_t want) {
        return value + 1 >= want && value <= want + 1;
    };
    return close(pixel.b, expected.b) && close(pixel.g, expected.g) && close(pixel.r, expected.r) &&
           close(pixel.a, expected.a);
}

Bgra pixelAt(const std::vector<uint8_t>& pixels, uint32_t width, uint32_t x, uint32_t y)
{
    const size_t at = (size_t{y} * width + x) * 4;
    return {pixels[at], pixels[at + 1], pixels[at + 2], pixels[at + 3]};
}

// The default render steps' frame (ADR 0048): the clear color in the corners
// (within one step: hardware may round the sRGB encoding either way), and the
// Triangle step's triangle, which a winding mistake would cull: red near its
// top corner, green near its bottom right, blue near its bottom left.
void checkFrame(const std::vector<uint8_t>& pixels, uint32_t width, uint32_t height, Bgra clear)
{
    for (auto [x, y] : {std::pair{0u, 0u}, {width - 1, 0u}, {0u, height - 1}, {width - 1, height - 1}})
    {
        const Bgra corner = pixelAt(pixels, width, x, y);
        CAPTURE(x);
        CAPTURE(y);
        CHECK(near(corner, clear));
    }
    // The triangle's corners are at (0, 0.6), (0.55, -0.45), (-0.55, -0.45)
    // in clip space, y up; pixel rows go down.
    auto pixelOf = [&](float clipX, float clipY) {
        return pixelAt(pixels, width, static_cast<uint32_t>((clipX + 1) / 2 * width),
                       static_cast<uint32_t>((1 - clipY) / 2 * height));
    };
    const Bgra top = pixelOf(0.0f, 0.45f), right = pixelOf(0.4f, -0.38f), left = pixelOf(-0.4f, -0.38f);
    CHECK(top.r > top.g);
    CHECK(top.r > top.b);
    CHECK(right.g > right.r);
    CHECK(right.g > right.b);
    CHECK(left.b > left.r);
    CHECK(left.b > left.g);
    CHECK(top.a == 255);
}

} // namespace

TEST_SUITE("gpu")
{
    TEST_CASE("Offscreen frames render the steps and read back sRGB-encoded on every backend")
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
            REQUIRE(render::renderFrame(surface.value(), nullptr, 1.0f, 0.0f, 0.5f, 1.0f));
            REQUIRE(render::readSurfacePixels(surface.value(), pixels));
            checkFrame(pixels, width, height, {188, 0, 255, 255});

            // A later frame replaces it, through both frames in flight, paced.
            REQUIRE(render::setGpuTiming(surface.value(), true));
            for (int frame = 0; frame < 5; ++frame)
            {
                REQUIRE(render::waitForNextFrame(surface.value()));
                REQUIRE(render::renderFrame(surface.value(), nullptr, 0.0f, 1.0f, 0.0f, 0.25f));
            }
            REQUIRE(render::readSurfacePixels(surface.value(), pixels));
            checkFrame(pixels, width, height, {0, 255, 0, 64});
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
            REQUIRE(render::renderFrame(surface.value(), nullptr, t, 0.2f, 1.0f - t, 1.0f));
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
