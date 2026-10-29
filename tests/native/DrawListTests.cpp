// Drawing meshes (ADR 0051): mesh instances resolved, culled, batched, and
// drawn through DepthPrepass, Opaque, and Resolve, read back on every
// backend; world matrices sent only when they change; models released when
// unused; MSAA settings.

#include "RenderInternal.h"

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

std::vector<uint8_t> fileBytes(binary::Writer* writer)
{
    Vector<std::byte> bytes(MemoryTag::General);
    REQUIRE(binary::writeToMemory(*writer, binary::Compression::Fast, bytes));
    binary::destroyWriter(writer);
    const auto* data = reinterpret_cast<const uint8_t*>(bytes.data());
    return {data, data + bytes.size()};
}

template <class T>
void addSection(binary::Writer* writer, const char (&tag)[5], const std::vector<T>& values)
{
    REQUIRE(binary::addSection(*writer, binary::tag(tag), 1,
                               {reinterpret_cast<const uint8_t*>(values.data()), values.size() * sizeof(T)}, 16));
}

assets::AssetId idOf(uint8_t first)
{
    assets::AssetId id{};
    id.bytes[0] = first;
    id.bytes[15] = 0x5A;
    return id;
}

// A unit quad in the XY plane facing -Z (clockwise from a camera looking
// along +Z), with half-float UVs and one material.
std::vector<uint8_t> quadModel(const float (&baseColor)[4], const assets::AssetId* texture)
{
    binary::Writer* writer = binary::createWriter(assets::meshFileKind, assets::meshFileVersion);
    addSection(writer, "INFO", std::vector<assets::MeshFileInfo>{{1, 1, 1, 4, 4, 0}});
    assets::MeshEntry entry{};
    entry.vertexCount = 4;
    entry.indexSize = 2;
    entry.attributes = 4; // UV0
    entry.lodCount = 1;
    entry.submeshCount = 1;
    const float boundsMin[3] = {-0.5f, -0.5f, 0}, boundsMax[3] = {0.5f, 0.5f, 0};
    std::memcpy(entry.boundsMin, boundsMin, sizeof(boundsMin));
    std::memcpy(entry.boundsMax, boundsMax, sizeof(boundsMax));
    addSection(writer, "MSHS", std::vector<assets::MeshEntry>{entry});
    addSection(writer, "LODS", std::vector<assets::MeshLod>{{0, 0.0f}});
    addSection(writer, "RNGS", std::vector<assets::MeshRange>{{0, 6, 0}});
    addSection(writer, "POSN", std::vector<float>{-0.5f, -0.5f, 0, -0.5f, 0.5f, 0, 0.5f, 0.5f, 0, 0.5f, -0.5f, 0});
    // Half floats: 0 and 1 (0x3c00).
    addSection(writer, "UV0_", std::vector<uint16_t>{0, 0x3c00, 0, 0, 0x3c00, 0, 0x3c00, 0x3c00});
    addSection(writer, "INDX", std::vector<uint16_t>{0, 1, 2, 0, 2, 3});
    assets::MeshNode node{};
    node.parent = -1;
    node.mesh = 0;
    addSection(writer, "NODE", std::vector<assets::MeshNode>{node});
    assets::MaterialEntry material{};
    std::memcpy(material.baseColor, baseColor, sizeof(baseColor));
    if (texture != nullptr)
    {
        std::memcpy(material.baseColorTexture, texture->bytes, 16);
    }
    addSection(writer, "MATL", std::vector<assets::MaterialEntry>{material});
    return fileBytes(writer);
}

// A 1x1 RGBA8 texture, not sRGB.
std::vector<uint8_t> pixelTexture(uint8_t r, uint8_t g, uint8_t b)
{
    binary::Writer* writer = binary::createWriter(assets::textureFileKind, assets::textureFileVersion);
    addSection(writer, "INFO", std::vector<assets::TextureInfo>{{assets::TextureFormat::Rgba8, 1, 1, 1, 0}});
    const std::vector<uint8_t> texel = {r, g, b, 255};
    REQUIRE(binary::addSection(*writer, assets::textureMipTag(0), 1, texel, 16));
    return fileBytes(writer);
}

std::map<uint8_t, std::vector<uint8_t>> library;

int32_t readTestAsset(const assets::AssetId* id, void* context,
                      void (*deliver)(void* context, const uint8_t* data, uint64_t size))
{
    auto found = library.find(id->bytes[0]);
    if (found == library.end())
    {
        return -1;
    }
    deliver(context, found->second.data(), found->second.size());
    return 0;
}

// Gives an entity an archetype (so a chunk) of its own.
struct Moving
{};

struct Bgra
{
    uint8_t b, g, r, a;
};

