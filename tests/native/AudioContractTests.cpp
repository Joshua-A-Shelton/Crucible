// The audio contract (ADR 0075): its components and gameplay system
// registered, the null audio counting what it's asked, sources that play on
// start started once, and an installed service taking over.

#include "SubsystemTestSupport.h"

#include <crucible/Audio.h>

#include <doctest/doctest.h>

using namespace crucible;
using namespace crucible::audio;

TEST_CASE("The audio contract registers its components and gameplay system")
{
    registerAudio();
    const int32_t index = test::systemIndex("Audio.Update");
    REQUIRE(index >= 0);
    CHECK(loop::nativeSystemIsGameplay(static_cast<uint32_t>(index)));
    CHECK(loop::nativeSystemPhase(static_cast<uint32_t>(index)) == "LateUpdate");
    CHECK(ecs::componentIdOf<AudioSource> != ecs::invalidComponent);
    CHECK(ecs::componentInfo(ecs::componentIdOf<AudioListener>).size == 0); // a tag
}

TEST_CASE("The null audio counts what it's asked to do")
{
    const NullAudioCounts before = nullAudioCounts();
    const uint32_t first = playOneShot({}, 0, {0, 0, 0}, 1, masterBus);
    const uint32_t second = playOneShot({}, 0, {1, 0, 0}, 1, masterBus);
    CHECK(first != 0);
    CHECK(second != first);
    stop(first, 0.1f);
    setBusVolume(1, 0.5f, 1);
    setBusEffects(1, {800, 0.2f, noBus, 0});
    defineSnapshot("Underwater", 1, 0.3f, {400, 0.5f, noBus, 0});
    blendToSnapshot("Underwater", 2);
    playMusic({}, {1.5f, MusicAlign::Bar});
    const NullAudioCounts after = nullAudioCounts();
    CHECK(after.plays - before.plays == 2);
    CHECK(after.stops - before.stops == 1);
    CHECK(after.busChanges - before.busChanges == 2);
    CHECK(after.snapshots - before.snapshots == 1);
    CHECK(after.musicChanges - before.musicChanges == 1);
}

TEST_CASE("The null audio starts sources that play on start once")
{
    registerAudio();
    ecs::World* world = ecs::createWorld();
    const ecs::ComponentId set[] = {ecs::componentIdOf<AudioSource>};
    const ecs::Entity playing = ecs::createEntity(*world, set);
    ecs::get<AudioSource>(*world, playing)->playOnStart = true;
    ecs::createEntity(*world, set); // waits to be played
    const uint32_t before = nullAudioCounts().sourcesStarted;
    test::runSystem("Audio.Update", *world);
    test::runSystem("Audio.Update", *world);
    CHECK(nullAudioCounts().sourcesStarted - before == 1);
    ecs::destroyWorld(world);
}

namespace {

uint32_t fixedVoice(assets::AssetId, uint64_t, Vector3, float, uint32_t)
{
    return 77;
}

} // namespace

TEST_CASE("An installed audio service answers the calls")
{
    const AudioService before = audioService();
    AudioService replacement = before;
    replacement.playOneShot = fixedVoice;
    installAudio(replacement);
    CHECK(playOneShot({}, 0, {0, 0, 0}, 1, masterBus) == 77);
    installAudio(before);
    CHECK(playOneShot({}, 0, {0, 0, 0}, 1, masterBus) != 77);
}
