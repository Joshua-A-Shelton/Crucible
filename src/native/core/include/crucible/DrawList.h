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
struct MaterialHandle;

// The visible instances of one mesh at one level of detail: one instanced
// drawIndexed per submesh, all with the same instances.
struct DrawBatch
{
    const MeshData* model;
    uint32_t mesh;          // into model->meshes
    uint32_t lod;           // that mesh's level, 0 the finest
    uint32_t firstInstance; // into the instance list: the draw's firstInstance
    uint32_t instanceCount;
    // The model's material for each of its slots (ADR 0052); a submesh's
    // MeshRange::material picks one. Empty for a model without generated
    // materials, whose submeshes draw the fallback.
    std::span<const MaterialHandle> materials;
    // A skinned instance's batch (ADR 0080, always one instance): its first
    // vertex in the frame's skinned vertices; UINT32_MAX for a batch drawn
    // from the model's own.
    uint32_t deformedOffset = UINT32_MAX;
};

// A skinned instance the Skinning step deforms this frame (ADR 0080): its
// mesh's vertices, blended by `jointCount` palette joints from
// `paletteOffset`, into the skinned vertices from `deformedOffset`.
struct SkinnedInstance
{
    const MeshData* model;
    uint32_t mesh;
    uint32_t paletteOffset; // in joints, three float4 rows each
    uint32_t jointCount;
    uint32_t deformedOffset; // in vertices
    uint32_t vertexCount;
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
    // The instance list as the CPU wrote it (entity indices, by firstInstance
    // plus offset), for steps that leave some out (the editor's pick, ADR 0071).
    std::span<const uint32_t> entities{};
    // Skinning (ADR 0080): the instances to deform, the palettes' slot
    // (StructuredBuffer<float4>), the skinned vertices' slot as draws read
    // them (ByteAddressBuffer), and how many vertices it holds. The skinned
    // vertices are stream by stream: positions (12 bytes each), then
    // normals (4), tangents (8), and outline normals (4).
    std::span<const SkinnedInstance> skinned{};
    uint32_t palettes = 0;
    uint32_t deformed = 0;
    uint32_t skinnedVertexCount = 0;
};

// Byte offsets of the skinned vertices' streams, for `count` vertices.
inline constexpr uint64_t skinnedNormalsOffset(uint32_t count)
{
    return uint64_t{count} * 12;
}
inline constexpr uint64_t skinnedTangentsOffset(uint32_t count)
{
    return uint64_t{count} * 16;
}
inline constexpr uint64_t skinnedOutlineNormalsOffset(uint32_t count)
{
    return uint64_t{count} * 24;
}
inline constexpr uint64_t skinnedVerticesSize(uint32_t count)
{
    return uint64_t{count} * 28;
}

} // namespace crucible::render
