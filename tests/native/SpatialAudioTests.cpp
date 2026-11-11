// 3D sound (ADR 0082): constant-power panning from the listener's right,
// distance rolloffs, and AudioSources played by "Audio.Update" where their
// entity is.

#include "AudioTestSupport.h"
#include "SubsystemTestSupport.h"

#include <crucible/Transform.h>

using namespace crucible;
using namespace crucible::audio;
using namespace crucible::audio::testing;

namespace {

// The levels a one-shot at `position` reaches on each side.
std::pair<float, float> heard(Vector3 position)
{
    Offline mixer;
    const TestSound sound(steady(0.5f, 4800));
    (void)playOneShot(sound.id, 0, position, 1, masterBus);
    mixer.mix(2400);
    return {mixer.left(1000) / 0.5f, mixer.right(1000) / 0.5f};
}

// A world with a listener at the origin facing +z, and a source.
struct Scene
{
    ecs::World* world = nullptr;
    ecs::Entity source{};

    explicit Scene(AudioSource component, Vector3 at = {})
    {
        registerAudio();
        scenes::registerTransforms();
        world = ecs::createWorld();
        const ecs::ComponentId listener[] = {ecs::componentIdOf<AudioListener>,
                                             ecs::componentIdOf<scenes::WorldTransform>};
        *ecs::get<scenes::WorldTransform>(*world, ecs::createEntity(*world, listener)) = {Matrix4x4::identity()};
        const ecs::ComponentId sourced[] = {ecs::componentIdOf<AudioSource>,
                                            ecs::componentIdOf<scenes::WorldTransform>};
        source = ecs::createEntity(*world, sourced);
        *ecs::get<AudioSource>(*world, source) = component;
        *ecs::get<scenes::WorldTransform>(*world, source) = {Matrix4x4::translation(at)};
    }

    ~Scene()
    {
        ecs::destroyWorld(world);
    }

    Scene(const Scene&) = delete;
    Scene& operator=(const Scene&) = delete;

    void update()
    {
        test::runSystem("Audio.Update", *world);
    }
};

AudioSource sourceOf(assets::AssetId clip)
{
    return {clip, 1, 1, false, true, masterBus, false, 1, 50, Rolloff::Inverse, 0};
}

} // namespace

TEST_CASE("A sound to the right is heard on the right at constant power")
{
    const auto [rightLeft, rightRight] = heard({1, 0, 0}); // +x is the listener's right
    CHECK(rightLeft == doctest::Approx(0).epsilon(1e-4));
    CHECK(rightRight == doctest::Approx(1).epsilon(1e-4));
    const auto [leftLeft, leftRight] = heard({-1, 0, 0});
    CHECK(leftLeft == doctest::Approx(1).epsilon(1e-4));
    CHECK(leftRight == doctest::Approx(0).epsilon(1e-4));
    // Half way round, the power is the same as in the middle.
    const auto [halfLeft, halfRight] = heard(normalize(Vector3{1, 0, 1}));
    CHECK(halfLeft * halfLeft + halfRight * halfRight == doctest::Approx(1).epsilon(1e-3));
    CHECK(halfRight > halfLeft);
}

TEST_CASE("One-shots fall off with distance and go silent past the farthest")
{
    // A one-shot's distances: full to 1 m, inverse beyond, silent past 50 m.
    CHECK(heard({0, 0, 2}).first == doctest::Approx(0.5f * centre).epsilon(1e-3));
    CHECK(heard({0, 0, 4}).first == doctest::Approx(0.25f * centre).epsilon(1e-3));
    CHECK(heard({0, 0, 60}).first == 0.0f);
}

TEST_CASE("An AudioSource plays on start where its entity is with its pitch")
{
    Offline mixer;
    const TestSound sound(steady(0.5f, 4800));
    AudioSource component = sourceOf(sound.id);
    component.pitch = 2;
    component.spatial = true;
    component.rolloff = Rolloff::Linear;
    component.maxDistance = 11;
    Scene scene(component, {0, 0, 6}); // half way from 1 m to 11 m
    scene.update();
    mixer.mix(4800);
    CHECK(mixer.left(1000) == doctest::Approx(0.5f * 0.5f * centre).epsilon(1e-3));
    CHECK(mixer.peak(2410, 4800, 0) == 0.0f); // twice as fast: half as long
    scene.update();                           // started once only
    mixer.mix(960);
    CHECK(mixer.peak(4800, 5760, 0) == 0.0f);
}

TEST_CASE("A source stops when its entity goes and stopAll stops everything")
{
    Offline mixer;
    const TestSound sound(steady(0.5f, 4800));
    AudioSource component = sourceOf(sound.id);
    component.loop = true;
    {
        Scene scene(component);
        scene.update();
        mixer.mix(960);
        CHECK(mixer.left(500) > 0.3f);
        ecs::destroyEntity(*scene.world, scene.source);
        scene.update(); // fades out over 50 ms
        mixer.mix(frameOf(0.1));
        CHECK(mixer.peak(960 + frameOf(0.06), mixer.frames, 0) == 0.0f);
    }
    {
        Scene scene(component);
        scene.update();
        mixer.mix(960);
        stopAll(0);
        mixer.mix(960);
        CHECK(mixer.peak(mixer.frames - 900, mixer.frames, 0) == 0.0f);
        scene.update(); // it plays on start, so it starts again
        mixer.mix(960);
        CHECK(mixer.left(mixer.frames - 100) > 0.3f);
    }
}
