// Shadows (ADR 0054): the sun's cascades near and far, cut off past the
// shadow distance; spot and point lights' atlas tiles, kept while nothing
// changes; and the cascades' texel snapping. Drawn on every backend with a
// plain Lambert test material, so a shadowed pixel is the ambient light alone.

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
using crucible::render::DirectionalLight;
using crucible::render::PointLight;
using crucible::render::SpotLight;
using crucible::scenes::LocalTransform;
using crucible::scenes::MeshInstance;
using crucible::scenes::WorldTransform;

namespace {

constexpr float halfPi = 1.5707963f;
// Quads face -Z; these turn them to face up, or toward -X.
const Quaternion faceUp = Quaternion::fromAxisAngle({1, 0, 0}, halfPi);
const Quaternion faceMinusX = Quaternion::fromAxisAngle({0, 1, 0}, halfPi);

// A camera at `position`, looking along +Z tipped down by `pitch` radians.
ecs::Entity addCamera(test::Scene& scene, Vector3 position, float pitch)
{
    const ecs::ComponentId set[] = {ecs::componentIdOf<LocalTransform>, ecs::componentIdOf<WorldTransform>,
                                    ecs::componentIdOf<Camera>};
    const ecs::Entity camera = ecs::createEntity(*scene.world, set);
    *ecs::get<LocalTransform>(*scene.world, camera) = {position, Quaternion::fromAxisAngle({1, 0, 0}, pitch),
                                                       {1, 1, 1}};
    *ecs::get<Camera>(*scene.world,
                      camera) = {.projection = CameraProjection::Perspective, .fieldOfViewY = 0.9f, .nearPlane = 0.1f};
    return camera;
}

ecs::Entity castingSun(test::Scene& scene, Vector3 towardLight, float distance = 0)
{
    const ecs::Entity sun = scene.sun(towardLight, {1, 1, 1}, 1);
    DirectionalLight& light = *ecs::get<DirectionalLight>(*scene.world, sun);
    light.castsShadows = true;
    light.shadowDistance = distance;
    return sun;
}

ecs::Entity light(test::Scene& scene, Vector3 position, Quaternion rotation, ecs::ComponentId kind)
{
    const ecs::ComponentId set[] = {ecs::componentIdOf<LocalTransform>, ecs::componentIdOf<WorldTransform>, kind};
    const ecs::Entity entity = ecs::createEntity(*scene.world, set);
    *ecs::get<LocalTransform>(*scene.world, entity) = {position, rotation, {1, 1, 1}};
    return entity;
}

uint8_t green(const std::vector<uint8_t>& pixels, const render::ViewConstants& view, Vector3 position)
{
    return test::pixelOf(pixels, view, position).g;
}

} // namespace

TEST_CASE("The sun's cascades are snapped to texels and keep their size as the camera moves")
{
    render::ViewConstants view{};
    auto place = [&](Vector3 position, float yaw) {
        view.projection = Matrix4x4::perspectiveReverseZInfinite(0.9f, 16.0f / 9.0f, 0.1f);
        view.inverseView = Matrix4x4::rotation(Quaternion::fromAxisAngle({0, 1, 0}, yaw)) *
                           Matrix4x4::translation(position);
        view.cameraPosition = position;
    };
    const Vector3 sun = normalize(Vector3{0.3f, 0.8f, -0.5f});
    render::ShadowView first[render::shadowCascadeCount], second[render::shadowCascadeCount];
    place({1, 2, 3}, 0.2f);
    render::fitCascades(view, sun, 100, 2048, first);
    const Vector4 splits = view.cascadeSplits;
    place({1.37f, 2.11f, 2.52f}, 1.3f);
    render::fitCascades(view, sun, 100, 2048, second);

    CHECK(splits.w == doctest::Approx(100));
    CHECK(splits.x < splits.y);
    CHECK(splits.y < splits.z);
    CHECK(splits.z < splits.w);
    for (uint32_t i = 0; i < render::shadowCascadeCount; ++i)
    {
        CAPTURE(i);
        // The same texel size wherever the camera is and looks.
        CHECK(first[i].normalOffset == second[i].normalOffset);
        CHECK(first[i].viewProjection.rows[0].x == second[i].viewProjection.rows[0].x);
        // A fixed point in the world lands on the same spot within a texel
        // in both: the maps moved by whole texels.
        const Vector3 point{5, 0, 7};
        const Vector4 a = transform(Vector4{point.x, point.y, point.z, 1}, first[i].viewProjection);
        const Vector4 b = transform(Vector4{point.x, point.y, point.z, 1}, second[i].viewProjection);
        const float texelsX = (a.x - b.x) * 0.5f * 2048;
        const float texelsY = (a.y - b.y) * 0.5f * 2048;
        CHECK(std::fabs(texelsX - std::round(texelsX)) < 0.01f);
        CHECK(std::fabs(texelsY - std::round(texelsY)) < 0.01f);
        if (i > 0)
        {
            CHECK(first[i].normalOffset > first[i - 1].normalOffset);
        }
    }
}

