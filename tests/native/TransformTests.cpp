// Transforms and cameras (ADR 0050): world-matrix propagation down the
// hierarchy, the camera the renderer chooses, and its ViewConstants reaching
// shaders on every backend.

#include "RenderInternal.h"

#include <crucible/Camera.h>
#include <crucible/Ecs.h>
#include <crucible/Jobs.h>
#include <crucible/Platform.h>
#include <crucible/Render.h>
#include <crucible/Shader.h>
#include <crucible/Systems.h>
#include <crucible/Transform.h>

#include <doctest/doctest.h>
#include <slag/Slag.h>
#include <slag/rendergraph/RenderGraph.h>

#include <chrono>
#include <cmath>
#include <cstring>
#include <fstream>
#include <iterator>
#include <string>
#include <vector>

using namespace crucible;
using namespace crucible::ecs;
using crucible::render::Camera;
using crucible::render::CameraProjection;
using crucible::scenes::LocalTransform;
using crucible::scenes::WorldTransform;

namespace {

constexpr float pi = 3.14159265f;

bool near(float a, float b)
{
    return std::fabs(a - b) <= 1e-4f * std::fmax(1.0f, std::fabs(b));
}

bool near(const Matrix4x4& a, const Matrix4x4& b)
{
    for (int row = 0; row < 4; ++row)
    {
        for (int column = 0; column < 4; ++column)
        {
            if (!near(a.rows[row][column], b.rows[row][column]))
            {
                return false;
            }
        }
    }
    return true;
}

struct WorldScope
{
    World* world = createWorld();
    ~WorldScope()
    {
        destroyWorld(world);
    }
    World& operator*()
    {
        return *world;
    }
};

// The local matrix the slow way: scale, then rotation, then translation.
Matrix4x4 trs(const LocalTransform& local)
{
    return Matrix4x4::scale(local.scale) * Matrix4x4::rotation(local.rotation) * Matrix4x4::translation(local.position);
}

LocalTransform transform(Vector3 position, float yaw = 0, Vector3 scale = {1, 1, 1})
{
    return {position, Quaternion::fromYawPitchRoll(yaw, 0, 0), scale};
}

Entity spawn(World& world, const LocalTransform& local, Entity parent = nullEntity)
{
    const ComponentId both[] = {componentIdOf<LocalTransform>, componentIdOf<WorldTransform>};
    const Entity entity = createEntity(world, both);
    *get<LocalTransform>(world, entity) = local;
    if (parent != nullEntity)
    {
        REQUIRE(setParent(world, entity, parent));
    }
    return entity;
}

const Matrix4x4& worldOf(World& world, Entity entity)
{
    return read<WorldTransform>(world, entity)->matrix;
}

// Runs the propagation system as the loop would: with the version of its
// last run (0: first), applying its commands, then advancing the version.
// Returns the version it ran at, for the next run.
uint32_t runPropagation(World& world, uint32_t lastRunVersion = 0)
{
    CommandBuffer* commands = createCommandBuffer(world);
    const loop::SystemContext context{.world = &world, .commands = commands, .lastRunVersion = lastRunVersion};
    reinterpret_cast<loop::SystemFunction>(scenes::transformPropagationFunction())(context, nullptr);
    apply(*commands);
    destroyCommandBuffer(commands);
    const uint32_t ran = currentVersion(world);
    advanceVersion(world);
    return ran;
}

bool worldChangedSince(World& world, Entity entity, uint32_t version)
{
    return changedSince(world, entity, componentIdOf<WorldTransform>, version);
}

} // namespace

