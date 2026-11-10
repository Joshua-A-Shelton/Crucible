// Culling skinned instances (ADR 0080): by their posed bounds, from each
// joint's bind-pose reach, not by where the mesh was bound.

#include "RenderInternal.h"
#include "TestModels.h"

#include <crucible/Animation.h>
#include <crucible/Camera.h>
#include <crucible/Ecs.h>
#include <crucible/Platform.h>
#include <crucible/Render.h>
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

TEST_SUITE("gpu")
{
    TEST_CASE("Skinned instances are culled by their posed bounds")
    {
        scenes::registerTransforms();
        scenes::registerMeshInstance();
        render::registerCamera();
        render::registerLights();
        animation::registerAnimation();
        // Bound at the origin, out of view; its Arm rests 20 m up, in view.
        test::library[1] = test::quadModel({.baseColor = {1, 0, 0, 1}, .skinned = true, .armRest = {0, 20, 0}});
        assets::setAssetReader(reinterpret_cast<uint64_t>(&test::readTestAsset));
        REQUIRE(platform::initialize());
        for (slag::Backend backend : {slag::Backend::D3D12, slag::Backend::Vulkan})
        {
            if (!slag::isBackendAvailable(backend) || !render::initialize(backend))
            {
                continue;
            }
            CAPTURE(static_cast<int>(backend));
            Result<render::SurfaceHandle> surface = render::createOffscreenSurface(64, 64);
            REQUIRE(surface);
            {
                test::Scene scene;
                const ComponentId cameraSet[] = {componentIdOf<LocalTransform>, componentIdOf<WorldTransform>,
                                                 componentIdOf<Camera>};
                const Entity camera = createEntity(*scene.world, cameraSet);
                *get<LocalTransform>(*scene.world, camera) = {{0, 20, -10}, {0, 0, 0, 1}, {1, 1, 1}};
                *get<Camera>(*scene.world, camera) = {
                    .projection = CameraProjection::Perspective, .fieldOfViewY = 0.8f, .nearPlane = 0.1f};
                const ComponentId rootSet[] = {componentIdOf<LocalTransform>, componentIdOf<WorldTransform>,
                                               componentIdOf<animation::Animator>};
                const Entity root = createEntity(*scene.world, rootSet);
                *get<LocalTransform>(*scene.world, root) = {{0, 0, 0}, {0, 0, 0, 1}, {1, 1, 1}};
                *get<animation::Animator>(*scene.world, root) = {test::idOf(1), {}, 1, true};
                const ComponentId meshSet[] = {componentIdOf<MeshInstance>, componentIdOf<LocalTransform>,
                                               componentIdOf<WorldTransform>};
                const Entity mesh = createEntity(*scene.world, meshSet);
                *get<MeshInstance>(*scene.world, mesh) = {test::idOf(1), 0};
                *get<LocalTransform>(*scene.world, mesh) = {{0, 0, 0}, {0, 0, 0, 1}, {1, 1, 1}};
                REQUIRE(setParent(*scene.world, mesh, root));

                // Unposed, it's culled by where it was bound: out of view.
                scene.render(surface.value());
                scene.render(surface.value());
                CHECK(render::drawStats().visible == 0);
                // Posed, its right edge is in view: drawn, though the rest
                // of it is far below.
                animation::evaluatePoses(*scene.world);
                scene.render(surface.value());
                CHECK(render::drawStats().visible == 1);
                // Moved away with its character, culled again.
                get<LocalTransform>(*scene.world, root)->position = {100, 0, 0};
                animation::evaluatePoses(*scene.world);
                scene.render(surface.value());
                CHECK(render::drawStats().visible == 0);
                animation::releasePoses(*scene.world);
            }
            render::destroySurface(surface.value());
            render::shutdown();
        }
        platform::shutdown();
        assets::setAssetReader(0);
        test::library.clear();
    }
}