TEST_SUITE("gpu")
{
    TEST_CASE("Lights cast shadows on every backend")
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
            REQUIRE(render::shadowFormat() != slag::PixelFormat::Undefined);
            constexpr uint32_t width = 256, height = 128;
            Result<render::SurfaceHandle> surface = render::createOffscreenSurface(width, height);
            REQUIRE(surface);

            SUBCASE("The sun shadows the ground under an occluder, in every cascade, out to its distance")
            {
                test::Scene scene;
                addCamera(scene, {0, 2, -10}, 0.25f);
                scene.ambient({0.1f, 0.1f, 0.1f}, {0.1f, 0.1f, 0.1f});
                const ecs::Entity sun = castingSun(scene, normalize(Vector3{0.6f, 1, 0.3f}), 80);
                scene.quad(model, {0, 0, 100}, 260, faceUp);
                // Occluders half a meter up, at view depths in each cascade
                // (their splits here: 5.4, 12.1, 26.3, 80 m) and one past the
                // shadow distance; each shadow falls just off its occluder's
                // center (the sun is to the side).
                const float depths[] = {3.5f, 9, 20, 50, 120};
                std::vector<Vector3> shadowed;
                std::vector<float> sizes;
                for (float depth : depths)
                {
                    const float z = (depth - 0.49f) / 0.969f - 10;
                    const float size = 0.4f + depth * 0.06f;
                    scene.quad(model, {z * 0.2f, 0.5f, z}, size, faceUp);
                    shadowed.push_back({z * 0.2f - 0.3f, 0, z - 0.15f});
                    sizes.push_back(size);
                }
                scene.render(surface.value());
                const std::vector<uint8_t> pixels = test::readPixels(surface.value(), width, height);
                const render::ViewConstants view = render::viewConstants(scene.world, width, height, 0);
                CHECK(render::shadowStats().cascades == render::shadowCascadeCount);
                // Shadowed: the ambient's 0.1 (sRGB 89); lit: well over.
                for (size_t i = 0; i < 4; ++i)
                {
                    CAPTURE(depths[i]);
                    CHECK(green(pixels, view, shadowed[i]) < 100);
                    CHECK(green(pixels, view, shadowed[i] - Vector3{sizes[i] + 0.6f, 0, 0}) > 200);
                }
                // Past the shadow distance: lit.
                CHECK(green(pixels, view, shadowed[4]) > 200);

                // Without castsShadows, nothing is shadowed.
                ecs::get<DirectionalLight>(*scene.world, sun)->castsShadows = false;
                scene.render(surface.value());
                const std::vector<uint8_t> unshadowed = test::readPixels(surface.value(), width, height);
                CHECK(render::shadowStats().cascades == 0);
                CHECK(green(unshadowed, view, shadowed[0]) > 200);
            }

            SUBCASE("Meshes marked noShadows cast none")
            {
                test::Scene scene;
                addCamera(scene, {0, 6, -10}, 0.3f);
                scene.ambient({0.1f, 0.1f, 0.1f}, {0.1f, 0.1f, 0.1f});
                castingSun(scene, {0, 1, 0});
                scene.quad(model, {0, 0, 0}, 30, faceUp);
                const ecs::Entity occluder = scene.quad(model, {0, 1.5f, 0}, 2, faceUp);
                ecs::get<MeshInstance>(*scene.world, occluder)->noShadows = true;
                scene.render(surface.value());
                const std::vector<uint8_t> pixels = test::readPixels(surface.value(), width, height);
                const render::ViewConstants view = render::viewConstants(scene.world, width, height, 0);
                CHECK(green(pixels, view, {0, 0, 0}) > 200);
            }

            SUBCASE("Spot lights shadow through their tile, which is kept until something in it changes")
            {
                test::Scene scene;
                addCamera(scene, {0, 6, -10}, 0.45f);
                scene.ambient({0.1f, 0.1f, 0.1f}, {0.1f, 0.1f, 0.1f});
                scene.quad(model, {0, 0, 0}, 30, faceUp);
                const ecs::Entity occluder = scene.quad(model, {0, 2, 0}, 1, faceUp);
                const ecs::Entity bystander = scene.quad(model, {40, 0, 40}, 1, faceUp);
                const ecs::Entity spot = light(scene, {0, 4, 0}, test::Scene::facing({0, -1, 0}),
                                               ecs::componentIdOf<SpotLight>);
                *ecs::get<SpotLight>(*scene.world, spot) = {{1, 1, 1}, 24, 12, 1.4f, 1.6f, true};

                scene.render(surface.value());
                std::vector<uint8_t> pixels = test::readPixels(surface.value(), width, height);
                const render::ViewConstants view = render::viewConstants(scene.world, width, height, 0);
                CHECK(render::shadowStats().tilesDrawn == 1);
                CHECK(green(pixels, view, {0, 0, 0}) < 100);
                CHECK(green(pixels, view, {1.6f, 0, 0}) > 200);

                // Nothing changed: the tile is kept, and still shadows.
                scene.render(surface.value());
                pixels = test::readPixels(surface.value(), width, height);
                CHECK(render::shadowStats().tilesDrawn == 0);
                CHECK(render::shadowStats().tilesKept == 1);
                CHECK(green(pixels, view, {0, 0, 0}) < 100);

                // Something outside the light's reach moved: still kept.
                ecs::get<LocalTransform>(*scene.world, bystander)->position = {41, 0, 40};
                scene.render(surface.value());
                CHECK(render::shadowStats().tilesDrawn == 0);

                // The occluder moved: drawn again, and the shadow follows.
                ecs::get<LocalTransform>(*scene.world, occluder)->position = {1.6f, 2, 0};
                scene.render(surface.value());
                pixels = test::readPixels(surface.value(), width, height);
                CHECK(render::shadowStats().tilesDrawn == 1);
                CHECK(green(pixels, view, {0, 0, 0}) > 200);
                CHECK(green(pixels, view, {3.2f, 0, 0}) < 100);

                // The light moved: drawn again.
                scene.render(surface.value());
                CHECK(render::shadowStats().tilesDrawn == 0);
                ecs::get<LocalTransform>(*scene.world, spot)->position = {0, 4.5f, 0};
                scene.render(surface.value());
                CHECK(render::shadowStats().tilesDrawn == 1);

                // The occluder removed: drawn again (its casters changed).
                ecs::destroyEntity(*scene.world, occluder);
                scene.render(surface.value());
                pixels = test::readPixels(surface.value(), width, height);
                CHECK(render::shadowStats().tilesDrawn == 1);
                CHECK(green(pixels, view, {3.4f, 0, 0}) > 200);
            }

            SUBCASE("Point lights shadow through each cube face")
            {
                test::Scene scene;
                addCamera(scene, {0, 6, -10}, 0.45f);
                scene.ambient({0.1f, 0.1f, 0.1f}, {0.1f, 0.1f, 0.1f});
                scene.quad(model, {0, 0, 0}, 30, faceUp);
                // Below the light (its -Y face), and between it and a point
                // off to the side (its +X face).
                scene.quad(model, {0, 1.5f, 0}, 1, faceUp);
                scene.quad(model, {2.6f, 1.44f, 0}, 1, faceMinusX);
                const ecs::Entity point = light(scene, {0, 3, 0}, {0, 0, 0, 1}, ecs::componentIdOf<PointLight>);
                *ecs::get<PointLight>(*scene.world, point) = {{1, 1, 1}, 60, 12, true};
                scene.render(surface.value());
                const std::vector<uint8_t> pixels = test::readPixels(surface.value(), width, height);
                const render::ViewConstants view = render::viewConstants(scene.world, width, height, 0);
                CHECK(render::shadowStats().tilesDrawn == 6);
                CHECK(green(pixels, view, {0, 0, 0}) < 100);
                CHECK(green(pixels, view, {-2, 0, 0}) > 200);
                CHECK(green(pixels, view, {5, 0, 0}) < 100);
                CHECK(green(pixels, view, {5, 0, 2}) > 200);
            }

            render::destroySurface(surface.value());
            render::shutdown();
        }
        platform::shutdown();
        render::setAssetReader(0);
        test::library.clear();
    }
}
