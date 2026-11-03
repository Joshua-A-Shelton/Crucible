// Inverted-hull outlines (ADR 0055): drawn only for materials with a width,
// in their color, tinted by the surface as far as asked, and thinning with
// distance; on every backend.

#include "RenderInternal.h"
#include "TestModels.h"

#include <crucible/Camera.h>
#include <crucible/Ecs.h>
#include <crucible/Lights.h>
#include <crucible/Platform.h>
#include <crucible/Render.h>
#include <crucible/Sky.h>
#include <crucible/Transform.h>

#include <doctest/doctest.h>
#include <slag/Slag.h>

#include <algorithm>
#include <cmath>
#include <vector>

using namespace crucible;
using crucible::render::Camera;
using crucible::render::CameraProjection;
using crucible::scenes::LocalTransform;
using crucible::scenes::WorldTransform;
using crucible::test::Bgra;

namespace {

// A camera at z = -6 looking along +Z, white ambient light, and a gray sky
// behind everything, so dark lines show against it.
void setup(test::Scene& scene)
{
    const ecs::ComponentId set[] = {ecs::componentIdOf<LocalTransform>, ecs::componentIdOf<WorldTransform>,
                                    ecs::componentIdOf<Camera>};
    const ecs::Entity camera = ecs::createEntity(*scene.world, set);
    *ecs::get<LocalTransform>(*scene.world, camera) = {{0, 0, -6}, {0, 0, 0, 1}, {1, 1, 1}};
    *ecs::get<Camera>(*scene.world,
                      camera) = {.projection = CameraProjection::Perspective, .fieldOfViewY = 0.8f, .nearPlane = 0.1f};
    scene.ambient({1, 1, 1}, {1, 1, 1});
    const ecs::ComponentId skySet[] = {ecs::componentIdOf<render::Sky>};
    const ecs::Entity sky = ecs::createEntity(*scene.world, skySet);
    *ecs::get<render::Sky>(*scene.world, sky) = {{0.4f, 0.4f, 0.4f}, {0.4f, 0.4f, 0.4f}, {0.4f, 0.4f, 0.4f}, 1, 0, 0};
}

const Bgra& at(const std::vector<uint8_t>& pixels, uint32_t width, uint32_t x, uint32_t y)
{
    return *reinterpret_cast<const Bgra*>(pixels.data() + (size_t{y} * width + x) * 4);
}

// Along a row from a sphere's center: past the lit sphere, how many dark
// pixels before the gray sky (the outline's width), and where the sphere
// ends.
struct Edge
{
    uint32_t end; // the first pixel past the sphere
    uint32_t dark;
    uint8_t darkest; // the darkest of them
};

Edge edgeOf(const std::vector<uint8_t>& pixels, uint32_t width, uint32_t y, uint32_t x)
{
    while (x < width && at(pixels, width, x, y).g > 200)
    {
        ++x;
    }
    // Dark pixels within the next 12 (the sphere's antialiased edge comes
    // first, neither lit nor dark).
    Edge edge{x, 0, 255};
    for (uint32_t end = std::min(x + 12, width); x < end; ++x)
    {
        edge.dark += at(pixels, width, x, y).g < 60 ? 1 : 0;
        edge.darkest = std::min(edge.darkest, at(pixels, width, x, y).g);
    }
    return edge;
}

} // namespace

TEST_SUITE("gpu")
{
    TEST_CASE("Materials with an outline width draw inverted hulls on every backend")
    {
        scenes::registerTransforms();
        scenes::registerMeshInstance();
        render::registerCamera();
        render::registerLights();
        // White spheres: none, a 4-pixel black outline, and one in white
        // tinted fully by a red surface.
        test::library[1] = test::sphereModel({.baseColor = {1, 1, 1, 1}});
        test::library[2] = test::sphereModel({.baseColor = {1, 1, 1, 1}, .outlineWidth = 4});
        test::library[3] = test::sphereModel(
            {.baseColor = {1, 0.1f, 0.1f, 1}, .outlineWidth = 4, .outlineColor = {1, 1, 1}, .outlineTint = 1});
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

            SUBCASE("Only outlined materials get a line, thinner far away, tinted when asked")
            {
                test::Scene scene;
                setup(scene);
                scene.quad(test::idOf(1), {-1.5f, 0, 0}, 2);
                scene.quad(test::idOf(2), {1.5f, 0, 0}, 2);
                std::vector<uint8_t> pixels = render(scene);
                const render::ViewConstants view = render::viewConstants(scene.world, width, height, 0);
                auto pixelX = [&](Vector3 p) {
                    const Vector4 clip = transform(Vector4{p.x, p.y, p.z, 1}, view.viewProjection);
                    return static_cast<uint32_t>((clip.x / clip.w + 1) * 0.5f * width);
                };
                const uint32_t row = height / 2;
                const Edge plain = edgeOf(pixels, width, row, pixelX({-1.5f, 0, 0}));
                const Edge outlined = edgeOf(pixels, width, row, pixelX({1.5f, 0, 0}));
                CHECK(plain.dark == 0);
                CHECK(outlined.dark >= 3);
                CHECK(outlined.dark <= 5);

                // Five times as far and five times as large: the same size on
                // screen, but past outlineFadeDistance, so a thinner line.
                test::Scene far;
                setup(far);
                const float distance = 30;
                far.quad(test::idOf(2), {1.5f * distance / 6, 0, distance - 6}, 2 * distance / 6);
                pixels = render(far);
                const render::ViewConstants farView = render::viewConstants(far.world, width, height, 0);
                const Vector4 clip =
                    transform(Vector4{1.5f * distance / 6, 0, distance - 6, 1}, farView.viewProjection);
                const Edge thin =
                    edgeOf(pixels, width, row, static_cast<uint32_t>((clip.x / clip.w + 1) * 0.5f * width));
                // About 1.3 pixels: partly covered pixels, darker than the
                // sky (about 170) but not as dark as a whole pixel of line.
                CHECK(thin.dark < outlined.dark);
                CHECK(thin.darkest < 120);

                // A white outline fully tinted by a red surface: red, where the
                // line is.
                test::Scene tinted;
                setup(tinted);
                tinted.quad(test::idOf(3), {1.5f, 0, 0}, 2);
                pixels = render(tinted);
                const Bgra line = at(pixels, width, outlined.end + 1, row);
                CHECK(line.r > line.g + 80);
            }

            render::destroySurface(surface.value());
            render::shutdown();
        }
        platform::shutdown();
        render::setAssetReader(0);
        test::library.clear();
    }
}
