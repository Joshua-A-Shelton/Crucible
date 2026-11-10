// Compute pre-skinning (ADR 0080): a skinned quad posed by its Animator,
// drawn where skinning on the CPU puts its vertices, on every backend.

#include "RenderInternal.h"
#include "TestModels.h"

#include <crucible/Animation.h>
#include <crucible/Camera.h>
#include <crucible/Ecs.h>
#include <crucible/Platform.h>
#include <crucible/Render.h>
#include <crucible/RenderSchedule.h>
#include <crucible/Transform.h>

#include <doctest/doctest.h>
#include <slag/Slag.h>

using namespace crucible;
using namespace crucible::ecs;
using crucible::render::Camera;
using crucible::render::CameraProjection;
using crucible::scenes::LocalTransform;
using crucible::scenes::MeshInstance;
using crucible::scenes::WorldTransform;

namespace {

using test::Bgra;
using test::library;
using test::pixelOf;
using test::readPixels;
using test::readTestAsset;

bool isRed(Bgra pixel)
{
    return pixel.r > 100 && pixel.g < pixel.r / 3 && pixel.b < pixel.r / 3;
}

bool isBlack(Bgra pixel)
{
    return pixel.r < 3 && pixel.g < 3 && pixel.b < 3;
}

// A character: its root (the Animator, at `at`) and the skinned quad
// below it, scaled by `scale` there.
Entity character(test::Scene& scene, const assets::AssetId& model, Vector3 at, float scale = 1)
{
    const ComponentId rootSet[] = {componentIdOf<LocalTransform>, componentIdOf<WorldTransform>,
                                   componentIdOf<animation::Animator>};
    const Entity root = createEntity(*scene.world, rootSet);
    *get<LocalTransform>(*scene.world, root) = {at, {0, 0, 0, 1}, {1, 1, 1}};
    *get<animation::Animator>(*scene.world, root) = {model, {}, 1, true};
    const ComponentId meshSet[] = {componentIdOf<MeshInstance>, componentIdOf<LocalTransform>,
                                   componentIdOf<WorldTransform>};
    const Entity mesh = createEntity(*scene.world, meshSet);
    *get<MeshInstance>(*scene.world, mesh) = {model, 0};
    *get<LocalTransform>(*scene.world, mesh) = {{0, 0, 0}, {0, 0, 0, 1}, {scale, scale, scale}};
    REQUIRE(setParent(*scene.world, mesh, root));
    return root;
}

} // namespace

TEST_CASE("Skinning runs before DepthPrepass")
{
    CHECK(std::string_view(render::steps::skinning) == "Skinning");
    CHECK(render::renderSchedule().stepName(0) == "Skinning");
}

TEST_SUITE("gpu")
{
    TEST_CASE("Skinned meshes draw where their pose puts their vertices on every backend")
    {
        scenes::registerTransforms();
        scenes::registerMeshInstance();
        render::registerCamera();
        render::registerLights();
        animation::registerAnimation();
        const assets::AssetId arm = test::idOf(1), still = test::idOf(2);
        // Arm's vertices (the quad's right edge) rest 1.5 m up from where
        // they were bound; the other quad isn't skinned.
        library[1] = test::quadModel({.baseColor = {1, 0, 0, 1}, .skinned = true, .armRest = {0, 1.5f, 0}});
        library[2] = test::quadModel({.baseColor = {1, 0, 0, 1}});
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
                test::Scene scene;
                scene.defaultLighting();
                const ComponentId cameraSet[] = {componentIdOf<LocalTransform>, componentIdOf<WorldTransform>,
                                                 componentIdOf<Camera>};
                const Entity camera = createEntity(*scene.world, cameraSet);
                *get<LocalTransform>(*scene.world, camera) = {{0, 0, -10}, {0, 0, 0, 1}, {1, 1, 1}};
                *get<Camera>(*scene.world, camera) = {
                    .projection = CameraProjection::Perspective, .fieldOfViewY = 0.8f, .nearPlane = 0.1f};
                // The mesh entity is scaled by 2, which a posed skin ignores
                // (as glTF does: it follows its joints, in the Animator's
                // space): the quad spans 0.5 each way from (-3, 0), and Arm's
                // edge moves up 1.5.
                const Entity root = character(scene, arm, {-3, 0, 0}, 2);
                scene.quad(still, {3, 0, 0}, 2);

                // Before a pose, it's drawn as bound, through its own
                // transform: a square spanning 1 each way.
                scene.render(surface.value());
                scene.render(surface.value());
                const render::ViewConstants view = render::viewConstants(scene.world, width, height, 0);
                std::vector<uint8_t> pixels = readPixels(surface.value(), width, height);
                CHECK(isRed(pixelOf(pixels, view, {-3.8f, -0.8f, 0})));
                CHECK(isRed(pixelOf(pixels, view, {-2.2f, -0.8f, 0})));

                // Posed: the right edge rises, the left stays.
                animation::evaluatePoses(*scene.world);
                scene.render(surface.value());
                scene.render(surface.value());
                pixels = readPixels(surface.value(), width, height);
                CHECK(isRed(pixelOf(pixels, view, {-3.4f, 0.0f, 0})));    // left, as bound
                CHECK(isBlack(pixelOf(pixels, view, {-2.6f, -0.4f, 0}))); // right, moved up
                CHECK(isRed(pixelOf(pixels, view, {-2.6f, 1.35f, 0})));   // where it went
                CHECK(isBlack(pixelOf(pixels, view, {-3.8f, -0.8f, 0}))); // the scale, ignored
                CHECK(isRed(pixelOf(pixels, view, {3, 0, 0})));           // the unskinned one
                // Moving the character moves its skin with it.
                get<LocalTransform>(*scene.world, root)->position = {-3, -1, 0};
                animation::evaluatePoses(*scene.world);
                scene.render(surface.value());
                scene.render(surface.value());
                pixels = readPixels(surface.value(), width, height);
                CHECK(isRed(pixelOf(pixels, view, {-3.4f, -1.0f, 0})));
                CHECK(isRed(pixelOf(pixels, view, {-2.6f, 0.35f, 0})));
                CHECK(isBlack(pixelOf(pixels, view, {-2.6f, -1.4f, 0})));
                animation::releasePoses(*scene.world);
            }
            render::destroySurface(surface.value());
            render::shutdown();
        }
        platform::shutdown();
        assets::setAssetReader(0);
        library.clear();
    }
}
