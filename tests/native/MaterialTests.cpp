// Materials (ADR 0052): the engine's toon and PBR shading under the
// temporary sun, alpha-tested materials, and double-sided ones, drawn on
// every backend from models' generated materials.

#include "RenderInternal.h"
#include "TestModels.h"

#include <crucible/BinaryFile.h>
#include <crucible/Camera.h>
#include <crucible/Ecs.h>
#include <crucible/MaterialFile.h>
#include <crucible/MeshInstance.h>
#include <crucible/Platform.h>
#include <crucible/Render.h>
#include <crucible/Shader.h>
#include <crucible/Transform.h>

#include <doctest/doctest.h>
#include <slag/Slag.h>

#include <cmath>
#include <cstdlib>
#include <fstream>
#include <iterator>
#include <string>
#include <vector>

using namespace crucible;
using crucible::render::Camera;
using crucible::render::CameraProjection;
using crucible::scenes::LocalTransform;
using crucible::scenes::WorldTransform;
using crucible::test::Bgra;
using crucible::test::QuadOptions;

namespace {

// The direction toward the temporary sun (material.slang).
const Vector3 sun = normalize(Vector3{0.32f, 0.86f, -0.4f});

// A tilt about X that turns the quad's normal (0, 0, -1) to light it with
// N·L = target, still facing the camera.
Quaternion tiltFor(float target)
{
    float best = 0, error = 1e9f;
    for (float angle = -1.5f; angle <= 1.5f; angle += 0.0005f)
    {
        const Quaternion q = Quaternion::fromAxisAngle({1, 0, 0}, angle);
        const Vector3 normal = transformPoint({0, 0, -1}, Matrix4x4::rotation(q));
        const float e = std::fabs(dot(normal, sun) - target);
        if (e < error && normal.z < -0.3f) // still facing the camera
        {
            error = e;
            best = angle;
        }
    }
    return Quaternion::fromAxisAngle({1, 0, 0}, best);
}

int difference(Bgra a, Bgra b)
{
    return std::abs(a.r - b.r) + std::abs(a.g - b.g) + std::abs(a.b - b.b);
}

ecs::Entity addCamera(test::Scene& scene)
{
    const ecs::ComponentId set[] = {ecs::componentIdOf<LocalTransform>, ecs::componentIdOf<WorldTransform>,
                                    ecs::componentIdOf<Camera>};
    const ecs::Entity camera = ecs::createEntity(*scene.world, set);
    *ecs::get<LocalTransform>(*scene.world, camera) = {{0, 0, -10}, {0, 0, 0, 1}, {1, 1, 1}};
    *ecs::get<Camera>(*scene.world,
                      camera) = {.projection = CameraProjection::Perspective, .fieldOfViewY = 0.8f, .nearPlane = 0.1f};
    return camera;
}

} // namespace

TEST_SUITE("gpu")
{
    TEST_CASE("Toon bands its lighting where PBR shades smoothly on every backend")
    {
        scenes::registerTransforms();
        scenes::registerMeshInstance();
        render::registerCamera();
        render::registerLights();
        const assets::AssetId toon = test::idOf(1), pbr = test::idOf(2), masked = test::idOf(3),
                              halfClear = test::idOf(4), oneSided = test::idOf(5), twoSided = test::idOf(6);
        test::library[1] = test::quadModel({.baseColor = {1, 0.2f, 0.2f, 1}});
        test::library[2] = test::quadModel({.baseColor = {1, 0.2f, 0.2f, 1}, .type = assets::DefaultMaterialType::Pbr});
        test::library[3] = test::quadModel({.texture = &halfClear, .alphaMode = assets::AlphaMode::Mask});
        // Two texels: transparent white, then opaque white.
        test::library[4] = test::rowTexture({255, 255, 255, 0, 255, 255, 255, 255});
        test::library[5] = test::quadModel();
        test::library[6] = test::quadModel({.doubleSided = true});
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
            {
                test::Scene scene;
                scene.defaultLighting();
                addCamera(scene);
                // N·L of 0.4 and 0.65 fall in the same toon band, 0.9 in
                // the next (2 bands, edges near 0.25 and 0.75).
                const float lighting[3] = {0.4f, 0.65f, 0.9f};
                for (int i = 0; i < 3; ++i)
                {
                    scene.quad(toon, {-6.0f + 2.5f * static_cast<float>(i), 1.5f, 0}, 2, tiltFor(lighting[i]));
                    scene.quad(pbr, {-6.0f + 2.5f * static_cast<float>(i), -1.5f, 0}, 2, tiltFor(lighting[i]));
                }
                // Alpha-tested: the left of the quad (u < 0.5) is clipped.
                scene.quad(masked, {3.5f, 1.5f, 0}, 2);
                // Turned away from the camera: drawn only if double-sided.
                const Quaternion away = Quaternion::fromAxisAngle({0, 1, 0}, 3.14159265f);
                scene.quad(oneSided, {3.5f, -1.5f, 0}, 2, away);
                scene.quad(twoSided, {6.0f, -1.5f, 0}, 2, away);

                scene.render(surface.value());
                const std::vector<uint8_t> pixels = test::readPixels(surface.value(), width, height);
                const render::ViewConstants view = render::viewConstants(scene.world, width, height, 0);
                Bgra toonPixels[3], pbrPixels[3];
                for (int i = 0; i < 3; ++i)
                {
                    toonPixels[i] = test::pixelOf(pixels, view, {-6.0f + 2.5f * static_cast<float>(i), 1.5f, 0});
                    pbrPixels[i] = test::pixelOf(pixels, view, {-6.0f + 2.5f * static_cast<float>(i), -1.5f, 0});
                    CAPTURE(i);
                    CHECK(toonPixels[i].r > toonPixels[i].g); // reddish, and lit
                    CHECK(pbrPixels[i].r > pbrPixels[i].g);
                }
                // Toon: one band for the first two, a brighter one for the third.
                CHECK(difference(toonPixels[0], toonPixels[1]) <= 3);
                CHECK(toonPixels[2].r > toonPixels[1].r + 10);
                // PBR: brighter with every step.
                CHECK(pbrPixels[1].r > pbrPixels[0].r + 5);
                CHECK(pbrPixels[2].r > pbrPixels[1].r + 5);

                const Bgra clipped = test::pixelOf(pixels, view, {3.5f - 0.7f, 1.5f, 0});
                const Bgra kept = test::pixelOf(pixels, view, {3.5f + 0.7f, 1.5f, 0});
                CHECK(clipped.r < 3);
                CHECK(kept.r > 100);

                CHECK(test::pixelOf(pixels, view, {3.5f, -1.5f, 0}).r < 3);
                CHECK(test::pixelOf(pixels, view, {6.0f, -1.5f, 0}).r > 30);
            }
            render::destroySurface(surface.value());
            render::shutdown();
        }
        platform::shutdown();
        render::setAssetReader(0);
        test::library.clear();
    }
}

