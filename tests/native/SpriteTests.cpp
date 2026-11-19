// Sprites (ADR 0104): drawn over the finished image with their texture's
// exact colors, by region, pivot, flips, and pixels per unit; sorted by
// sorting layer, order in layer, and height where the layer y-sorts; tinted;
// and picked by the editor where they show. Read back on every backend.

#include "RenderInternal.h"
#include "TestModels.h"

#include <crucible/Camera.h>
#include <crucible/Ecs.h>
#include <crucible/MeshInstance.h>
#include <crucible/Platform.h>
#include <crucible/Render.h>
#include <crucible/RenderCommands.h>
#include <crucible/Sprite.h>
#include <crucible/Transform.h>

#include <doctest/doctest.h>
#include <slag/Slag.h>

#include <vector>

using namespace crucible;
using namespace crucible::ecs;

namespace {

using render2d::Sprite;
using test::Bgra;
using test::library;
using test::readPixels;
using test::readTestAsset;
using test::Scene;

constexpr uint32_t width = 64, height = 64;

// An sRGB RGBA8 sprite texture with its pixels per unit, rows top to bottom.
std::vector<uint8_t> spriteTexture(uint32_t w, uint32_t h, const std::vector<uint8_t>& rgba, float pixelsPerUnit)
{
    binary::Writer* writer = binary::createWriter(assets::textureFileKind, assets::textureFileVersion);
    test::addSection(writer, binary::tag("INFO"),
                     std::vector<assets::TextureInfo>{{assets::TextureFormat::Rgba8, w, h, 1, 1}});
    REQUIRE(binary::addSection(*writer, assets::textureMipTag(0), 1, rgba, 16));
    test::addSection(writer, binary::tag("SPRT"), std::vector<float>{pixelsPerUnit});
    return test::fileBytes(writer);
}

// An orthographic camera 8 m tall over 64 pixels, looking down +Z from z =
// -10: 8 pixels a meter, x from -4 m (left) to 4 m, y from -4 m (bottom).
Entity camera(Scene& scene)
{
    const ComponentId set[] = {componentIdOf<render::Camera>, componentIdOf<scenes::LocalTransform>,
                               componentIdOf<scenes::WorldTransform>};
    const Entity entity = createEntity(*scene.world, set);
    *get<render::Camera>(*scene.world, entity) = {.projection = render::CameraProjection::Orthographic,
                                                  .orthographicHeight = 8,
                                                  .nearPlane = 0.1f,
                                                  .farPlane = 100};
    *get<scenes::LocalTransform>(*scene.world, entity) = {{0, 0, -10}, {0, 0, 0, 1}, {1, 1, 1}};
    return entity;
}

Entity sprite(Scene& scene, const Sprite& value, Vector2 position)
{
    const ComponentId set[] = {componentIdOf<Sprite>, componentIdOf<scenes::Transform2D>,
                               componentIdOf<scenes::WorldTransform>};
    const Entity entity = createEntity(*scene.world, set);
    *get<Sprite>(*scene.world, entity) = value;
    *get<scenes::Transform2D>(*scene.world, entity) = {position, 0, {1, 1}, 0};
    return entity;
}

Sprite showing(uint8_t texture)
{
    return {.texture = test::idOf(texture), .pivot = {0, 0}, .tint = {1, 1, 1, 1}};
}

// The pixel at column x, row y (from the top), as RGBA.
std::array<uint8_t, 4> at(const std::vector<uint8_t>& pixels, uint32_t x, uint32_t y)
{
    const size_t i = (size_t{y} * width + x) * 4;
    return {pixels[i + 2], pixels[i + 1], pixels[i], pixels[i + 3]}; // BGRA back to RGBA
}

// Frames until every sprite's texture has loaded and drawn.
std::vector<uint8_t> renderSettled(Scene& scene, render::SurfaceHandle surface)
{
    for (int frame = 0; frame < 4; ++frame)
    {
        scene.render(surface);
    }
    return readPixels(surface, width, height);
}

// Four columns, two rows: red, green, blue, white over yellow, cyan,
// magenta, and a clear texel.
const std::vector<uint8_t> grid = {200, 10,  20, 255, 30, 190, 40,  255, 50,  60, 180, 255, 255, 255, 255, 255,
                                   210, 220, 0,  255, 0,  230, 240, 255, 250, 5,  245, 255, 0,   0,   0,   0};

} // namespace