TEST_CASE("World transforms compose down the hierarchy")
{
    scenes::registerTransforms();
    REQUIRE(jobs::initialize(3));
    {
        WorldScope world;
        const LocalTransform rootLocal = transform({10, 0, 0}, pi / 2, {2, 2, 2});
        const LocalTransform childLocal = transform({0, 0, 1}, pi / 4);
        const LocalTransform grandchildLocal = transform({1, 2, 3}, 0, {1, 3, 1});
        const Entity root = spawn(*world, rootLocal);
        const Entity child = spawn(*world, childLocal, root);
        const Entity grandchild = spawn(*world, grandchildLocal, child);

        // Under a parent without transforms: placed as a root.
        const Entity plain = createEntity(*world);
        const Entity orphan = spawn(*world, transform({5, 5, 5}), plain);

        // As a system would: remember a version, then start a newer one.
        const uint32_t before = advanceVersion(*world);
        advanceVersion(*world);
        runPropagation(*world);

        CHECK(near(worldOf(*world, root), trs(rootLocal)));
        CHECK(near(worldOf(*world, child), trs(childLocal) * trs(rootLocal)));
        CHECK(near(worldOf(*world, grandchild), trs(grandchildLocal) * trs(childLocal) * trs(rootLocal)));
        CHECK(near(worldOf(*world, orphan), Matrix4x4::translation({5, 5, 5})));
        // The child, scaled by its parent, sits 2 m along the root's +Z
        // turned 90 degrees: +X, from (10, 0, 0).
        const Vector3 childPosition{worldOf(*world, child).rows[3].x, worldOf(*world, child).rows[3].y,
                                    worldOf(*world, child).rows[3].z};
        CHECK(near(childPosition.x, 12.0f));
        CHECK(near(childPosition.z, 0.0f));

        // The columns were written: change detection sees the update.
        const ComponentId worldOnly[] = {componentIdOf<WorldTransform>};
        Query* query = createQuery(*world, {.required = worldOnly});
        struct Seen
        {
            uint32_t version;
            bool changed = true;
        } seen{before};
        forEachChunk(
            *query,
            [](const ChunkView& view, void* data) {
                auto& s = *static_cast<Seen*>(data);
                s.changed = s.changed && changedSince(view, componentIdOf<WorldTransform>, s.version);
            },
            &seen);
        CHECK(seen.changed);
        destroyQuery(query);

        // Moving the root moves the subtree, on demand too.
        get<LocalTransform>(*world, root)->position = {0, 0, 0};
        scenes::updateWorldTransforms(*world);
        CHECK(near(worldOf(*world, grandchild),
                   trs(grandchildLocal) * trs(childLocal) * trs(transform({0, 0, 0}, pi / 2, {2, 2, 2}))));
    }
    jobs::shutdown();
}

TEST_CASE("A child without transforms passes nothing on and its children are roots")
{
    scenes::registerTransforms();
    WorldScope world;
    const Entity root = spawn(*world, transform({1, 0, 0}));
    const Entity middle = createEntity(*world);
    REQUIRE(setParent(*world, middle, root));
    const Entity below = spawn(*world, transform({0, 1, 0}), middle);
    scenes::updateWorldTransforms(*world);
    CHECK(near(worldOf(*world, below), Matrix4x4::translation({0, 1, 0})));
}

TEST_CASE("Entities with a LocalTransform get a placed WorldTransform")
{
    scenes::registerTransforms();
    WorldScope world;
    const Entity parent = spawn(*world, transform({0, 10, 0}));
    const ComponentId localOnly[] = {componentIdOf<LocalTransform>};
    const Entity child = createEntity(*world, localOnly);
    *get<LocalTransform>(*world, child) = transform({1, 0, 0});
    REQUIRE(setParent(*world, child, parent));

    // The on-demand update records nothing; the system adds the component.
    scenes::updateWorldTransforms(*world);
    CHECK_FALSE(hasComponent(*world, child, componentIdOf<WorldTransform>));
    runPropagation(*world);
    REQUIRE(hasComponent(*world, child, componentIdOf<WorldTransform>));
    CHECK(near(worldOf(*world, child), Matrix4x4::translation({1, 10, 0})));
}

TEST_CASE("Deep and wide hierarchies propagate in parallel like a serial walk")
{
    scenes::registerTransforms();
    REQUIRE(jobs::initialize(4));
    {
        WorldScope world;
        std::vector<Entity> entities;
        std::vector<Entity> parents;
        // 200 chains of 20, each link turned and offset.
        for (int chain = 0; chain < 200; ++chain)
        {
            Entity parent = nullEntity;
            for (int link = 0; link < 20; ++link)
            {
                parent = spawn(*world, transform({0.1f * link, 0, 1}, 0.05f * chain), parent);
                entities.push_back(parent);
                parents.push_back(parentOf(*world, parent));
            }
        }
        runPropagation(*world);

        uint32_t wrong = 0;
        for (size_t i = 0; i < entities.size(); ++i)
        {
            Matrix4x4 expected = trs(*read<LocalTransform>(*world, entities[i]));
            if (parents[i] != nullEntity)
            {
                expected = expected * worldOf(*world, parents[i]);
            }
            wrong += near(worldOf(*world, entities[i]), expected) ? 0 : 1;
        }
        CHECK(wrong == 0);
    }
    jobs::shutdown();
}