struct Scene
{
    World* world = createWorld();
    uint32_t lastRun = 0;

    ~Scene()
    {
        destroyWorld(world);
    }

    Entity quad(const assets::AssetId& model, Vector3 position, float scale = 1)
    {
        const ComponentId set[] = {componentIdOf<MeshInstance>, componentIdOf<LocalTransform>,
                                   componentIdOf<WorldTransform>};
        const Entity entity = createEntity(*world, set);
        *get<MeshInstance>(*world, entity) = {model, 0};
        *get<LocalTransform>(*world, entity) = {position, {0, 0, 0, 1}, {scale, scale, scale}};
        return entity;
    }

    // One frame as the loop runs it: propagation, then the render system
    // with its last-run version, then a newer version.
    void render(render::SurfaceHandle surface)
    {
        scenes::updateWorldTransforms(*world);
        REQUIRE(render::renderFrame(surface, world, lastRun, 0, 0, 0, 1));
        lastRun = currentVersion(*world);
        advanceVersion(*world);
    }
};

std::vector<uint8_t> readPixels(render::SurfaceHandle surface, uint32_t width, uint32_t height)
{
    std::vector<uint8_t> pixels(width * height * 4);
    REQUIRE(render::readSurfacePixels(surface, pixels));
    return pixels;
}

// The pixel where a world position lands.
Bgra pixelOf(const std::vector<uint8_t>& pixels, const render::ViewConstants& view, Vector3 position)
{
    const Vector4 clip = transform(Vector4{position.x, position.y, position.z, 1}, view.viewProjection);
    const auto x = static_cast<uint32_t>((clip.x / clip.w + 1) * 0.5f * view.viewportSize.x);
    const auto y = static_cast<uint32_t>((1 - clip.y / clip.w) * 0.5f * view.viewportSize.y);
    const size_t at = (size_t{y} * static_cast<uint32_t>(view.viewportSize.x) + x) * 4;
    return {pixels[at], pixels[at + 1], pixels[at + 2], pixels[at + 3]};
}

bool isColor(Bgra pixel, uint8_t r, uint8_t g, uint8_t b)
{
    auto close = [](uint8_t value, uint8_t want) {
        return value + 2 >= want && value <= want + 2;
    };
    return close(pixel.r, r) && close(pixel.g, g) && close(pixel.b, b);
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
        const float red[4] = {1, 0, 0, 1}, white[4] = {1, 1, 1, 1}, green[4] = {0, 1, 0, 1};
        const assets::AssetId redQuad = idOf(1), texturedQuad = idOf(2), blueTexture = idOf(3), greenQuad = idOf(4);
        library[1] = quadModel(red, nullptr);
        library[2] = quadModel(white, &blueTexture);
        library[3] = pixelTexture(0, 0, 255);
        library[4] = quadModel(green, nullptr);
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
                CHECK(isColor(pixelOf(pixels, view, {-3, 0, 0}), 255, 0, 0));
                CHECK(isColor(pixelOf(pixels, view, {3, 0, 0}), 255, 0, 0));
                CHECK(isColor(pixelOf(pixels, view, {0, 0, 0}), 0, 0, 255)); // white x blue texture
                CHECK(isColor(pixelOf(pixels, view, {0, 2, 0}), 0, 0, 0));   // the clear color
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
                CHECK(isColor(pixelOf(pixels, view, {3, 1.5f, 0}), 255, 0, 0));
                CHECK(isColor(pixelOf(pixels, view, {3, -0.8f, 0}), 0, 0, 0));
                // And the other slot's copy catches up the next frame.
                scene.render(surface.value());
                pixels = readPixels(surface.value(), width, height);
                CHECK(isColor(pixelOf(pixels, view, {3, 1.5f, 0}), 255, 0, 0));

                // Forced full uploads send everything.
                render::forceFullTransformUploads(true);
                scene.render(surface.value());
                CHECK(render::drawStats().transformsSent == 5);
                render::forceFullTransformUploads(false);

                // Without MSAA the frame is the same where it's flat.
                REQUIRE(render::setMsaaSamples(1));
                scene.render(surface.value());
                pixels = readPixels(surface.value(), width, height);
                CHECK(isColor(pixelOf(pixels, view, {-3, 0, 0}), 255, 0, 0));
                CHECK(isColor(pixelOf(pixels, view, {0, 0, 0}), 0, 0, 255));
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
        const float red[4] = {1, 0, 0, 1};
        library[1] = quadModel(red, nullptr);
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
                const Entity entity =
                    scene.quad(idOf(1), {static_cast<float>(i % 400) - 200, static_cast<float>(i / 400) - 125, 0});
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
