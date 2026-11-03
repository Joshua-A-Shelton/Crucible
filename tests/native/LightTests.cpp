// Lights (ADR 0053): point and spot lights' falloff and cones, the
// hemisphere ambient, and clustering across depths, drawn on every backend
// with a plain Lambert test material, so pixel values follow the lights
// exactly.

#include "RenderInternal.h"
#include "TestModels.h"

#include <crucible/Camera.h>
#include <crucible/Ecs.h>
#include <crucible/Lights.h>
#include <crucible/MeshInstance.h>
#include <crucible/Platform.h>
#include <crucible/Render.h>
#include <crucible/Transform.h>

#include <doctest/doctest.h>
#include <slag/Slag.h>

#include <cmath>
#include <vector>

using namespace crucible;
using crucible::render::Camera;
using crucible::render::CameraProjection;
using crucible::render::PointLight;
using crucible::render::SpotLight;
using crucible::scenes::LocalTransform;
using crucible::scenes::WorldTransform;
using crucible::test::Bgra;

namespace {

// The readback's sRGB channel back in linear light.
float linear(uint8_t value)
{
    const float c = value / 255.0f;
    return c <= 0.04045f ? c / 12.92f : std::pow((c + 0.055f) / 1.055f, 2.4f);
}

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

ecs::Entity pointLight(test::Scene& scene, Vector3 position, float intensity, float range)
{
    const ecs::ComponentId set[] = {ecs::componentIdOf<LocalTransform>, ecs::componentIdOf<WorldTransform>,
                                    ecs::componentIdOf<PointLight>};
    const ecs::Entity entity = ecs::createEntity(*scene.world, set);
    *ecs::get<LocalTransform>(*scene.world, entity) = {position, {0, 0, 0, 1}, {1, 1, 1}};
    *ecs::get<PointLight>(*scene.world, entity) = {{1, 1, 1}, intensity, range};
    return entity;
}

} // namespace