TEST_CASE("Propagation recomputes only what moved since its last run")
{
    scenes::registerTransforms();
    render::registerCamera();
    REQUIRE(jobs::initialize(3));
    {
        WorldScope world;
        const Entity mover = spawn(*world, transform({1, 0, 0}));
        const Entity rider = spawn(*world, transform({0, 1, 0}), mover);
        const Entity passenger = spawn(*world, transform({0, 0, 1}), rider);
        // Elsewhere: an archetype of its own, so its chunk stays untouched.
        const ComponentId tagged[] = {componentIdOf<LocalTransform>, componentIdOf<WorldTransform>,
                                      componentIdOf<Camera>};
        const Entity still = createEntity(*world, tagged);
        *get<LocalTransform>(*world, still) = transform({7, 7, 7});

        uint32_t last = runPropagation(*world);
        CHECK(near(worldOf(*world, passenger), Matrix4x4::translation({1, 1, 1})));

        // Nothing changed: nothing written.
        last = runPropagation(*world, last);
        CHECK_FALSE(worldChangedSince(*world, mover, last - 1));
        CHECK_FALSE(worldChangedSince(*world, passenger, last - 1));

        // Moving the root reaches its descendants, whose own chunks are clean.
        get<LocalTransform>(*world, mover)->position = {2, 0, 0};
        const uint32_t before = last;
        last = runPropagation(*world, last);
        CHECK(near(worldOf(*world, passenger), Matrix4x4::translation({2, 1, 1})));
        CHECK(worldChangedSince(*world, passenger, before));
        CHECK_FALSE(worldChangedSince(*world, still, before));
        CHECK(near(worldOf(*world, still), Matrix4x4::translation({7, 7, 7})));

        // A middle entity moving takes the one below with it.
        get<LocalTransform>(*world, rider)->position = {0, 3, 0};
        last = runPropagation(*world, last);
        CHECK(near(worldOf(*world, passenger), Matrix4x4::translation({2, 3, 1})));

        // Reparenting is a change too.
        REQUIRE(setParent(*world, passenger, still));
        last = runPropagation(*world, last);
        CHECK(near(worldOf(*world, passenger), Matrix4x4::translation({7, 7, 8})));

        // A parent losing its transforms leaves its children as roots.
        removeComponent(*world, still, componentIdOf<LocalTransform>);
        last = runPropagation(*world, last);
        CHECK(near(worldOf(*world, passenger), Matrix4x4::translation({0, 0, 1})));

        // Rewriting the same local transform writes no new world matrix.
        *get<LocalTransform>(*world, mover) = *read<LocalTransform>(*world, mover);
        const uint32_t rewritten = last;
        last = runPropagation(*world, last);
        CHECK_FALSE(worldChangedSince(*world, mover, rewritten));
    }
    jobs::shutdown();
}

TEST_CASE("computeWorldTransform is fresh without storing anything")
{
    scenes::registerTransforms();
    WorldScope world;
    const Entity root = spawn(*world, transform({1, 0, 0}, pi / 2));
    const Entity child = spawn(*world, transform({0, 0, 2}, 0, {2, 2, 2}), root);
    const Entity grandchild = spawn(*world, transform({0, 1, 0}), child);
    scenes::updateWorldTransforms(*world);
    CHECK(near(scenes::computeWorldTransform(*world, grandchild), worldOf(*world, grandchild)));

    get<LocalTransform>(*world, root)->position = {5, 0, 0};
    const Matrix4x4 stored = worldOf(*world, grandchild);
    const Matrix4x4 now = scenes::computeWorldTransform(*world, grandchild);
    CHECK(near(worldOf(*world, grandchild), stored)); // not written
    scenes::updateWorldTransforms(*world);
    CHECK(near(now, worldOf(*world, grandchild)));

    CHECK(near(scenes::computeWorldTransform(*world, createEntity(*world)), Matrix4x4::identity()));
}