TEST_SUITE("gpu")
{
    TEST_CASE("Game materials draw with their own shading model on every backend")
    {
        scenes::registerTransforms();
        scenes::registerMeshInstance();
        render::registerCamera();
        render::registerLights();
        const assets::AssetId shader = test::idOf(7), flat = test::idOf(8), model = test::idOf(9);
        const std::vector<uint8_t> shaderBytes = test::readFile(CRUCIBLE_SHADER_DIR "/TestMaterials.crshader");
        const test::TypeLayout layout = test::typeNamed(shaderBytes, "Flat");
        test::library[7] = shaderBytes;
        test::library[8] = test::materialFile(shader, layout.index, layout.defaults);
        test::library[9] = test::quadModel({.baseColor = {1, 0, 0, 1}, .remap = &flat});
        render::setAssetReader(reinterpret_cast<uint64_t>(&test::readTestAsset));
        REQUIRE(platform::initialize());
        for (slag::Backend backend : {slag::Backend::D3D12, slag::Backend::Vulkan})
        {
            if (!slag::isBackendAvailable(backend) || !render::initialize(backend))
            {
                continue;
            }
            CAPTURE(static_cast<int>(backend));
            constexpr uint32_t width = 64, height = 64;
            Result<render::SurfaceHandle> surface = render::createOffscreenSurface(width, height);
            REQUIRE(surface);
            {
                test::Scene scene;
                scene.defaultLighting();
                addCamera(scene);
                scene.quad(model, {0, 0, 0}, 4);
                scene.render(surface.value());
                const render::ViewConstants view = render::viewConstants(scene.world, width, height, 0);
                // The slot draws the authored material, not its generated red:
                // green * (one light * 0.5 + 0.25 ambient) = 0.75, about 225
                // in sRGB. Every hook ran: no light, no ambient, or no finish
                // would come out otherwise.
                Bgra pixel = test::pixelOf(test::readPixels(surface.value(), width, height), view, {0, 0, 0});
                CHECK(pixel.r < 5);
                CHECK(pixel.g > 215);
                CHECK(pixel.g < 235);

                // Acquired again: the model's, counted.
                Result<render::MaterialHandle> material = render::acquireMaterial(flat);
                REQUIRE(material);
                CHECK(render::materialTypeName(material.value()) == "Flat");
                CHECK(render::materialShader(material.value()) == shader);
                // New parameters: red, strength 1 (1.25, clamped to 255).
                std::vector<uint8_t> parameters = layout.defaults;
                const float red[4] = {1, 0, 0, 1}, strength = 1;
                std::memcpy(parameters.data() + layout.offsetOf("color"), red, sizeof(red));
                std::memcpy(parameters.data() + layout.offsetOf("strength"), &strength, sizeof(strength));
                REQUIRE(render::setMaterialParameters(material.value(), parameters));
                CHECK(render::setMaterialParameters(material.value(), {parameters.data(), 4}).status() ==
                      Status::InvalidData);
                scene.render(surface.value());
                pixel = test::pixelOf(test::readPixels(surface.value(), width, height), view, {0, 0, 0});
                CHECK(pixel.r > 250);
                CHECK(pixel.g < 5);
                render::releaseMaterial(material.value());
            }
            render::destroySurface(surface.value());
            render::shutdown();
        }
        platform::shutdown();
        render::setAssetReader(0);
        test::library.clear();
    }
}
