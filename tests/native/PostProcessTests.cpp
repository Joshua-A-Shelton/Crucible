// Finishing the image (ADR 0055): exposure, the tonemappers, bloom and its
// threshold, and grading, from the camera's PostProcess, on every backend.
// Surfaces are the Lambert test material under a white ambient light, so a
// surface's linear value is its color times the ambient's intensity.

#include "RenderInternal.h"
#include "TestModels.h"

#include <crucible/Camera.h>
#include <crucible/Ecs.h>
#include <crucible/Lights.h>
#include <crucible/Platform.h>
#include <crucible/PostProcess.h>
#include <crucible/Render.h>
#include <crucible/Transform.h>

#include <doctest/doctest.h>
#include <slag/Slag.h>

#include <cmath>
#include <vector>

using namespace crucible;
using crucible::render::Camera;
using crucible::render::CameraProjection;
using crucible::render::PostProcess;
using crucible::render::Tonemapper;
using crucible::scenes::LocalTransform;
using crucible::scenes::WorldTransform;
using crucible::test::Bgra;

namespace {

// A camera at z = -10 looking along +Z, with a PostProcess.
PostProcess& addCamera(test::Scene& scene)
{
    const ecs::ComponentId set[] = {ecs::componentIdOf<LocalTransform>, ecs::componentIdOf<WorldTransform>,
                                    ecs::componentIdOf<Camera>, ecs::componentIdOf<PostProcess>};
    const ecs::Entity camera = ecs::createEntity(*scene.world, set);
    *ecs::get<LocalTransform>(*scene.world, camera) = {{0, 0, -10}, {0, 0, 0, 1}, {1, 1, 1}};
    *ecs::get<Camera>(*scene.world,
                      camera) = {.projection = CameraProjection::Perspective, .fieldOfViewY = 0.9f, .nearPlane = 0.1f};
    return *ecs::get<PostProcess>(*scene.world, camera);
}

int near(int value, int expected)
{
    return std::abs(value - expected);
}

} // namespace