TEST_CASE("The renderer views from the camera with the highest priority")
{
    scenes::registerTransforms();
    render::registerCamera();
    WorldScope world;

    // No world, or no camera: the default view at the origin.
    const render::ViewConstants fallback = render::viewConstants(nullptr, 200, 100, 1.5f);
    CHECK(near(fallback.view, Matrix4x4::identity()));
    CHECK(fallback.time == 1.5f);
    CHECK(fallback.viewportSize.x == 200.0f);

    const ComponentId set[] = {componentIdOf<LocalTransform>, componentIdOf<WorldTransform>, componentIdOf<Camera>};
    const Camera perspective{
        .projection = CameraProjection::Perspective, .fieldOfViewY = pi / 2, .nearPlane = 0.5f, .priority = 1};
    const Entity low = createEntity(*world, set);
    *get<Camera>(*world,
                 low) = {.projection = CameraProjection::Perspective, .fieldOfViewY = 1, .nearPlane = 1, .priority = 0};
    *get<LocalTransform>(*world, low) = transform({0, 0, 0});
    const Entity first = createEntity(*world, set);
    *get<Camera>(*world, first) = perspective;
    // Scaled: the view ignores scale.
    *get<LocalTransform>(*world, first) = transform({0, 0, -5}, 0, {3, 3, 3});
    const Entity second = createEntity(*world, set);
    *get<Camera>(*world, second) = perspective; // same priority, higher index
    *get<LocalTransform>(*world, second) = transform({100, 0, 0});
    scenes::updateWorldTransforms(*world);

    const render::ViewConstants view = render::viewConstants(world.world, 200, 100, 0);
    CHECK(near(view.cameraPosition.z, -5.0f));
    CHECK(near(view.view, Matrix4x4::translation({0, 0, 5})));
    // A point on the axis at the near plane is at depth 1, farther ones less.
    const Vector4 atNear = transform(Vector4{0, 0, -4.5f, 1}, view.viewProjection);
    CHECK(near(atNear.z / atNear.w, 1.0f));
    const Vector4 ahead = transform(Vector4{0, 0, 5, 1}, view.viewProjection);
    CHECK(near(ahead.z / ahead.w, 0.05f));
    // 90 degrees high on a 2:1 surface: 45 degrees up is the top edge.
    const Vector4 top = transform(Vector4{0, 10, 5, 1}, view.viewProjection);
    CHECK(near(top.y / top.w, 1.0f));
    CHECK(near(view.inverseViewProjection, [&] {
        Matrix4x4 inverse{};
        (void)invert(view.viewProjection, inverse);
        return inverse;
    }()));

    // Orthographic: the full height across the view, depth 1 to 0 from near
    // to far.
    *get<Camera>(*world, first) = {.projection = CameraProjection::Orthographic,
                                   .orthographicHeight = 4,
                                   .nearPlane = 1,
                                   .farPlane = 11,
                                   .priority = 2};
    const render::ViewConstants ortho = render::viewConstants(world.world, 200, 100, 0);
    CHECK(near(transformPoint({4, 2, -4}, ortho.viewProjection).x, 1.0f));
    CHECK(near(transformPoint({4, 2, -4}, ortho.viewProjection).y, 1.0f));
    CHECK(near(transformPoint({0, 0, -4}, ortho.viewProjection).z, 1.0f));
    CHECK(near(transformPoint({0, 0, 6}, ortho.viewProjection).z, 0.0f));
}