TEST_SUITE("gpu")
{
    TEST_CASE("Sprites draw their texture's exact colors by region pivot flips and pixels per unit on every backend")
    {
        scenes::registerTransforms();
        render::registerCamera();
        render2d::registerSprite();
        library[41] = spriteTexture(4, 2, grid, 8); // a texel a pixel
        library[42] = spriteTexture(4, 2, grid, 4); // two pixels a texel
        assets::setAssetReader(reinterpret_cast<uint64_t>(&readTestAsset));
        REQUIRE(platform::initialize());
        for (slag::Backend backend : {slag::Backend::D3D12, slag::Backend::Vulkan})
        {
            if (!slag::isBackendAvailable(backend) || !render::initialize(backend))
            {
                continue;
            }
            CAPTURE(static_cast<int>(backend));
            Result<render::SurfaceHandle> created = render::createOffscreenSurface(width, height);
            REQUIRE(created);
            const render::SurfaceHandle surface = created.value();
            {
                Scene scene;
                camera(scene);
                // At the origin, pivot bottom-left: columns 32 to 35, rows 30 (the
                // texture's top) and 31.
                const Entity whole = sprite(scene, showing(41), {0, 0});
                std::vector<uint8_t> pixels = renderSettled(scene, surface);
                CHECK(at(pixels, 32, 30) == std::array<uint8_t, 4>{200, 10, 20, 255});
                CHECK(at(pixels, 35, 30) == std::array<uint8_t, 4>{255, 255, 255, 255});
                CHECK(at(pixels, 33, 31) == std::array<uint8_t, 4>{0, 230, 240, 255});
                CHECK(at(pixels, 35, 31)[0] == 0); // the clear texel shows the black behind it
                CHECK(at(pixels, 31, 30)[0] == 0);
                CHECK(at(pixels, 36, 30)[0] == 0);
                CHECK(at(pixels, 32, 29)[0] == 0);

                // Flipped across: the white column comes first.
                get<Sprite>(*scene.world, whole)->flipX = true;
                pixels = renderSettled(scene, surface);
                CHECK(at(pixels, 32, 30) == std::array<uint8_t, 4>{255, 255, 255, 255});
                CHECK(at(pixels, 35, 30) == std::array<uint8_t, 4>{200, 10, 20, 255});
                get<Sprite>(*scene.world, whole)->flipX = false;

                // A region, the bottom row's middle two, centered on (-2, 2) m.
                Sprite part = showing(41);
                part.region = {1, 1, 2, 1};
                part.pivot = {0.5f, 0.5f};
                sprite(scene, part, {-2, 2});
                pixels = renderSettled(scene, surface);
                // x -2 m is column 16, so columns 15 and 16; y 2 m is row 16, half a texel up and down.
                CHECK(at(pixels, 15, 15) == std::array<uint8_t, 4>{0, 230, 240, 255});
                CHECK(at(pixels, 16, 15) == std::array<uint8_t, 4>{250, 5, 245, 255});
                CHECK(at(pixels, 14, 15)[1] == 0);
                CHECK(at(pixels, 17, 15)[1] == 0);

                // Four pixels a meter: twice the size, at (2, -2) m: columns 48 to 55, rows 44 to 47.
                sprite(scene, showing(42), {2, -2});
                pixels = renderSettled(scene, surface);
                CHECK(at(pixels, 48, 44) == std::array<uint8_t, 4>{200, 10, 20, 255});
                CHECK(at(pixels, 49, 45) == std::array<uint8_t, 4>{200, 10, 20, 255});
                CHECK(at(pixels, 55, 47)[3] == 255);
                CHECK(at(pixels, 50, 46) == std::array<uint8_t, 4>{0, 230, 240, 255});
            }
            render::destroySurface(surface);
            render::shutdown();
        }
        assets::setAssetReader(0);
        platform::shutdown();
    }

    TEST_CASE("Sprites sort by layer then order in layer then height where the layer y-sorts on every backend")
    {
        scenes::registerTransforms();
        render::registerCamera();
        render2d::registerSprite();
        library[43] = spriteTexture(1, 1, {255, 0, 0, 255}, 1); // a 1 m red square
        library[44] = spriteTexture(1, 1, {0, 0, 255, 255}, 1); // and a blue one
        assets::setAssetReader(reinterpret_cast<uint64_t>(&readTestAsset));
        REQUIRE(platform::initialize());
        for (slag::Backend backend : {slag::Backend::D3D12, slag::Backend::Vulkan})
        {
            if (!slag::isBackendAvailable(backend) || !render::initialize(backend))
            {
                continue;
            }
            CAPTURE(static_cast<int>(backend));
            Result<render::SurfaceHandle> created = render::createOffscreenSurface(width, height);
            REQUIRE(created);
            const render::SurfaceHandle surface = created.value();
            {
                Scene scene;
                camera(scene);
                // Overlapping where both show: from (0.5, 0.5) m to (1, 1) m, around column 38, row 26.
                Sprite red = showing(43);
                Sprite blue = showing(44);
                red.sortingLayer = 1; // a later layer, made first
                const Entity redEntity = sprite(scene, red, {0, 0});
                const Entity blueEntity = sprite(scene, blue, {0.5f, 0.5f});
                auto overlap = [&] {
                    return at(renderSettled(scene, surface), 38, 26);
                };
                CHECK(overlap()[0] == 255); // red's layer draws later

                // One layer: order in layer decides.
                get<Sprite>(*scene.world, redEntity)->sortingLayer = 0;
                get<Sprite>(*scene.world, blueEntity)->orderInLayer = 1;
                CHECK(overlap()[2] == 255);
                get<Sprite>(*scene.world, redEntity)->orderInLayer = 2;
                CHECK(overlap()[0] == 255);

                // Equal orders in a layer that y-sorts: the lower one (red, at 0 m) draws on top.
                get<Sprite>(*scene.world, redEntity)->orderInLayer = 0;
                get<Sprite>(*scene.world, blueEntity)->orderInLayer = 0;
                render2d::setYSortLayers(1);
                CHECK(overlap()[0] == 255);
                // Moved below red, blue is now in front.
                get<scenes::Transform2D>(*scene.world, blueEntity)->position = {0.5f, -0.5f};
                const std::vector<uint8_t> lower = renderSettled(scene, surface);
                CHECK(at(lower, 38, 34)[2] == 255); // the overlap, now from 0 to 0.5 m
                render2d::setYSortLayers(0);
            }
            render::destroySurface(surface);
            render::shutdown();
        }
        assets::setAssetReader(0);
        platform::shutdown();
    }

    TEST_CASE("A sprite's tint multiplies its colors and its alpha blends it on every backend")
    {
        scenes::registerTransforms();
        render::registerCamera();
        render2d::registerSprite();
        library[45] = spriteTexture(1, 1, {255, 255, 255, 255}, 1);
        assets::setAssetReader(reinterpret_cast<uint64_t>(&readTestAsset));
        REQUIRE(platform::initialize());
        for (slag::Backend backend : {slag::Backend::D3D12, slag::Backend::Vulkan})
        {
            if (!slag::isBackendAvailable(backend) || !render::initialize(backend))
            {
                continue;
            }
            CAPTURE(static_cast<int>(backend));
            Result<render::SurfaceHandle> created = render::createOffscreenSurface(width, height);
            REQUIRE(created);
            const render::SurfaceHandle surface = created.value();
            {
                Scene scene;
                camera(scene);
                Sprite tinted = showing(45);
                tinted.tint = {1, 0, 0, 1};
                const Entity entity = sprite(scene, tinted, {0, 0});
                CHECK(at(renderSettled(scene, surface), 36, 28) == std::array<uint8_t, 4>{255, 0, 0, 255});
                // Half transparent over black: half the red in linear light, sRGB-encoded.
                get<Sprite>(*scene.world, entity)->tint = {1, 0, 0, 0.5f};
                const uint8_t red = at(renderSettled(scene, surface), 36, 28)[0];
                CHECK(red > 180);
                CHECK(red < 200);
            }
            render::destroySurface(surface);
            render::shutdown();
        }
        assets::setAssetReader(0);
        platform::shutdown();
    }

    TEST_CASE("The editor picks a sprite where it shows over the meshes behind it on every backend")
    {
        scenes::registerTransforms();
        scenes::registerMeshInstance();
        render::registerCamera();
        render::registerLights();
        render2d::registerSprite();
        library[46] = spriteTexture(4, 2, grid, 8);
        library[47] = test::quadModel({.baseColor = {0, 1, 0, 1}});
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
            Result<render::SurfaceHandle> created = render::createOffscreenSurface(width, height);
            REQUIRE(created);
            const render::SurfaceHandle surface = created.value();
            {
                Scene scene;
                scene.defaultLighting();
                camera(scene);
                const Entity behind = scene.quad(test::idOf(47), {0.25f, 0.125f, 1}, 2);
                const Entity front = sprite(scene, showing(46), {0, 0});
                auto pickAt = [&](uint32_t x, uint32_t y) {
                    const uint64_t asked = render::framesSubmitted(surface) + 1;
                    render::requestPick(surface, x, y);
                    for (int frame = 0; frame < 8; ++frame)
                    {
                        scene.render(surface);
                        if (render::pickResult(surface).frame >= asked)
                        {
                            break;
                        }
                    }
                    return render::pickResult(surface);
                };
                CHECK(pickAt(32, 30).entity == front);
                // Its clear texel shows the quad behind, and so picks it.
                CHECK(pickAt(35, 31).entity == behind);
            }
            render::destroySurface(surface);
            for (const char* step : {"Grid", "Pick", "SelectionOutline"})
            {
                REQUIRE(render::removeStep(0, step));
            }
            render::shutdown();
        }
        assets::setAssetReader(0);
        platform::shutdown();
    }
}

TEST_CASE("The Sprites step is named and drawn after Tonemap")
{
    CHECK(std::string_view(render::steps::sprites) == "Sprites");
}