TEST_SUITE("gpu")
{
    TEST_CASE("Lights fall off and cluster as they should on every backend")
    {
        scenes::registerTransforms();
        scenes::registerMeshInstance();
        render::registerCamera();
        render::registerLights();
        const assets::AssetId shader = test::idOf(7), lit = test::idOf(8), model = test::idOf(9);
        const std::vector<uint8_t> shaderBytes = test::readFile(CRUCIBLE_SHADER_DIR "/TestMaterials.crshader");
        const test::TypeLayout layout = test::typeNamed(shaderBytes, "Lit");
        test::library[7] = shaderBytes;
        test::library[8] = test::materialFile(shader, layout.index, layout.defaults);
        test::library[9] = test::quadModel({.remap = &lit});
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

            SUBCASE("Point lights fall off with inverse square, to nothing at their range")
            {
                test::Scene scene;
                addCamera(scene);
                // White quads facing the camera, lit head-on from 1 m and
                // 2 m by lights reaching 3.5 m (not the next quad); a third
                // light is 2 m away but reaches only 1.5 m.
                scene.quad(model, {-4, 0, 0}, 2);
                scene.quad(model, {0, 0, 0}, 2);
                scene.quad(model, {4, 0, 0}, 2);
                pointLight(scene, {-4, 0, -1}, 0.6f, 3.5f);
                pointLight(scene, {0, 0, -2}, 0.6f, 3.5f);
                pointLight(scene, {4, 0, -2}, 0.6f, 1.5f);
                scene.render(surface.value());
                const std::vector<uint8_t> pixels = test::readPixels(surface.value(), width, height);
                const render::ViewConstants view = render::viewConstants(scene.world, width, height, 0);
                // intensity / d² × (1 − (d / range)⁴)²
                auto expected = [](float d, float range) {
                    const float r = d / range;
                    const float window = 1.0f - r * r * r * r;
                    return 0.6f * window * window / (d * d);
                };
                CHECK(linear(test::pixelOf(pixels, view, {-4, 0, 0}).g) ==
                      doctest::Approx(expected(1, 3.5f)).epsilon(0.05));
                CHECK(linear(test::pixelOf(pixels, view, {0, 0, 0}).g) ==
                      doctest::Approx(expected(2, 3.5f)).epsilon(0.08));
                CHECK(test::pixelOf(pixels, view, {4, 0, 0}).g < 3);
            }

            SUBCASE("Spot lights light their cone")
            {
                test::Scene scene;
                addCamera(scene);
                scene.quad(model, {0, 0, 0}, 8);
                // From 3 m, a 30-degree cone (full angle) along +Z, at the
                // quad's center: 2 m off center is about 34 degrees out.
                const ecs::ComponentId set[] = {ecs::componentIdOf<LocalTransform>, ecs::componentIdOf<WorldTransform>,
                                                ecs::componentIdOf<SpotLight>};
                const ecs::Entity spot = ecs::createEntity(*scene.world, set);
                *ecs::get<LocalTransform>(*scene.world, spot) = {{0, 0, -3}, {0, 0, 0, 1}, {1, 1, 1}};
                *ecs::get<SpotLight>(*scene.world, spot) = {{1, 1, 1}, 4, 20, 0.35f, 0.52f};
                scene.render(surface.value());
                const std::vector<uint8_t> pixels = test::readPixels(surface.value(), width, height);
                const render::ViewConstants view = render::viewConstants(scene.world, width, height, 0);
                CHECK(test::pixelOf(pixels, view, {0, 0, 0}).g > 100);
                CHECK(test::pixelOf(pixels, view, {2, 0, 0}).g < 3);
            }

            SUBCASE("The ambient light is the sky's above and the ground's below")
            {
                test::Scene scene;
                addCamera(scene);
                // Tilted to face up and down, still toward the camera.
                scene.quad(model, {-2, 0, 0}, 2, Quaternion::fromAxisAngle({1, 0, 0}, 1.0f));
                scene.quad(model, {2, 0, 0}, 2, Quaternion::fromAxisAngle({1, 0, 0}, -1.0f));
                scene.ambient({0.8f, 0, 0}, {0, 0, 0.8f});
                scene.render(surface.value());
                const std::vector<uint8_t> pixels = test::readPixels(surface.value(), width, height);
                const render::ViewConstants view = render::viewConstants(scene.world, width, height, 0);
                const Bgra up = test::pixelOf(pixels, view, {-2, 0, 0});
                const Bgra down = test::pixelOf(pixels, view, {2, 0, 0});
                CHECK(up.r > 2 * up.b);
                CHECK(down.b > 2 * down.r);
            }

            SUBCASE("Many small lights at many depths each light only their own quad")
            {
                test::Scene scene;
                addCamera(scene);
                // A grid on screen, 10 columns by 12 rows, each row at its
                // own depth from 2 m to 57 m (so every cluster slice in that
                // range is used), each quad with a light just in front that
                // reaches its center but not its edges.
                struct Placed
                {
                    Vector3 center;
                    float size;
                };
                std::vector<Placed> placed;
                const float halfHeight = std::tan(0.45f); // per meter from the camera
                for (int row = 0; row < 12; ++row)
                {
                    const float depth = 2.0f + static_cast<float>(row) * 5.0f;
                    const float distance = depth + 10.0f; // from the camera
                    for (int column = 0; column < 10; ++column)
                    {
                        const float x =
                            ((static_cast<float>(column) + 0.5f) / 10.0f * 2.0f - 1.0f) * 2.0f * halfHeight * distance;
                        const float y =
                            (1.0f - (static_cast<float>(row) + 0.5f) / 12.0f * 2.0f) * halfHeight * distance;
                        const float size = 0.06f * distance;
                        placed.push_back({{x, y, depth}, size});
                        scene.quad(model, {x, y, depth}, size);
                        pointLight(scene, {x, y, depth - size * 0.4f}, 0.5f * size * size, size * 0.45f);
                    }
                }
                scene.render(surface.value());
                const std::vector<uint8_t> pixels = test::readPixels(surface.value(), width, height);
                const render::ViewConstants view = render::viewConstants(scene.world, width, height, 0);
                CHECK(render::drawStats().visible > 0);
                uint32_t unlit = 0, overlit = 0;
                for (const Placed& quad : placed)
                {
                    const Bgra center = test::pixelOf(pixels, view, quad.center);
                    CAPTURE(quad.center.z);
                    unlit += center.g < 60 ? 1 : 0;
                    // Near the quad's edge: past its light's reach.
                    const Bgra between =
                        test::pixelOf(pixels, view, {quad.center.x + quad.size * 0.45f, quad.center.y, quad.center.z});
                    overlit += between.g > 20 ? 1 : 0;
                }
                CHECK(unlit == 0);
                CHECK(overlit == 0);
            }

            render::destroySurface(surface.value());
            render::shutdown();
        }
        platform::shutdown();
        render::setAssetReader(0);
        test::library.clear();
    }
}
