// A native module replacing physics and audio, for SubsystemReplacementTests
// (ADR 0075): the contract's system names registered again, its own
// services installed.

#include <crucible/Audio.h>
#include <crucible/Module.h>
#include <crucible/Physics.h>
#include <crucible/Systems.h>

namespace {

int marker = 0; // its systems' user data: what the tests look for

void nothing(const crucible::loop::SystemContext&, void*) {}

crucible::physics::RayHit alwaysAt42(crucible::ecs::World&, crucible::Vector3, crucible::Vector3, float, uint32_t)
{
    return {true, {}, {0, 0, 0}, {0, 1, 0}, 42};
}

uint32_t voice9(crucible::assets::AssetId, uint64_t, crucible::Vector3, float, uint32_t)
{
    return 9;
}

} // namespace

CRUCIBLE_MODULE_EXPORT bool crucibleModuleStart()
{
    crucible::linkEngineComponents();
    crucible::physics::PhysicsService physics = crucible::physics::physicsService();
    physics.raycast = alwaysAt42;
    crucible::physics::installPhysics(physics);
    for (const auto& [name, phase] : {std::pair{crucible::physics::syncSystemName, "PrePhysics"},
                                      std::pair{crucible::physics::stepSystemName, "Physics"},
                                      std::pair{crucible::physics::writeBackSystemName, "PostPhysics"}})
    {
        crucible::loop::registerSystem({.name = name, .phase = phase, .function = nothing, .userData = &marker});
    }
    crucible::audio::AudioService audio = crucible::audio::audioService();
    audio.playOneShot = voice9;
    crucible::audio::installAudio(audio);
    crucible::loop::registerSystem(
        {.name = crucible::audio::updateSystemName, .phase = "LateUpdate", .function = nothing, .userData = &marker});
    return true;
}

CRUCIBLE_MODULE_EXPORT void crucibleModuleStop() {}
