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

// Gives an entity an archetype (so a chunk) of its own.
struct Moving
{};

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
        registerComponent<Moving>("Test.Moving");
        const assets::AssetId redQuad = test::idOf(1), texturedQuad = test::idOf(2), blueTexture = test::idOf(3),
                              greenQuad = test::idOf(4);
        library[1] = test::quadModel({.baseColor = {1, 0, 0, 1}});
        library[2] = test::quadModel({.texture = &blueTexture});
        library[3] = test::pixelTexture(0, 0, 255, 255);
        library[4] = test::quadModel({.baseColor = {0, 1, 0, 1}});
        render::setAssetReader(reinterpret_cast<uint64_t>(&readTestAsset));
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
                addComponent(*scene.world, right, componentIdOf<Moving>);
                scene.quad(texturedQuad, {0, 0, 0}, 2);
                scene.quad(greenQuad, {-3, 0, 2}, 1.5f);
                const Entity far = scene.quad(redQuad, {500, 0, 0});

                // The first frame resolves the instances and draws them.
                scene.render(surface.value());
                const render::DrawStats first = render::drawStats();
                CHECK(first.instances == 5);
                CHECK(first.visible == 4);
                CHECK(first.transformsSent == 5);
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

                // Nothing moved: no world matrix is sent again (both frame
                // slots' copies are already up to date after two frames).
                scene.render(surface.value());
                scene.render(surface.value());
                CHECK(render::drawStats().transformsSent == 0);
                CHECK(render::drawStats().visible == 4);

                // One moves: only its chunk is sent (it's alone there;
                // versions are per chunk, so chunk-mates would go too), and
                // it's drawn where it moved.
                get<LocalTransform>(*scene.world, right)->position = {3, 1.5f, 0};
                scene.render(surface.value());
                CHECK(render::drawStats().transformsSent == 1);
                pixels = readPixels(surface.value(), width, height);
                CHECK(isRed(pixelOf(pixels, view, {3, 1.5f, 0})));
                CHECK(isBlack(pixelOf(pixels, view, {3, -0.8f, 0})));
                // And the other slot's copy catches up the next frame.
                scene.render(surface.value());
                pixels = readPixels(surface.value(), width, height);
                CHECK(isRed(pixelOf(pixels, view, {3, 1.5f, 0})));

                // Forced full uploads send everything.
                render::forceFullTransformUploads(true);
                scene.render(surface.value());
                CHECK(render::drawStats().transformsSent == 5);
                render::forceFullTransformUploads(false);

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
        render::setAssetReader(0);
        library.clear();
    }
}

// By hand: crucible_native_tests --no-skip --test-suite=benchmark -tc="Drawing*"
// Whether incremental propagation and transform uploads pay off (docs/open-items.md):
// 100k mesh instances, 1% moving each frame, either scattered among the
// static ones or in an archetype of their own, timed incremental and full.
TEST_SUITE("benchmark" * doctest::skip())
{
    TEST_CASE("Drawing: incremental against full transform work")
    {
        scenes::registerTransforms();
        scenes::registerMeshInstance();
        render::registerCamera();
        registerComponent<Moving>("Test.Moving");
        library[1] = test::quadModel({.baseColor = {1, 0, 0, 1}});
        render::setAssetReader(reinterpret_cast<uint64_t>(&readTestAsset));
        REQUIRE(jobs::initialize(0));
        REQUIRE(platform::initialize());
        REQUIRE(render::initialize(slag::InstanceDesc{}.backend));
        Result<render::SurfaceHandle> surface = render::createOffscreenSurface(1280, 720);
        REQUIRE(surface);

        for (bool grouped : {false, true})
        {
            Scene scene;
            const ComponentId cameraSet[] = {componentIdOf<LocalTransform>, componentIdOf<WorldTransform>,
                                             componentIdOf<Camera>};
            const Entity camera = createEntity(*scene.world, cameraSet);
            *get<LocalTransform>(*scene.world, camera) = {{0, 0, -150}, {0, 0, 0, 1}, {1, 1, 1}};
            *get<Camera>(*scene.world, camera) = {
                .projection = CameraProjection::Perspective, .fieldOfViewY = 1.0f, .nearPlane = 0.1f};
            std::vector<Entity> movers;
            for (int i = 0; i < 100'000; ++i)
            {
                const Entity entity = scene.quad(
                    test::idOf(1), {static_cast<float>(i % 400) - 200, static_cast<float>(i / 400) - 125, 0});
                if (i % 100 == 0)
                {
                    movers.push_back(entity);
                    if (grouped)
                    {
                        addComponent(*scene.world, entity, componentIdOf<Moving>);
                    }
                }
            }
            scene.render(surface.value());
            scene.render(surface.value());

            auto time = [&](bool full) {
                render::forceFullTransformUploads(full);
                double propagate = 1e30, draw = 1e30;
                uint32_t propagationLast = currentVersion(*scene.world);
                for (int frame = 0; frame < 30; ++frame)
                {
                    for (Entity mover : movers)
                    {
                        get<LocalTransform>(*scene.world, mover)->position.z = 0.01f * (frame % 2);
                    }
                    // Propagation as the system runs it: since its last run, or everything.
                    CommandBuffer* commands = createCommandBuffer(*scene.world);
                    const loop::SystemContext context{
                        .world = scene.world, .commands = commands, .lastRunVersion = full ? 0 : propagationLast};
                    auto start = std::chrono::steady_clock::now();
                    reinterpret_cast<loop::SystemFunction>(scenes::transformPropagationFunction())(context, nullptr);
                    propagate = std::min(
                        propagate,
                        std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - start).count());
                    destroyCommandBuffer(commands);
                    propagationLast = currentVersion(*scene.world);
                    advanceVersion(*scene.world);

                    // The draw list's CPU time: renderFrame also waits for the GPU.
                    REQUIRE(render::renderFrame(surface.value(), scene.world, scene.lastRun, 0, 0, 0, 1));
                    draw = std::min(draw, static_cast<double>(render::drawStats().cpuNs) / 1e6);
                    scene.lastRun = currentVersion(*scene.world);
                    advanceVersion(*scene.world);
                }
                const std::string label =
                    std::string(grouped ? "grouped, " : "scattered, ") + (full ? "full:        " : "incremental: ");
                MESSAGE(label << "propagate " << propagate << " ms, draw list " << draw << " ms, matrices sent "
                              << render::drawStats().transformsSent << ", visible " << render::drawStats().visible);
            };
            for (int round = 0; round < 3; ++round)
            {
                time(round % 2 != 0);
                time(round % 2 == 0);
            }
            render::forceFullTransformUploads(false);
        }
        render::destroySurface(surface.value());
        render::shutdown();
        platform::shutdown();
        jobs::shutdown();
        render::setAssetReader(0);
        library.clear();
    }
}
