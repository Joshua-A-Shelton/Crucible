#pragma once

// Native modules (ADR 0074): a project's own C++ (Project/Native), built as
// a shared library that links the engine's shared core and uses its public
// headers. The launcher (--module) and the editor load it after the engine
// starts and before the schedule is built; its start function registers its
// systems (crucible/Systems.h) and components. Modules stay loaded until the
// process ends: native code can't be unloaded safely while its components
// live in a world.
//
// A module defines both entry points, in one of its files:
//
//     CRUCIBLE_MODULE_EXPORT bool crucibleModuleStart()
//     {
//         crucible::linkEngineComponents();
//         crucible::ecs::registerComponent<Bob>("Diorama.Bob");
//         crucible::loop::registerSystem({.name = "Diorama.Bob", .phase = "Update", .function = bob});
//         return true;
//     }
//
//     CRUCIBLE_MODULE_EXPORT void crucibleModuleStop() {}

#include <crucible/Animation.h>
#include <crucible/Audio.h>
#include <crucible/Camera.h>
#include <crucible/Ecs.h>
#include <crucible/Export.h>
#include <crucible/Expose.h>
#include <crucible/Lights.h>
#include <crucible/MeshInstance.h>
#include <crucible/Physics.h>
#include <crucible/Physics2D.h>
#include <crucible/PostProcess.h>
#include <crucible/Result.h>
#include <crucible/Sky.h>
#include <crucible/Transform.h>

#include <cstdint>
#include <string_view>

#if defined(_WIN32)
#define CRUCIBLE_MODULE_EXPORT extern "C" __declspec(dllexport)
#else
#define CRUCIBLE_MODULE_EXPORT extern "C" __attribute__((visibility("default")))
#endif

namespace crucible {

// Loads the module at the UTF-8 path and calls its crucibleModuleStart.
// NotFound if it can't be loaded; InvalidData if it lacks the entry points or
// its start returns false (it stays loaded either way, as code that may have
// registered something). Loading a path again does nothing. Main thread.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE Result<void> loadModule(std::string_view utf8Path);

// Calls every loaded module's crucibleModuleStop, newest first, before the
// engine shuts down. The libraries stay loaded.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE void stopModules();

// The modules loaded, in order, for C# to find a module's library by name
// (the bindings into the game assembly, ADR 0074).
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE_INTERNAL uint32_t moduleCount();
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE_INTERNAL std::string_view modulePath(uint32_t index);

// Each library has its own copy of ecs::componentIdOf<T> (Windows never
// shares them between DLLs), so a module registers the engine's components
// in its own copies, by the names the engine gave them: registering a name
// again returns the engine's ID. Call it first in crucibleModuleStart.
inline void linkEngineComponents()
{
    ecs::registerComponent<ecs::Parent>("crucible.Parent");
    ecs::registerComponent<scenes::LocalTransform>("Crucible.Scenes.LocalTransform");
    ecs::registerComponent<scenes::Transform2D>("Crucible.Scenes.Transform2D");
    ecs::registerComponent<scenes::WorldTransform>("Crucible.Scenes.WorldTransform");
    ecs::registerComponent<scenes::MeshInstance>("Crucible.Scenes.MeshInstance");
    ecs::registerComponent<scenes::LoadedMesh>("Crucible.Scenes.LoadedMesh");
    ecs::registerComponent<render::Camera>("Crucible.Render.Camera");
    ecs::registerComponent<render::PostProcess>("Crucible.Render.PostProcess");
    ecs::registerComponent<render::DirectionalLight>("Crucible.Render.DirectionalLight");
    ecs::registerComponent<render::PointLight>("Crucible.Render.PointLight");
    ecs::registerComponent<render::SpotLight>("Crucible.Render.SpotLight");
    ecs::registerComponent<render::AmbientLight>("Crucible.Render.AmbientLight");
    ecs::registerComponent<render::Sky>("Crucible.Render.Sky");
    ecs::registerComponent<physics::RigidBody>("Crucible.Physics.RigidBody");
    ecs::registerComponent<physics::Collider>("Crucible.Physics.Collider");
    ecs::registerComponent<physics::Joint>("Crucible.Physics.Joint");
    ecs::registerComponent<physics::CharacterController>("Crucible.Physics.CharacterController");
    ecs::registerComponent<physics2d::RigidBody2D>("Crucible.Physics2D.RigidBody2D");
    ecs::registerComponent<physics2d::Collider2D>("Crucible.Physics2D.Collider2D");
    ecs::registerComponent<audio::AudioSource>("Crucible.Audio.AudioSource");
    ecs::registerComponent<audio::AudioListener>("Crucible.Audio.AudioListener");
    ecs::registerComponent<animation::Animator>("Crucible.Animation.Animator");
    ecs::registerComponent<animation::PoseHandle>("Crucible.Animation.PoseHandle");
}

} // namespace crucible