TEST_SUITE("gpu")
{
    TEST_CASE("PostProcess exposes and tonemaps and blooms and grades on every backend")
    {
        scenes::registerTransforms();
        scenes::registerMeshInstance();
        render::registerCamera();
        render::registerLights();
        const std::vector<uint8_t> shaderBytes = test::readFile(CRUCIBLE_SHADER_DIR "/TestMaterials.crshader");
        const test::TypeLayout layout = test::typeNamed(shaderBytes, "Lit");
        test::library[7] = shaderBytes;
        const assets::AssetId shader = test::idOf(7);
        uint8_t next = 0;
        auto coloredQuad = [&](float r, float g, float b) {
            const uint8_t i = next++;
            std::vector<uint8_t> parameters = layout.defaults;
            const float color[4] = {r, g, b, 1};
            std::memcpy(parameters.data() + layout.offsetOf("color"), color, sizeof(color));
            test::library[20 + i] = test::materialFile(shader, layout.index, parameters);
            const assets::AssetId material = test::idOf(20 + i);
            test::library[40 + i] = test::quadModel({.remap = &material});
            return test::idOf(40 + i);
        };
        const assets::AssetId gray = coloredQuad(0.25f, 0.25f, 0.25f), white = coloredQuad(1, 1, 1),
                              orange = coloredQuad(1, 0.4f, 0.1f);
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
            auto render = [&](test::Scene& scene) {
                scene.render(surface.value());
                return test::readPixels(surface.value(), width, height);
            };

            SUBCASE("Exposure scales the light before tonemapping")
            {
                test::Scene scene;
                PostProcess& post = addCamera(scene);
                scene.ambient({1, 1, 1}, {1, 1, 1});
                scene.quad(gray, {0, 0, 0}, 2);
                scene.render(surface.value()); // places the camera
                const render::ViewConstants view = render::viewConstants(scene.world, width, height, 0);
                const uint8_t plain = test::pixelOf(render(scene), view, {0, 0, 0}).g;
                CHECK(near(plain, test::srgbByte(test::pbrNeutral({0.25f, 0.25f, 0.25f}).y)) <= 2);
                post.exposure = 1; // x2
                const uint8_t brighter = test::pixelOf(render(scene), view, {0, 0, 0}).g;
                CHECK(near(brighter, test::srgbByte(test::pbrNeutral({0.5f, 0.5f, 0.5f}).y)) <= 2);
            }

            SUBCASE("The tonemappers compress a bright orange differently")
            {
                test::Scene scene;
                PostProcess& post = addCamera(scene);
                scene.ambient({4, 4, 4}, {4, 4, 4}); // the orange at (4, 1.6, 0.4)
                scene.quad(orange, {0, 0, 0}, 2);
                scene.render(surface.value()); // places the camera
                const render::ViewConstants view = render::viewConstants(scene.world, width, height, 0);
                const Bgra neutral = test::pixelOf(render(scene), view, {0, 0, 0});
                const Vector3 expected = test::pbrNeutral({4, 1.6f, 0.4f});
                CHECK(near(neutral.r, test::srgbByte(expected.x)) <= 2);
                CHECK(near(neutral.g, test::srgbByte(expected.y)) <= 2);
                post.tonemapper = Tonemapper::AgX;
                const Bgra agx = test::pixelOf(render(scene), view, {0, 0, 0});
                post.tonemapper = Tonemapper::Aces;
                const Bgra aces = test::pixelOf(render(scene), view, {0, 0, 0});
                // Each keeps it orange (red over green over blue), each
                // differently.
                for (const Bgra& p : {neutral, agx, aces})
                {
                    CHECK(p.r > p.g);
                    CHECK(p.g > p.b);
                }
                CHECK(near(agx.g, neutral.g) + near(agx.b, neutral.b) > 10);
                CHECK(near(aces.g, neutral.g) + near(aces.b, neutral.b) > 10);
            }

            SUBCASE("Bloom spreads bright light past its edges, above the threshold only")
            {
                test::Scene scene;
                PostProcess& post = addCamera(scene);
                scene.ambient({16, 16, 16}, {16, 16, 16});
                scene.quad(white, {0, 0, 0}, 0.5f); // linear 16
                scene.render(surface.value());      // places the camera
                const render::ViewConstants view = render::viewConstants(scene.world, width, height, 0);
                const Vector3 beside{0.45f, 0, 0};                       // 0.2 past its edge
                CHECK(test::pixelOf(render(scene), view, beside).g < 3); // no bloom: dark beside it
                post.bloomStrength = 0.04f;
                const uint8_t glow = test::pixelOf(render(scene), view, beside).g;
                CHECK(glow > 8);
                // A threshold over the quad's light: no glow at all.
                post.bloomThreshold = 32;
                CHECK(test::pixelOf(render(scene), view, beside).g < 3);
                // Under it: the glow, slightly less.
                post.bloomThreshold = 4;
                const uint8_t thresholded = test::pixelOf(render(scene), view, beside).g;
                CHECK(thresholded > 3);
                CHECK(thresholded < glow);
            }

            SUBCASE("Grading changes saturation and white balance and contrast and gain")
            {
                test::Scene scene;
                PostProcess& post = addCamera(scene);
                scene.ambient({1, 1, 1}, {1, 1, 1});
                scene.quad(orange, {-2, 0, 0}, 2);
                scene.quad(gray, {2, 0, 0}, 2);
                scene.render(surface.value()); // places the camera
                const render::ViewConstants view = render::viewConstants(scene.world, width, height, 0);
                const Vector3 left{-2, 0, 0}, right{2, 0, 0};
                const std::vector<uint8_t> plain = render(scene);
                const Bgra orangePlain = test::pixelOf(plain, view, left);
                const Bgra grayPlain = test::pixelOf(plain, view, right);

                post.saturation = -1;
                const Bgra desaturated = test::pixelOf(render(scene), view, left);
                CHECK(near(desaturated.r, desaturated.g) <= 1);
                CHECK(near(desaturated.g, desaturated.b) <= 1);
                post.saturation = 0;

                post.temperature = 0.6f; // warmer
                const Bgra warm = test::pixelOf(render(scene), view, right);
                CHECK(warm.r > warm.b + 10);
                post.temperature = -0.6f;
                const Bgra cool = test::pixelOf(render(scene), view, right);
                CHECK(cool.b > cool.r + 10);
                post.temperature = 0;

                post.contrast = 0.5f; // below mid-gray gets darker, above lighter
                const Bgra contrasted = test::pixelOf(render(scene), view, left);
                CHECK(contrasted.r > orangePlain.r);
                CHECK(contrasted.b < orangePlain.b);
                post.contrast = 0;

                post.gain = {0, 0, 1}; // blue x2
                const Bgra gained = test::pixelOf(render(scene), view, right);
                CHECK(gained.b > grayPlain.b + 30);
                CHECK(near(gained.r, grayPlain.r) <= 1);
            }

            render::destroySurface(surface.value());
            render::shutdown();
        }
        platform::shutdown();
        render::setAssetReader(0);
        test::library.clear();
    }
}
