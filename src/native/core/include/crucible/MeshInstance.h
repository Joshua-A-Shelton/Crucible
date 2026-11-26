#pragma once

// Mesh instances (ADR 0051): which imported mesh an entity draws, and with
// which material (ADR 0132). Shared with
// C# like the transforms (Crucible.Scenes): MeshInstance is saved in scenes;
// LoadedMesh is the renderer's, never saved.

#include <crucible/Export.h>
#include <crucible/Expose.h>
#include <crucible/RenderAssets.h>

#include <cstdint>

namespace crucible::scenes {

// One mesh of an imported model (ADR 0043, 0045).
struct CRUCIBLE_EXPOSE MeshInstance
{
    CRUCIBLE_HINT_ASSET(".crmesh") assets::AssetId model; // the model's asset: its imported MESH file
    CRUCIBLE_HINT_MESH_OF(model) int32_t mesh;            // which of the model's meshes
    bool noShadows;                                       // casts no shadows (ADR 0054); zero, the default, casts
};

// Draws the entity's MeshInstance with this material in place of every slot's
// own (ADR 0132): a paint swap, a hit flash, a material shown on a sphere.
// Batched with the other instances of the same mesh and material. Zero, the
// default, draws the slots' own. Aligned as C# aligns an AssetId (to 4).
struct CRUCIBLE_EXPOSE alignas(4) MaterialOverride
{
    CRUCIBLE_HINT_ASSET(".crmat") assets::AssetId material; // an imported .crmaterial
};

// The loaded model a MeshInstance resolved to. The renderer adds it before
// drawing an instance for the first time, and resolves again when the
// instance's chunk changes or the handle goes stale. Models no instance used
// for a while are unloaded.
struct CRUCIBLE_EXPOSE LoadedMesh
{
    render::MeshHandle handle;
};

// Registers MeshInstance, MaterialOverride, and LoadedMesh. Registering again is harmless.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE_INTERNAL void registerMeshInstance();

} // namespace crucible::scenes
