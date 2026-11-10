// The editor's view (ADR 0067): a surface's own camera, an offscreen
// surface's image as a texture, resized; and the editor's steps, Grid, Pick,
// and SelectionOutline, read back on every backend.

#include "RenderInternal.h"
#include "TestModels.h"

#include <crucible/Camera.h>
#include <crucible/Ecs.h>
#include <crucible/MeshInstance.h>
#include <crucible/Platform.h>
#include <crucible/Render.h>
#include <crucible/RenderCommands.h>
#include <crucible/Transform.h>

#include <doctest/doctest.h>
#include <slag/Slag.h>

#include <vector>

using namespace crucible;
using namespace crucible::ecs;

namespace {

using test::Bgra;
using test::library;
using test::pixelOf;
using test::readPixels;
using test::readTestAsset;
using test::Scene;

bool isRed(Bgra pixel)
{
    return pixel.r > 100 && pixel.g < pixel.r / 3 && pixel.b < pixel.r / 3;
}

// The outline's orange, sRGB-encoded.
bool isOrange(Bgra pixel)
{
    return pixel.r > 200 && pixel.g > 100 && pixel.g < 210 && pixel.b < 80;
}

int count(const std::vector<uint8_t>& pixels, bool (*test)(Bgra))
{
    int found = 0;
    for (size_t at = 0; at + 3 < pixels.size(); at += 4)
    {
        found += test({pixels[at], pixels[at + 1], pixels[at + 2], pixels[at + 3]}) ? 1 : 0;
    }
    return found;
}

int differing(const std::vector<uint8_t>& a, const std::vector<uint8_t>& b)
{
    int found = 0;
    for (size_t at = 0; at < a.size() && at < b.size(); at += 4)
    {
        found += a[at] != b[at] || a[at + 1] != b[at + 1] || a[at + 2] != b[at + 2] ? 1 : 0;
    }
    return found;
}

// Frames until the pick asked for comes back (the GPU finishes a frame or two later).
render::PickResult pick(Scene& scene, render::SurfaceHandle surface, uint32_t x, uint32_t y)
{
    const uint64_t asked = render::framesSubmitted(surface) + 1;
    render::requestPick(surface, x, y);
    for (int frame = 0; frame < 8; ++frame)
    {
        scene.render(surface);
        const render::PickResult result = render::pickResult(surface);
        if (result.frame >= asked)
        {
            return result;
        }
    }
    (void)render::renderQueue()->waitIdle();
    return render::pickResult(surface);
}

} // namespace

