#pragma once

// Loading assets on background jobs (ADR 0099): a load reads an asset's
// bytes through the source and prepares it on a Background job (parsing,
// decompressing, creating GPU objects, submitting uploads), then publishes
// it on the main thread, where the system that asked for it makes it
// visible (its tables, the mixer, the physics world). The system
// "Assets.Publish" publishes finished loads at the start of every frame.
//
// A source that isn't threadSafe is read on the main thread when the load
// starts; the rest still runs on a job. Without job workers (the engine's
// tests), a load runs to the end, published, before startLoad returns.
//
// Systems start loads when they first meet an asset (a new MeshInstance, a
// new Collider), so a scene's loads begin in its first frame. To have them
// done before that frame, call startLoads (each system's load starter, over
// the world) and then waitForLoads. To load a scene while frames go on, call
// startLoads each frame until it returns 0 (C#'s Scene.LoadAsync does).
//
// Main thread, apart from what a task's prepare does on its job.

#include <crucible/AssetSource.h>
#include <crucible/Ecs.h>
#include <crucible/Export.h>
#include <crucible/Expose.h>

#include <cstdint>
#include <span>
#include <string_view>

namespace crucible::assets {

struct LoadTask
{
    AssetId id;
    void* context;
    // On a job (or the main thread, without workers): the asset's bytes, or
    // found = false if the source has no such asset. Must be thread-safe.
    void (*prepare)(void* context, std::span<const uint8_t> bytes, bool found);
    // On the main thread, after prepare: whether its work outside the job
    // (a GPU upload) is done; with wait, returns once it is. Null: done.
    bool (*finished)(void* context, bool wait) = nullptr;
    // On the main thread, once finished: makes the result visible, or
    // reports why it failed. Called exactly once, and the task is forgotten.
    void (*publish)(void* context);
};

// Starts a load. The context must stay alive until publish is called.
CRUCIBLE_EXPORT void startLoad(const LoadTask& task);

// Publishes every load that has finished (the "Assets.Publish" system).
CRUCIBLE_EXPORT void publishLoads();

// Loads started and not yet published.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE uint32_t pendingLoads();

// Returns once no load is pending, publishing each as it finishes (and the
// loads those start).
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE void waitForLoads();

// A system's load starter: starts loads for what the world's components
// name and nothing is loading yet, without simulating anything, and returns
// how many of the world's components still wait for a load (one that failed
// waits no longer: it never becomes ready).
using LoadStarter = uint32_t (*)(ecs::World& world);

// Registers a starter (again: once). Thread-safe.
CRUCIBLE_EXPORT void registerLoadStarter(LoadStarter starter);

// Runs every registered starter over the world: then waitForLoads has a
// scene's loads to wait for before its first frame. Returns how many of the
// world's components still wait (the starters' sum); 0 once all are ready.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE uint32_t startLoads(ecs::World& world);

inline constexpr std::string_view publishSystemName = "Assets.Publish";

// Registers "Assets.Publish" (BeginFrame). Called by the engine's C# layer
// with the other modules.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE_INTERNAL void registerAssetLoads();

} // namespace crucible::assets
