// The rest of the scene (ADR 0055): the gradient sky and its sun, sorted
// transparency, and alpha-to-coverage, drawn on every backend.

#include "RenderInternal.h"
#include "TestModels.h"

#include <crucible/Camera.h>
#include <crucible/Ecs.h>
#include <crucible/Lights.h>
#include <crucible/MeshInstance.h>
#include <crucible/Platform.h>
#include <crucible/Render.h>
#include <crucible/Sky.h>
#include <crucible/Transform.h>

#include <doctest/doctest.h>
#include <slag/Slag.h>

#include <cmath>
#include <vector>

using namespace crucible;
using crucible::render::Camera;
using crucible::render::CameraProjection;
using crucible::render::Sky;
using crucible::scenes::LocalTransform;
using crucible::scenes::WorldTransform;
using crucible::test::Bgra;

namespace {

ecs::Entity addCamera(test::Scene& scene)
{
    const ecs::ComponentId set[] = {ecs::componentIdOf<LocalTransform>, ecs::componentIdOf<WorldTransform>,
                                    ecs::componentIdOf<Camera>};
    const ecs::Entity camera = ecs::createEntity(*scene.world, set);
    *ecs::get<LocalTransform>(*scene.world, camera) = {{0, 0, -10}, {0, 0, 0, 1}, {1, 1, 1}};
    *ecs::get<Camera>(*scene.world,
                      camera) = {.projection = CameraProjection::Perspective, .fieldOfViewY = 0.9f, .nearPlane = 0.1f};
    return camera;
}

ecs::Entity addSky(test::Scene& scene, float sunSize)
{
    const ecs::ComponentId set[] = {ecs::componentIdOf<Sky>};
    const ecs::Entity sky = ecs::createEntity(*scene.world, set);
    *ecs::get<Sky>(*scene.world, sky) = {{0, 0, 1}, {0.5f, 0.5f, 0.5f}, {1, 0, 0}, 1, sunSize, 4};
    return sky;
}

int difference(Bgra a, Bgra b)
{
    return std::abs(a.r - b.r) + std::abs(a.g - b.g) + std::abs(a.b - b.b);
}

const Bgra& at(const std::vector<uint8_t>& pixels, uint32_t width, uint32_t x, uint32_t y)
{
    return *reinterpret_cast<const Bgra*>(pixels.data() + (size_t{y} * width + x) * 4);
}

} // namespace

