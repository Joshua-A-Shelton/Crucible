// Replacing physics and audio with a native module (ADR 0074, 0075), on the
// shared core as the launcher and editor use it: the module's systems take
// the contract's names, as gameplay systems, and its services answer; the
// engine registering its defaults afterwards leaves them in place.

#include "SubsystemTestSupport.h"

#include <crucible/Audio.h>
#include <crucible/Module.h>
#include <crucible/Physics.h>

#include <doctest/doctest.h>

using namespace crucible;

namespace {

void* userDataOf(std::string_view name)
{
    const int32_t index = test::systemIndex(name);
    return index < 0 ? nullptr : loop::nativeSystemUserData(static_cast<uint32_t>(index));
}

} // namespace

TEST_CASE("A module replaces physics and audio")
{
    // The engine's defaults first, as when the launcher starts.
    physics::registerPhysics();
    audio::registerAudio();
    CHECK(userDataOf("Physics.Step") == nullptr);
    ecs::World* world = ecs::createWorld();
    CHECK_FALSE(physics::raycast(*world, {0, 0, 0}, {0, 0, 1}, 100, physics::allLayers).hit);

    REQUIRE(loadModule(CRUCIBLE_TEST_SUBSYSTEMS));
    for (std::string_view name : {"Physics.Sync", "Physics.Step", "Physics.WriteBack", "Audio.Update"})
    {
        CAPTURE(name);
        CHECK(userDataOf(name) != nullptr);
        CHECK(loop::nativeSystemIsGameplay(static_cast<uint32_t>(test::systemIndex(name))));
    }
    const physics::RayHit hit = physics::raycast(*world, {0, 0, 0}, {0, 0, 1}, 100, physics::allLayers);
    CHECK(hit.hit);
    CHECK(hit.distance == 42);
    CHECK(audio::playOneShot({}, 0, {0, 0, 0}, 1, audio::masterBus) == 9);

    // The defaults again (a schedule built later): the module's stay.
    physics::registerPhysics();
    audio::registerAudio();
    CHECK(userDataOf("Physics.Step") != nullptr);
    CHECK(userDataOf("Audio.Update") != nullptr);
    ecs::destroyWorld(world);
}
