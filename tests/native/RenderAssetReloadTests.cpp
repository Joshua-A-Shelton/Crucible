// Reloading GPU assets in place (ADR 0072): a texture, a model, a material,
// and a game shader loaded again after their bytes change, drawn anew with
// the same handles; a reload that fails leaves the old version drawing.

#include "RenderInternal.h"
#include "TestModels.h"

#include <crucible/Camera.h>
#include <crucible/Ecs.h>
#include <crucible/MeshInstance.h>
#include <crucible/Platform.h>
#include <crucible/Render.h>
#include <crucible/RenderAssets.h>
#include <crucible/Transform.h>

#include <doctest/doctest.h>
#include <slag/Slag.h>

#include <cstring>
#include <vector>

using namespace crucible;
using crucible::render::Camera;
using crucible::render::CameraProjection;
using crucible::scenes::LocalTransform;
using crucible::scenes::WorldTransform;
using crucible::test::Bgra;

namespace {

void addCamera(test::Scene& scene)
{
    const ecs::ComponentId set[] = {ecs::componentIdOf<LocalTransform>, ecs::componentIdOf<WorldTransform>,
                                    ecs::componentIdOf<Camera>};
    const ecs::Entity camera = ecs::createEntity(*scene.world, set);
    *ecs::get<LocalTransform>(*scene.world, camera) = {{0, 0, -10}, {0, 0, 0, 1}, {1, 1, 1}};
    *ecs::get<Camera>(*scene.world,
                      camera) = {.projection = CameraProjection::Perspective, .fieldOfViewY = 0.8f, .nearPlane = 0.1f};
}

bool reddest(Bgra pixel)
{
    return pixel.r > 100 && pixel.r > pixel.g * 2 && pixel.r > pixel.b * 2;
}

bool greenest(Bgra pixel)
{
    return pixel.g > 100 && pixel.g > pixel.r * 2 && pixel.g > pixel.b * 2;
}

bool bluest(Bgra pixel)
{
    return pixel.b > 100 && pixel.b > pixel.r * 2 && pixel.b > pixel.g * 2;
}

} // namespace

TEST_CASE("Nothing is loaded to reload before the renderer starts")
{
    Result<bool> reloaded = render::reloadRenderAsset(test::idOf(1));
    REQUIRE(reloaded);
    CHECK_FALSE(reloaded.value());
}

TEST_SUITE("gpu")
{
    TEST_CASE("Loaded assets reload in place on every backend")
    {
        scenes::registerTransforms();
        scenes::registerMeshInstance();
        render::registerCamera();
        render::registerLights();
        const assets::AssetId textured = test::idOf(1), colored = test::idOf(2), texture = test::idOf(4);
        const assets::AssetId shader = test::idOf(7), flat = test::idOf(8), remapped = test::idOf(9);
        const std::vector<uint8_t> shaderBytes = test::readFile(CRUCIBLE_SHADER_DIR "/TestMaterials.crshader");
        const test::TypeLayout layout = test::typeNamed(shaderBytes, "Flat");
        std::vector<uint8_t> redFlat = layout.defaults;
        const float red[4] = {1, 0, 0, 1};
        std::memcpy(redFlat.data() + layout.offsetOf("color"), red, sizeof(red));
        assets::setAssetReader(reinterpret_cast<uint64_t>(&test::readTestAsset));
        REQUIRE(platform::initialize());
        for (slag::Backend backend : {slag::Backend::D3D12, slag::Backend::Vulkan})
        {
            if (!slag::isBackendAvailable(backend) || !render::initialize(backend))
            {
                continue;
            }
            CAPTURE(static_cast<int>(backend));
            test::library[1] = test::quadModel({.texture = &texture});
            test::library[2] = test::quadModel({.baseColor = {1, 0, 0, 1}});
            test::library[4] = test::pixelTexture(255, 0, 0, 255);
            test::library[7] = shaderBytes;
            test::library[8] = test::materialFile(shader, layout.index, layout.defaults); // green
            test::library[9] = test::quadModel({.remap = &flat});
            constexpr uint32_t width = 96, height = 64;
            Result<render::SurfaceHandle> surface = render::createOffscreenSurface(width, height);
            REQUIRE(surface);
            {
                test::Scene scene;
                scene.defaultLighting();
                addCamera(scene);
                scene.quad(textured, {-2.5f, 0, 0}, 2);
                const ecs::Entity model = scene.quad(colored, {0, 0, 0}, 2);
                scene.quad(remapped, {2.5f, 0, 0}, 2);
                scene.render(surface.value());
                const render::ViewConstants view = render::viewConstants(scene.world, width, height, 0);
                auto pixel = [&](float x) {
                    return test::pixelOf(test::readPixels(surface.value(), width, height), view, {x, 0, 0});
                };
                CHECK(reddest(pixel(-2.5f)));
                CHECK(reddest(pixel(0)));
                CHECK(greenest(pixel(2.5f)));
                Result<render::TextureHandle> held = render::acquireTexture(texture);
                REQUIRE(held);
                const uint32_t slot = render::textureDescriptor(held.value());
                const render::MeshHandle mesh = ecs::read<scenes::LoadedMesh>(*scene.world, model)->handle;

                // New bytes for each: drawn anew through the same handles.
                test::library[4] = test::pixelTexture(0, 0, 255, 255);
                test::library[2] = test::quadModel({.baseColor = {0, 0, 1, 1}});
                test::library[8] = test::materialFile(shader, layout.index, redFlat);
                for (const assets::AssetId& id : {texture, colored, flat})
                {
                    Result<bool> reloaded = render::reloadRenderAsset(id);
                    REQUIRE(reloaded);
                    CHECK(reloaded.value());
                }
                scene.render(surface.value());
                CHECK(bluest(pixel(-2.5f)));
                CHECK(bluest(pixel(0)));
                CHECK(reddest(pixel(2.5f)));
                CHECK(render::textureDescriptor(held.value()) == slot); // the same slot, read by the material
                CHECK(render::meshData(mesh) != nullptr);
                CHECK(ecs::read<scenes::LoadedMesh>(*scene.world, model)->handle.generation == mesh.generation);
                render::releaseTexture(held.value());

                // The shader again: its pipelines made anew, its materials kept.
                Result<bool> again = render::reloadRenderAsset(shader);
                REQUIRE(again);
                CHECK(again.value());
                scene.render(surface.value());
                CHECK(reddest(pixel(2.5f)));

                // Failures leave the old versions drawing.
                test::library[2] = {1, 2, 3};
                test::library[7] = {1, 2, 3};
                CHECK(render::reloadRenderAsset(colored).status() == Status::InvalidData);
                CHECK(render::reloadRenderAsset(shader).status() == Status::InvalidData);
                test::library.erase(4);
                CHECK(render::reloadRenderAsset(texture).status() == Status::NotFound);
                scene.render(surface.value());
                CHECK(bluest(pixel(-2.5f)));
                CHECK(bluest(pixel(0)));
                CHECK(reddest(pixel(2.5f)));

                // Not loaded: nothing to do.
                Result<bool> unloaded = render::reloadRenderAsset(test::idOf(42));
                REQUIRE(unloaded);
                CHECK_FALSE(unloaded.value());
            }
            render::destroySurface(surface.value());
            render::shutdown();
            test::library.clear();
        }
        platform::shutdown();
        assets::setAssetReader(0);
    }
}