TEST_SUITE("gpu")
{
    TEST_CASE("The editor's view has its own camera and picks and outlines and draws the grid")
    {
        scenes::registerTransforms();
        scenes::registerMeshInstance();
        render::registerCamera();
        render::registerLights();
        const assets::AssetId redQuad = test::idOf(11);
        library[11] = test::quadModel({.baseColor = {1, 0, 0, 1}});
        assets::setAssetReader(reinterpret_cast<uint64_t>(&readTestAsset));
        REQUIRE(platform::initialize());
        for (slag::Backend backend : {slag::Backend::D3D12, slag::Backend::Vulkan})
        {
            if (!slag::isBackendAvailable(backend) || !render::initialize(backend))
            {
                continue;
            }
            CAPTURE(static_cast<int>(backend));
            REQUIRE(render::addEditorSteps(0));
            constexpr uint32_t width = 128, height = 64;
            Result<render::SurfaceHandle> created = render::createOffscreenSurface(width, height);
            REQUIRE(created);
            const render::SurfaceHandle surface = created.value();
            {
                Scene scene;
                scene.defaultLighting();
                // No camera in the world: the surface's own looks down +Z from z = -10.
                const render::Camera camera{
                    .projection = render::CameraProjection::Perspective, .fieldOfViewY = 0.8f, .nearPlane = 0.1f};
                REQUIRE(render::setSurfaceCamera(surface, {0, 0, -10}, {0, 0, 0, 1}, camera));
                const Entity left = scene.quad(redQuad, {-3, 0, 0}, 2);
                const Entity right = scene.quad(redQuad, {3, 0, 0}, 2);
                scene.render(surface);
                std::vector<uint8_t> pixels = readPixels(surface, width, height);
                const render::CameraOverride own{Matrix4x4::translation({0, 0, -10}), camera};
                const render::ViewConstants view = render::viewConstants(scene.world, width, height, 0, &own);
                CHECK(isRed(pixelOf(pixels, view, {-3, 0, 0})));

                // Picking: the entity drawn at a pixel, and nothing where nothing is.
                const Vector4 clip = transform(Vector4{-3, 0, 0, 1}, view.viewProjection);
                const auto x = static_cast<uint32_t>((clip.x / clip.w + 1) * 0.5f * width);
                const auto y = static_cast<uint32_t>((1 - clip.y / clip.w) * 0.5f * height);
                render::PickResult picked = pick(scene, surface, x, y);
                CHECK(picked.hit);
                CHECK(picked.entity == left);
                picked = pick(scene, surface, 1, 1);
                CHECK_FALSE(picked.hit);
                CHECK(picked.frame != 0);
                // Its depth (ADR 0071): reverse-Z, near / distance, 10 m away.
                picked = pick(scene, surface, x, y);
                CHECK(picked.depth == doctest::Approx(0.1f / 10).epsilon(0.02));
                CHECK(pick(scene, surface, 1, 1).depth == 0.0f);
                // Excluded, it's looked through; the other quad, in the same batch, isn't.
                render::setSurfacePickExclusion(surface, std::span<const Entity>(&left, 1));
                CHECK_FALSE(pick(scene, surface, x, y).hit);
                const Vector4 rightClip = transform(Vector4{3, 0, 0, 1}, view.viewProjection);
                picked = pick(scene, surface, static_cast<uint32_t>((rightClip.x / rightClip.w + 1) * 0.5f * width),
                              static_cast<uint32_t>((1 - rightClip.y / rightClip.w) * 0.5f * height));
                CHECK(picked.entity == right);
                render::setSurfacePickExclusion(surface, {});
                CHECK(pick(scene, surface, x, y).entity == left);

                // The selection's outline, orange, only while something's selected.
                CHECK(count(pixels, isOrange) == 0);
                render::setSurfaceSelection(surface, std::span<const Entity>(&left, 1));
                scene.render(surface);
                pixels = readPixels(surface, width, height);
                CHECK(count(pixels, isOrange) > 10);
                CHECK(isRed(pixelOf(pixels, view, {-3, 0, 0}))); // the inside isn't painted
                render::setSurfaceSelection(surface, {});

                // The grid on the ground, seen from above it.
                REQUIRE(render::setSurfaceCamera(surface, {0, 3, -10}, {0, 0, 0, 1}, camera));
                scene.render(surface);
                const std::vector<uint8_t> without = readPixels(surface, width, height);
                render::setSurfaceGrid(surface, true);
                scene.render(surface);
                const std::vector<uint8_t> with = readPixels(surface, width, height);
                CHECK(differing(without, with) > 50);
                render::setSurfaceGrid(surface, false);

                // The image as a texture, kept through a resize.
                Result<render::TextureHandle> texture = render::surfaceTexture(surface);
                REQUIRE(texture);
                CHECK(render::textureSize(texture.value()).width == width);
                scene.render(surface);
                REQUIRE(render::resizeOffscreenSurface(surface, 96, 48));
                CHECK(render::surfaceTexture(surface).value().index == texture.value().index);
                CHECK(render::textureSize(texture.value()).width == 96);
                CHECK(render::textureSize(texture.value()).height == 48);
                scene.render(surface);
                pixels = readPixels(surface, 96, 48);
                CHECK(count(pixels, isRed) > 10);
                CHECK(render::resizeOffscreenSurface(surface, 0, 48).status() == Status::InvalidData);
                render::clearSurfaceCamera(surface);
            }
            render::destroySurface(surface);
            for (const char* step : {"Grid", "Pick", "SelectionOutline"})
            {
                REQUIRE(render::removeStep(0, step));
            }
            render::shutdown();
        }
        platform::shutdown();
    }
}
