// Drawing meshes (ADR 0051): mesh instances resolved, culled, batched, and
// drawn through DepthPrepass, Opaque, and Resolve, read back on every
// backend; world matrices sent only when they change; models released when
// unused; MSAA settings.

#include "RenderInternal.h"
#include "TestModels.h"

#include <crucible/BinaryFile.h>
#include <crucible/Camera.h>
#include <crucible/Ecs.h>
#include <crucible/Jobs.h>
#include <crucible/MeshFile.h>
#include <crucible/MeshInstance.h>
#include <crucible/Platform.h>
#include <crucible/Render.h>
#include <crucible/Systems.h>
#include <crucible/TextureFile.h>
#include <crucible/Transform.h>

#include <doctest/doctest.h>
#include <slag/Slag.h>

#include <chrono>
#include <cstring>
#include <map>
#include <string>
#include <thread>
#include <vector>

using namespace crucible;
using namespace crucible::ecs;
using crucible::render::Camera;
using crucible::render::CameraProjection;
using crucible::scenes::LoadedMesh;
using crucible::scenes::LocalTransform;
using crucible::scenes::MeshInstance;
using crucible::scenes::WorldTransform;

namespace {

using test::Bgra;
using test::library;
using test::pixelOf;
using test::readPixels;
using test::readTestAsset;
using test::Scene;

// Lit, so only the hue is checked: the channel is bright, the others dark.
bool isRed(Bgra pixel)
{
    return pixel.r > 100 && pixel.g < pixel.r / 3 && pixel.b < pixel.r / 3;
}

bool isBlue(Bgra pixel)
{
    return pixel.b > 100 && pixel.r < pixel.b / 3 && pixel.g < pixel.b / 3;
}

bool isBlack(Bgra pixel)
{
    return pixel.r < 3 && pixel.g < 3 && pixel.b < 3;
}

} // namespace

TEST_CASE("Mesh instances register and draw lists start empty without a renderer")
{
    scenes::registerMeshInstance();
    CHECK(componentIdOf<MeshInstance> != invalidComponent);
    CHECK(componentIdOf<LoadedMesh> != invalidComponent);
    CHECK(render::setMsaaSamples(3).status() == Status::Unsupported);
    CHECK(render::msaaSamples() == 4);
}

TEST_SUITE("gpu")
{
    TEST_CASE("Mesh instances draw culled and batched on every backend")
    {
        scenes::registerTransforms();
        scenes::registerMeshInstance();
        render::registerCamera();
        render::registerLights();
        const assets::AssetId redQuad = test::idOf(1), texturedQuad = test::idOf(2), blueTexture = test::idOf(3),
                              greenQuad = test::idOf(4);
        library[1] = test::quadModel({.baseColor = {1, 0, 0, 1}});
        library[2] = test::quadModel({.texture = &blueTexture});
        library[3] = test::pixelTexture(0, 0, 255, 255);
        library[4] = test::quadModel({.baseColor = {0, 1, 0, 1}});
        assets::setAssetReader(reinterpret_cast<uint64_t>(&readTestAsset));
        REQUIRE(platform::initialize());
        for (slag::Backend backend : {slag::Backend::D3D12, slag::Backend::Vulkan})
        {
            if (!slag::isBackendAvailable(backend) || !render::initialize(backend))
            {
                continue;
            }
            CAPTURE(static_cast<int>(backend));
            constexpr uint32_t width = 128, height = 64;
            Result<render::SurfaceHandle> surface = render::createOffscreenSurface(width, height);
            REQUIRE(surface);
            {
                Scene scene;
                scene.defaultLighting();
                const ComponentId cameraSet[] = {componentIdOf<LocalTransform>, componentIdOf<WorldTransform>,
                                                 componentIdOf<Camera>};
                const Entity camera = createEntity(*scene.world, cameraSet);
                *get<LocalTransform>(*scene.world, camera) = {{0, 0, -10}, {0, 0, 0, 1}, {1, 1, 1}};
                *get<Camera>(*scene.world, camera) = {
                    .projection = CameraProjection::Perspective, .fieldOfViewY = 0.8f, .nearPlane = 0.1f};
                // Two red quads (one batch), a textured one, a green one
                // hidden behind the first, and one far outside the view.
                const Entity left = scene.quad(redQuad, {-3, 0, 0}, 2);
                const Entity right = scene.quad(redQuad, {3, 0, 0}, 2);
                scene.quad(texturedQuad, {0, 0, 0}, 2);
                scene.quad(greenQuad, {-3, 0, 2}, 1.5f);
                const Entity far = scene.quad(redQuad, {500, 0, 0});

                // The first frame resolves the instances and draws them.
                scene.render(surface.value());
                const render::DrawStats first = render::drawStats();
                CHECK(first.instances == 5);
                CHECK(first.visible == 4);
                CHECK(first.cachedModels == 3);
                std::vector<uint8_t> pixels = readPixels(surface.value(), width, height);
                const render::ViewConstants view = render::viewConstants(scene.world, width, height, 0);
                CHECK(isRed(pixelOf(pixels, view, {-3, 0, 0})));
                CHECK(isRed(pixelOf(pixels, view, {3, 0, 0})));
                CHECK(isBlue(pixelOf(pixels, view, {0, 0, 0})));  // white x blue texture
                CHECK(isBlack(pixelOf(pixels, view, {0, 2, 0}))); // the clear color
                const LoadedMesh* loaded = read<LoadedMesh>(*scene.world, left);
                REQUIRE(loaded != nullptr);
                const render::MeshHandle redHandle = loaded->handle;
                CHECK(render::meshData(redHandle) != nullptr);

                scene.render(surface.value());
                scene.render(surface.value());
                CHECK(render::drawStats().visible == 4);

                // One moves: it's drawn where it moved, in both frame slots.
                get<LocalTransform>(*scene.world, right)->position = {3, 1.5f, 0};
                scene.render(surface.value());
                pixels = readPixels(surface.value(), width, height);
                CHECK(isRed(pixelOf(pixels, view, {3, 1.5f, 0})));
                CHECK(isBlack(pixelOf(pixels, view, {3, -0.8f, 0})));
                scene.render(surface.value());
                pixels = readPixels(surface.value(), width, height);
                CHECK(isRed(pixelOf(pixels, view, {3, 1.5f, 0})));

                // Without MSAA the frame is the same where it's flat.
                REQUIRE(render::setMsaaSamples(1));
                scene.render(surface.value());
                pixels = readPixels(surface.value(), width, height);
                CHECK(isRed(pixelOf(pixels, view, {-3, 0, 0})));
                CHECK(isBlue(pixelOf(pixels, view, {0, 0, 0})));
                REQUIRE(render::setMsaaSamples(4));

                // Once nothing uses a model, it's released after a while.
                destroyEntity(*scene.world, left);
                destroyEntity(*scene.world, right);
                destroyEntity(*scene.world, far);
                std::this_thread::sleep_for(std::chrono::milliseconds(2100));
                scene.render(surface.value());
                CHECK(render::drawStats().cachedModels == 2);
                CHECK(render::meshData(redHandle) == nullptr);
            }
            render::destroySurface(surface.value());
            render::shutdown();
        }
        platform::shutdown();
        assets::setAssetReader(0);
        library.clear();
    }
}
