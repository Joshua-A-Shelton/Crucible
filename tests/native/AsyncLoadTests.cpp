// Loading on background jobs (ADR 0099), with job workers running and a
// source that holds its reads at a gate: a collider's body is made only once
// its collision data has loaded; a sound asked for while it loads starts
// once it has; an Animator holds still until its skeleton and clips have
// loaded, then plays as one that never waited; reads run off the main
// thread, unless the source isn't thread-safe. Each system's load starter
// counts what still waits. The renderer's loads are in RenderAssetsTests.cpp's
// GPU suite.

#include "AnimationTestSupport.h"
#include "AsyncLoadTestSupport.h"
#include "AudioTestSupport.h"
#include "PhysicsTestSupport.h"

#include <crucible/AssetLoads.h>
#include <crucible/Jobs.h>

using namespace crucible;
using crucible::testing::GatedSource;

namespace {

std::vector<uint8_t> meshFile(importers::MeshImport& mesh)
{
    binary::Writer* writer = binary::createWriter(assets::meshFileKind, assets::meshFileVersion);
    for (uint32_t i = 0; i < importers::meshSectionCount(mesh); ++i)
    {
        const importers::MeshSectionInfo info = importers::meshSection(mesh, i);
        std::vector<uint8_t> bytes(info.size);
        importers::copyMeshSection(mesh, i, bytes);
        REQUIRE(binary::addSection(*writer, info.tag, info.version, bytes, 16));
    }
    Vector<std::byte> out(MemoryTag::General);
    REQUIRE(binary::writeToMemory(*writer, binary::Compression::Fast, out));
    binary::destroyWriter(writer);
    const auto* data = reinterpret_cast<const uint8_t*>(out.data());
    return {data, data + out.size()};
}

std::vector<uint8_t> soundFile(const std::vector<int16_t>& samples)
{
    binary::Writer* writer = binary::createWriter(assets::soundFileKind, assets::soundFileVersion);
    const assets::SoundInfo info{
        audio::mixRate, 1, static_cast<uint32_t>(samples.size()), assets::SoundEncoding::Pcm16, 0, 4, 0};
    REQUIRE(binary::addSection(*writer, binary::tag("INFO"), 1, {reinterpret_cast<const uint8_t*>(&info), sizeof(info)},
                               16));
    REQUIRE(binary::addSection(*writer, binary::tag("PCMS"), 1,
                               {reinterpret_cast<const uint8_t*>(samples.data()), samples.size() * 2}, 16));
    Vector<std::byte> out(MemoryTag::General);
    REQUIRE(binary::writeToMemory(*writer, binary::Compression::Fast, out));
    binary::destroyWriter(writer);
    const auto* data = reinterpret_cast<const uint8_t*>(out.data());
    return {data, data + out.size()};
}

assets::AssetId idOf(uint8_t first)
{
    assets::AssetId id{};
    id.bytes[0] = first;
    id.bytes[15] = 0x99; // apart from other tests' assets
    return id;
}

// Job workers for the test, so loads run on them.
struct Workers
{
    Workers()
    {
        REQUIRE(jobs::initialize(2));
    }

    ~Workers()
    {
        assets::waitForLoads();
        jobs::shutdown();
    }
};

} // namespace

TEST_CASE("A collider's body is made once its collision data has loaded")
{
    importers::MeshSettings options = importers::testing::settings();
    options.collision = importers::CollisionImport::Mesh;
    Result<importers::MeshImport*> walker = importers::testing::importWalker(options);
    REQUIRE(walker);
    GatedSource::files = {{1, meshFile(*walker.value())}};
    importers::destroyMeshImport(walker.value());
    GatedSource::install();
    Workers workers;

    physics::testing::Scene scene;
    const assets::AssetId model = idOf(1);
    // The Walker's mesh as level geometry, its head's top at 1.89 m.
    scene.add({physics::ShapeKind::Mesh, {}, {}, 0.6f, 0, false, model, 0}, {5, 0, 0});
    auto headHeight = [&] {
        const physics::RayHit hit =
            physics::raycast(*scene.world, {5, 5, 0}, {0, -1, 0}, 10, physics::defaultQueryFilter());
        return hit.hit ? hit.point.y : -1.0f;
    };

    GatedSource::close();
    scene.step(3);
    CHECK(assets::pendingLoads() == 1);
    CHECK(headHeight() == -1.0f); // no body yet
    CHECK(assets::startLoads(*scene.world) == 1);

    GatedSource::release();
    assets::waitForLoads();
    CHECK(assets::startLoads(*scene.world) == 0);
    scene.step();
    CHECK(headHeight() == doctest::Approx(1.89f).epsilon(0.02));
    CHECK(GatedSource::offMain > 0);
    (void)physics::reloadCollisionAsset(model);
}