namespace {

std::vector<uint8_t> readFile(const std::string& path)
{
    std::ifstream file(path, std::ios::binary);
    return {std::istreambuf_iterator<char>(file), std::istreambuf_iterator<char>()};
}

// A step copying the frame's ViewConstants, as a shader sees them through the
// root constant buffer, into a readback buffer.
struct ViewCopy
{
    render::ShaderFile* shader = nullptr;
    slag::Pipeline* pipeline = nullptr;
    slag::Buffer* output = nullptr;
    slag::Buffer* readback = nullptr;
};

constexpr uint32_t outputSlot = 900; // among the render steps' first 1024
constexpr uint32_t viewSize = (sizeof(render::ViewConstants) + 255) / 256 * 256;

Result<void> prepareViewCopy(slag::GraphicsDevice& device, void* userData)
{
    auto& copy = *static_cast<ViewCopy*>(userData);
    slag::ShaderStage stage;
    render::shaderStage(*copy.shader, 0, stage);
    copy.pipeline = device.createComputePipeline({.compute = stage, .debugName = "Test.ViewCopy"}).value();
    copy.output = device
                      .createBuffer({.size = sizeof(render::ViewConstants),
                                     .placement = slag::Placement::automatic(slag::MemoryKind::GpuOnly)})
                      .value();
    copy.readback = device
                        .createBuffer({.size = sizeof(render::ViewConstants),
                                       .placement = slag::Placement::automatic(slag::MemoryKind::Readback)})
                        .value();
    device.writeRWStructuredBuffer(outputSlot,
                                   {.buffer = copy.output, .count = 1, .stride = sizeof(render::ViewConstants)});
    return copy.pipeline != nullptr && copy.output != nullptr && copy.readback != nullptr
               ? Result<void>()
               : Result<void>(Status::Unsupported);
}

void releaseViewCopy(slag::GraphicsDevice& device, void* userData)
{
    auto& copy = *static_cast<ViewCopy*>(userData);
    device.destroy(copy.pipeline);
    device.destroy(copy.output);
    device.destroy(copy.readback);
}

void addViewCopy(render::RenderStepContext& context, void* userData)
{
    auto* copy = static_cast<ViewCopy*>(userData);
    const slag::rendergraph::RenderGraphBuffer view = context.buffer(render::viewKey());
    const slag::rendergraph::RenderGraphBuffer output = context.graph.importBuffer(copy->output);
    const slag::rendergraph::RenderGraphBuffer readback = context.graph.importBuffer(copy->readback);
    context.graph.addPass(
        "Test.ViewCopy", slag::CommandQueueType::Graphics,
        [&](slag::rendergraph::PassBuilder& pass) {
            pass.use(view, slag::rendergraph::Usage::ConstantBuffer);
            pass.use(output, slag::rendergraph::Usage::ShaderWrite);
        },
        [copy, view](slag::rendergraph::PassContext& pass, slag::CommandBuffer* commands) {
            commands->bindPipeline(copy->pipeline);
            commands->setConstantBuffer(pass.buffer(view)->gpuAddress(), viewSize);
            commands->pushConstants(&outputSlot, sizeof(outputSlot));
            commands->dispatch(1, 1, 1);
        });
    context.graph.addPass(
        "Test.ViewReadback", slag::CommandQueueType::Graphics,
        [&](slag::rendergraph::PassBuilder& pass) {
            pass.use(output, slag::rendergraph::Usage::CopySource);
            pass.use(readback, slag::rendergraph::Usage::CopyDest);
        },
        [copy](slag::rendergraph::PassContext&, slag::CommandBuffer* commands) {
            commands->copyBuffer(copy->readback, 0, copy->output, 0, sizeof(render::ViewConstants));
        });
}

} // namespace

