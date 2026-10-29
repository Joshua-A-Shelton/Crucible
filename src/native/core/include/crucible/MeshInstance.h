#pragma once

// Mesh instances (ADR 0051): which imported mesh an entity draws. Shared with
// C# like the transforms (Crucible.Scenes): MeshInstance is saved in scenes;
// LoadedMesh is the renderer's, never saved.

#include <crucible/Expose.h>
#include <crucible/RenderAssets.h>

#include <cstdint>

namespace crucible::scenes {

// One mesh of an imported model (ADR 0043, 0045).
struct CRUCIBLE_EXPOSE MeshInstance
{
    assets::AssetId model; // the model's asset: its imported MESH file
    int32_t mesh;          // which of the model's meshes
};

// The loaded model a MeshInstance resolved to. The renderer adds it before
// drawing an instance for the first time, and resolves again when the
// instance's chunk changes or the handle goes stale. Models no instance used
// for a while are unloaded.
struct CRUCIBLE_EXPOSE LoadedMesh
{
    render::MeshHandle handle;
};

// Registers MeshInstance and LoadedMesh. Registering again is harmless.
CRUCIBLE_EXPOSE_INTERNAL void registerMeshInstance();

} // namespace crucible::scenes