TEST_CASE("A sound asked for while it loads starts once it has")
{
    GatedSource::files = {{2, soundFile(audio::testing::sine(440, 0.3))}};
    GatedSource::install();
    Workers workers;
    audio::testing::Offline mixer;

    GatedSource::close();
    const uint32_t voice = audio::playOneShot(idOf(2), mixer.at(0), audio::testing::here, 1, audio::masterBus);
    CHECK(voice != 0); // a voice already, to stop or change
    mixer.mixSeconds(0.1);
    CHECK(mixer.onset(0.05f) == -1); // nothing yet

    GatedSource::release();
    assets::waitForLoads();
    const uint64_t loaded = mixer.frames;
    mixer.mixSeconds(0.2);
    CHECK(mixer.onset(0.05f, loaded) >= 0);

    // An AudioSource that plays on start waits while its sound loads.
    audio::registerAudio();
    ecs::World* world = ecs::createWorld();
    const ecs::ComponentId sourceOnly[] = {ecs::componentIdOf<audio::AudioSource>};
    *ecs::get<audio::AudioSource>(*world, ecs::createEntity(*world, sourceOnly)) = {
        .clip = idOf(7), .volume = 1, .pitch = 1, .playOnStart = true};
    GatedSource::files[7] = soundFile(audio::testing::sine(440, 0.1));
    GatedSource::close();
    CHECK(assets::startLoads(*world) == 1);
    GatedSource::release();
    assets::waitForLoads();
    CHECK(assets::startLoads(*world) == 0);
    ecs::destroyWorld(world);

    // A sound that isn't there: no voice once it's known.
    CHECK(audio::playOneShot(idOf(3), mixer.at(0.3), audio::testing::here, 1, audio::masterBus) != 0);
    assets::waitForLoads();
    CHECK(audio::playOneShot(idOf(3), mixer.at(0.3), audio::testing::here, 1, audio::masterBus) == 0);
}

TEST_CASE("An Animator holds still until its skeleton and clip have loaded")
{
    using animation::testing::WalkerAssets;
    WalkerAssets::install();
    // The Walker's model and Walk clip, under IDs no other test has loaded.
    GatedSource::files = {{4, WalkerAssets::files[1]}, {5, WalkerAssets::files[2]}};
    GatedSource::install();
    Workers workers;
    scenes::registerTransforms();
    animation::registerAnimation();

    auto makeWalker = [](ecs::World& world) {
        const ecs::ComponentId components[] = {ecs::componentIdOf<animation::Animator>,
                                               ecs::componentIdOf<scenes::LocalTransform>,
                                               ecs::componentIdOf<scenes::WorldTransform>};
        const ecs::Entity entity = ecs::createEntity(world, components);
        *ecs::get<animation::Animator>(world, entity) = {idOf(4), idOf(5), 1.0f, true};
        *ecs::get<scenes::LocalTransform>(world, entity) = {{0, 0, 0}, Quaternion::identity(), {1, 1, 1}};
        return entity;
    };
    ecs::World* waiting = ecs::createWorld();
    const ecs::Entity late = makeWalker(*waiting);
    uint64_t clock = 1'000'000'000;
    auto step = [&clock](ecs::World& world, float seconds) {
        clock += static_cast<uint64_t>(static_cast<double>(seconds) * 1e9);
        animation::advancePoses(world, seconds, clock);
        animation::evaluatePoses(world);
    };

    // A second while it loads: time stands still.
    GatedSource::close();
    for (int i = 0; i < 10; ++i)
    {
        step(*waiting, 0.1f);
    }
    CHECK(assets::pendingLoads() >= 1);
    CHECK(assets::startLoads(*waiting) == 1);
    GatedSource::release();
    assets::waitForLoads();
    CHECK(assets::startLoads(*waiting) == 0);
    step(*waiting, 0.25f);

    // One that never waited, a quarter of a second in: the same pose.
    ecs::World* prompt = ecs::createWorld();
    const ecs::Entity ready = makeWalker(*prompt);
    step(*prompt, 0.25f);
    const int32_t foot = animation::findBone(*prompt, ready, "RightFoot");
    REQUIRE(foot >= 0);
    const Matrix4x4 a = animation::boneModelTransform(*waiting, late, foot);
    const Matrix4x4 b = animation::boneModelTransform(*prompt, ready, foot);
    for (int r = 0; r < 4; ++r)
    {
        CHECK(a.rows[r].x == doctest::Approx(b.rows[r].x).epsilon(1e-4));
        CHECK(a.rows[r].y == doctest::Approx(b.rows[r].y).epsilon(1e-4));
        CHECK(a.rows[r].z == doctest::Approx(b.rows[r].z).epsilon(1e-4));
    }
    CHECK(GatedSource::offMain > 0);
    animation::releasePoses(*waiting);
    animation::releasePoses(*prompt);
    ecs::destroyWorld(waiting);
    ecs::destroyWorld(prompt);
    for (uint8_t n : {4, 5})
    {
        (void)animation::reloadAnimationAsset(idOf(n));
    }
}

TEST_CASE("A source that isn't thread-safe is read on the main thread")
{
    GatedSource::files = {{6, soundFile(audio::testing::sine(440, 0.1))}};
    GatedSource::install(false);
    Workers workers;
    audio::testing::Offline mixer;
    CHECK(audio::playOneShot(idOf(6), mixer.at(0), audio::testing::here, 1, audio::masterBus) != 0);
    assets::waitForLoads();
    CHECK(GatedSource::offMain == 0);
}
