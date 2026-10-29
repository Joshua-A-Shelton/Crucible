#pragma once

// What render steps draw (ADR 0051): the frame's visible mesh instances,
// culled against the camera on the job system and grouped into instanced
// batches, before the steps run. Shaders find each instance's world matrix
// through two resource-heap slots: the instance list maps an instance to its
// entity's index, and the transform buffer holds world matrices by entity
// index (kept on the GPU, updated only where WorldTransform changed).

#include <cstdint>
#include <span>

namespace crucible::render {

struct MeshData;

// The visible instances of one mesh at one level of detail: one instanced
// drawIndexed per submesh, all with the same instances.
struct DrawBatch
{
    const MeshData* model;
    uint32_t mesh;          // into model->meshes
    uint32_t lod;           // that mesh's level, 0 the finest
    uint32_t firstInstance; // into the instance list: the draw's firstInstance
    uint32_t instanceCount;
    // Per model material: its base-color texture's resource-heap slot, 0 for
    // none. Until materials (M5 step 6).
    std::span<const uint32_t> baseColorTextures;
};

struct DrawList
{
    std::span<const DrawBatch> batches; // ordered by model, mesh, then level
    // Resource-heap slots, valid for this frame: StructuredBuffer<uint>, the
    // visible instances' entity indices, batch after batch; and
    // StructuredBuffer<InstanceTransform> (shaders/crucible/shared.slang),
    // world matrices by entity index.
    uint32_t instances;
    uint32_t transforms;
};

} // namespace crucible::render