TEST_SUITE("gpu")
{
    TEST_CASE("Shaders read the camera's ViewConstants on every backend")
    {
        scenes::registerTransforms();
        render::registerCamera();
        const std::vector<uint8_t> bytes = readFile(CRUCIBLE_SHADER_DIR "/ViewProbe.crshader");
        Result<render::ShaderFile*> shader = render::loadShaderFile(bytes);
        REQUIRE(shader);
        REQUIRE(platform::initialize());
        for (slag::Backend backend : {slag::Backend::D3D12, slag::Backend::Vulkan})
        {
            if (!slag::isBackendAvailable(backend) || !render::initialize(backend))
            {
                continue;
            }
            CAPTURE(static_cast<int>(backend));
            Result<render::SurfaceHandle> surface = render::createOffscreenSurface(64, 32);
            REQUIRE(surface);
            ViewCopy copy{.shader = shader.value()};
            REQUIRE(render::renderSchedule().add({.name = "Test.ViewCopy",
                                                  .addPasses = addViewCopy,
                                                  .userData = &copy,
                                                  .prepare = prepareViewCopy,
                                                  .release = releaseViewCopy}));
            {
                WorldScope world;
                const ComponentId set[] = {componentIdOf<LocalTransform>, componentIdOf<WorldTransform>,
                                           componentIdOf<Camera>};
                const Entity camera = createEntity(*world, set);
                *get<Camera>(*world, camera) = {
                    .projection = CameraProjection::Perspective, .fieldOfViewY = 1, .nearPlane = 0.25f};
                // Three frames, so every frame slot's buffer is used.
                for (int frame = 0; frame < 3; ++frame)
                {
                    CAPTURE(frame);
                    *get<LocalTransform>(*world, camera) = transform({1, 2, static_cast<float>(frame)}, 0.3f);
                    scenes::updateWorldTransforms(*world);
                    REQUIRE(render::renderFrame(surface.value(), world.world, 0, 0, 0, 1));
                    REQUIRE(render::renderQueue()->waitIdle());
                    render::ViewConstants seen{};
                    std::memcpy(&seen, copy.readback->mappedData(), sizeof(seen));
                    const render::ViewConstants expected = render::viewConstants(world.world, 64, 32, seen.time);
                    CHECK(near(seen.view, expected.view));
                    CHECK(near(seen.viewProjection, expected.viewProjection));
                    CHECK(near(seen.inverseViewProjection, expected.inverseViewProjection));
                    CHECK(near(seen.cameraPosition.z, static_cast<float>(frame)));
                    CHECK(seen.viewportSize.x == 64.0f);
                    CHECK(seen.viewportSize.y == 32.0f);
                    CHECK(seen.time > 0.0f);
                }
            }
            REQUIRE(render::renderSchedule().remove("Test.ViewCopy"));
            render::destroySurface(surface.value());
            render::shutdown();
        }
        platform::shutdown();
        render::destroyShaderFile(shader.value());
    }
}

TEST_SUITE("benchmark" * doctest::skip())
{
    TEST_CASE("Transforms: propagate 100k entities")
    {
        scenes::registerTransforms();
        REQUIRE(jobs::initialize(0));
        {
            WorldScope world;
            // 10k roots with a chain of 9 below each: a typical mix of
            // static props and small rigs.
            for (int root = 0; root < 10'000; ++root)
            {
                Entity parent = spawn(*world, transform({static_cast<float>(root), 0, 0}, 0.01f * root));
                for (int link = 0; link < 9; ++link)
                {
                    parent = spawn(*world, transform({0, 1, 0}, 0.1f), parent);
                }
            }
            std::vector<Entity> roots;
            const ComponentId rootSet[] = {componentIdOf<LocalTransform>, componentIdOf<WorldTransform>};
            Query* rootQuery = createQuery(*world, {.required = rootSet, .excluded = {}});
            forEachChunk(
                *rootQuery,
                [](const ChunkView& view, void* data) {
                    if (readColumn(view, parentComponent()) == nullptr)
                    {
                        auto& list = *static_cast<std::vector<Entity>*>(data);
                        list.insert(list.end(), entitiesOf(view), entitiesOf(view) + entityCountOf(view));
                    }
                },
                &roots);
            destroyQuery(rootQuery);

            // Each frame as the system sees it: `move` roots moved since its
            // last run (a root and its chain: 10 entities each).
            uint32_t last = runPropagation(*world);
            auto time = [&](size_t move) {
                double best = 1e30;
                for (int run = 0; run < 20; ++run)
                {
                    for (size_t i = 0; i < move; ++i)
                    {
                        get<LocalTransform>(*world, roots[(i * 97 + run) % roots.size()])->position.y += 1;
                    }
                    const auto start = std::chrono::steady_clock::now();
                    CommandBuffer* commands = createCommandBuffer(*world);
                    const loop::SystemContext context{
                        .world = world.world, .commands = commands, .lastRunVersion = last};
                    reinterpret_cast<loop::SystemFunction>(scenes::transformPropagationFunction())(context, nullptr);
                    best = std::min(
                        best,
                        std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - start).count());
                    destroyCommandBuffer(commands);
                    last = currentVersion(*world);
                    advanceVersion(*world);
                }
                return best;
            };
            const double none = time(0);
            const double some = time(100);
            const double all = time(roots.size());
            MESSAGE("propagate 100k entities: nothing moved " << none << " ms, 1% moved " << some << " ms, all moved "
                                                              << all << " ms (" << jobs::workerCount() << " workers)");
        }
        jobs::shutdown();
    }
}