TEST_SUITE("gpu")
{
    TEST_CASE("The sky and transparency and alpha-to-coverage draw as they should on every backend")
    {
        scenes::registerTransforms();
        scenes::registerMeshInstance();
        render::registerCamera();
        render::registerLights();
        const std::vector<uint8_t> shaderBytes = test::readFile(CRUCIBLE_SHADER_DIR "/TestMaterials.crshader");
        const test::TypeLayout layout = test::typeNamed(shaderBytes, "Lit");
        test::library[7] = shaderBytes;
        const assets::AssetId shader = test::idOf(7);
        // Lit quads in a color (models 40 + i, materials 20 + i): blended
        // below alpha 1. Lit under a white ambient of 1, a surface is its
        // color.
        uint8_t next = 0;
        auto coloredQuad = [&](float r, float g, float b, float a) {
            const uint8_t i = next++;
            std::vector<uint8_t> parameters = layout.defaults;
            const float color[4] = {r, g, b, a};
            std::memcpy(parameters.data() + layout.offsetOf("color"), color, sizeof(color));
            test::library[20 + i] = test::materialFile(shader, layout.index, parameters,
                                                       a < 1 ? assets::AlphaMode::Blend : assets::AlphaMode::Opaque);
            const assets::AssetId material = test::idOf(20 + i);
            test::library[40 + i] = test::quadModel({.remap = &material});
            return test::idOf(40 + i);
        };
        const assets::AssetId red = coloredQuad(1, 0, 0, 0.9f), green = coloredQuad(0, 1, 0, 0.9f),
                              blue = coloredQuad(0, 0, 1, 0.9f), clear = coloredQuad(0, 0, 1, 0.5f),
                              white = coloredQuad(1, 1, 1, 1);
        // Alpha-tested: the left of the quad is clear, the right opaque.
        const assets::AssetId halfClear = test::idOf(4), masked = test::idOf(5);
        test::library[4] = test::rowTexture({255, 255, 255, 0, 255, 255, 255, 255});
        test::library[5] = test::quadModel({.texture = &halfClear, .alphaMode = assets::AlphaMode::Mask});
        render::setAssetReader(reinterpret_cast<uint64_t>(&test::readTestAsset));
        REQUIRE(platform::initialize());
        for (slag::Backend backend : {slag::Backend::D3D12, slag::Backend::Vulkan})
        {
            if (!slag::isBackendAvailable(backend) || !render::initialize(backend))
            {
                continue;
            }
            CAPTURE(static_cast<int>(backend));
            constexpr uint32_t width = 256, height = 128;
            Result<render::SurfaceHandle> surface = render::createOffscreenSurface(width, height);
            REQUIRE(surface);

            SUBCASE("The sky's gradient and sun fill what nothing else covers")
            {
                test::Scene scene;
                addCamera(scene);
                scene.ambient({1, 1, 1}, {1, 1, 1});
                scene.quad(white, {0, 0, 0}, 1);
                // The camera looks along +Z: the sun up and to the right.
                const Vector3 toSun = normalize(Vector3{0.3f, 0.2f, 1});
                scene.sun(toSun, {1, 1, 1}, 1);
                scene.render(surface.value());
                std::vector<uint8_t> pixels = test::readPixels(surface.value(), width, height);
                // No sky: the clear color (black).
                CHECK(at(pixels, width, 5, 5).b < 3);

                addSky(scene, 0.1f);
                scene.render(surface.value());
                pixels = test::readPixels(surface.value(), width, height);
                const render::ViewConstants view = render::viewConstants(scene.world, width, height, 0);
                // Just above the horizon, where the gradient starts.
                const Bgra horizon = at(pixels, width, 20, height / 2 - 1);
                const Bgra top = at(pixels, width, 20, 1);
                const Bgra bottom = at(pixels, width, 20, height - 2);
                // The horizon's gray (0.5, sRGB 188), the zenith's blue above,
                // the ground's red below.
                CHECK(std::abs(horizon.r - 188) < 25);
                CHECK(std::abs(horizon.b - 188) < 25);
                CHECK(top.b > top.r + 60);
                CHECK(bottom.r > bottom.b + 60);
                // The sun's disk, and not just off it.
                const Vector3 sun = view.cameraPosition + toSun * 100;
                const Bgra disk = test::pixelOf(pixels, view, sun);
                CHECK(disk.r > 250);
                CHECK(disk.g > 250);
                CHECK(test::pixelOf(pixels, view, sun + Vector3{0, 15, 0}).g < 220);
                // Geometry keeps its own color.
                CHECK(test::pixelOf(pixels, view, {0, 0, 0}).b > 250);
                CHECK(test::pixelOf(pixels, view, {0, 0, 0}).r > 250);
            }

            SUBCASE("Transparent surfaces blend nearest last, whatever order they were made in")
            {
                test::Scene scene;
                addCamera(scene);
                scene.ambient({1, 1, 1}, {1, 1, 1});
                // Nearly opaque panes, made nearest first: red over green over
                // blue, overlapping in pairs.
                scene.quad(red, {-1, 0, 0}, 2);
                scene.quad(green, {0, 0, 1}, 2);
                scene.quad(blue, {1, 0, 2}, 2);
                // An opaque quad in front of the right of the blue one, and
                // one behind a half-clear pane, seen through it.
                scene.quad(white, {2.2f, 0, -1}, 0.5f);
                scene.quad(white, {-5.2f, 0, 3}, 1.5f);
                scene.quad(clear, {-4, 0, 0}, 2);
                scene.render(surface.value());
                const std::vector<uint8_t> pixels = test::readPixels(surface.value(), width, height);
                const render::ViewConstants view = render::viewConstants(scene.world, width, height, 0);
                const Bgra redOverGreen = test::pixelOf(pixels, view, {-0.4f, 0, 0});
                const Bgra greenOverBlue = test::pixelOf(pixels, view, {0.6f, 0, 1});
                CHECK(redOverGreen.r > 200);
                CHECK(redOverGreen.g < 120);
                CHECK(greenOverBlue.g > 200);
                CHECK(greenOverBlue.b < 120);
                // In front: the opaque quad alone.
                const Bgra front = test::pixelOf(pixels, view, {2.2f, 0, -1});
                CHECK(front.r > 250);
                CHECK(front.g > 250);
                // Behind a half-clear blue pane: white tinted blue, not hidden.
                const Bgra through = test::pixelOf(pixels, view, {-4, 0, 0});
                CHECK(through.r > 120);
                CHECK(through.b > through.r + 30);
            }

            SUBCASE("Alpha-to-coverage antialiases alpha-tested edges with MSAA")
            {
                auto intermediateAcross = [&]() {
                    test::Scene scene;
                    addCamera(scene);
                    scene.defaultLighting();
                    // Off by a fraction of a pixel, so the clip edge doesn't
                    // fall on a pixel boundary.
                    scene.quad(masked, {0.03f, 0, 0}, 6);
                    scene.render(surface.value());
                    const std::vector<uint8_t> pixels = test::readPixels(surface.value(), width, height);
                    // Along the middle row, across the clip edge: pixels
                    // between clear (black) and the lit surface.
                    const Bgra lit = at(pixels, width, width / 2 + 30, height / 2);
                    REQUIRE(lit.r > 100);
                    uint32_t between = 0;
                    for (uint32_t x = width / 2 - 30; x < width / 2 + 30; ++x)
                    {
                        const Bgra p = at(pixels, width, x, height / 2);
                        between += p.r > 10 && difference(p, lit) > 30 ? 1 : 0;
                    }
                    return between;
                };
                CHECK(intermediateAcross() >= 1);
                REQUIRE(render::setMsaaSamples(1));
                CHECK(intermediateAcross() == 0);
                REQUIRE(render::setMsaaSamples(4));
            }

            render::destroySurface(surface.value());
            render::shutdown();
        }
        platform::shutdown();
        render::setAssetReader(0);
        test::library.clear();
    }
}
