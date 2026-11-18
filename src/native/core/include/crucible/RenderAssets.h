#pragma once

// GPU assets (ADR 0049): imported models (.crmesh) and textures (.crtexture)
// loaded into Slag buffers and textures for render steps to draw.
//
// By asset ID, acquire loads on first use and counts uses; release unloads
// after the last. The bytes come from the installed asset source
// (crucible/AssetSource.h, ADR 0076). Acquiring returns at once: the load
// runs on a Background job and is published at the start of a later frame
// (crucible/AssetLoads.h, ADR 0099); until then the handle is valid but not
// ready, and a model isn't drawn until it and its materials' textures are
// all ready. Without job workers, acquire loads before it returns. load /
// unload take bytes directly, for data outside the asset system, and load
// at once. Unloading never frees what a submitted frame may still use:
// Slag's DeferredDestroyer and DescriptorAllocator wait for the renderer's
// work semaphore.
//
// Threading: like the renderer, on the launcher's main thread.

#include <crucible/AssetSource.h>
#include <crucible/Export.h>
#include <crucible/Expose.h>
#include <crucible/Math.h>
#include <crucible/MeshFile.h>
#include <crucible/Result.h>

#include <cstdint>
#include <span>
#include <string_view>

namespace slag {
class Buffer;
}

namespace crucible::render {

// A loaded model or texture: an index and a generation, so a handle kept after
// its asset was unloaded is recognized. Generation 0 is no handle.
struct CRUCIBLE_EXPOSE MeshHandle
{
    uint32_t index;
    uint32_t generation;
};

struct CRUCIBLE_EXPOSE TextureHandle
{
    uint32_t index;
    uint32_t generation;
};

// A loaded material (ADR 0052): for now, a model's generated ones.
struct CRUCIBLE_EXPOSE MaterialHandle
{
    uint32_t index;
    uint32_t generation;
};

// The samplers made when the renderer starts, until materials choose their
// own (M5 step 6). Linear is trilinear with anisotropy.
enum class CRUCIBLE_EXPOSE Sampler : uint32_t
{
    LinearRepeat,
    LinearClamp,
    PointClamp,
};

// By asset ID. Unsupported before the renderer starts. Loading asynchronously,
// a failure is logged when the load ends (the handle never becomes ready);
// without job workers it's returned: NotFound if the reader has no such
// asset, InvalidData if its bytes aren't a model (or texture).
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE Result<MeshHandle> acquireMesh(assets::AssetId model);
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE void releaseMesh(MeshHandle mesh);
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE Result<TextureHandle> acquireTexture(assets::AssetId texture);
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE void releaseTexture(TextureHandle texture);

// Whether it's loaded (ADR 0099): a model with its materials' textures, so
// it draws; a texture; a material's textures. False for a stale handle.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE bool isMeshReady(MeshHandle mesh);
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE bool isTextureReady(TextureHandle texture);
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE bool isMaterialReady(MaterialHandle material);

// From bytes, outside the asset system.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE Result<MeshHandle> loadMesh(std::span<const uint8_t> bytes);
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE void unloadMesh(MeshHandle mesh);
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE Result<TextureHandle> loadTexture(std::span<const uint8_t> bytes);
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE void unloadTexture(TextureHandle texture);

// Authored materials (.crmat, ADR 0052), by asset ID: acquire loads on first
// use (with its shader and textures) and counts uses; release unloads after
// the last. NotFound if the reader has no such asset; InvalidData if its
// bytes aren't a material, or don't match its shader (reimport it).
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE Result<MaterialHandle> acquireMaterial(assets::AssetId material);
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE void releaseMaterial(MaterialHandle material);
// Replaces a material's parameters, laid out as its shader's material type
// (C#: the generated struct). Frames in flight keep the old values.
// InvalidData if the size differs from the type's.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE Result<void> setMaterialParameters(MaterialHandle material,
                                                                   std::span<const uint8_t> parameters);
// What a material is, for C#'s check that a struct matches: its shader's
// asset ID (zero for the engine's) and its type's name.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE_INTERNAL assets::AssetId materialShader(MaterialHandle material);
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE_INTERNAL std::string_view materialTypeName(MaterialHandle material);

// Loads an asset again from its reader (ADR 0072, the editor after a
// reimport): a model, texture, material, or game shader that's loaded is
// replaced in place, so handles to it stay valid and the next frame draws
// the new version. It waits for the GPU first. False if the asset isn't
// loaded (nothing to do; always, before the renderer starts). On failure the old version stays: NotFound if the
// reader has no such asset, InvalidData if its bytes don't load.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE Result<bool> reloadRenderAsset(assets::AssetId asset);

// The texture's slot in the resource heap, which shaders read it through
// (ResourceDescriptorHeap[slot]); 0 for a stale handle or one not ready.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE uint32_t textureDescriptor(TextureHandle texture);

// A loaded texture's top-level size in texels; zero for a stale handle.
struct CRUCIBLE_EXPOSE TextureSize
{
    uint32_t width;
    uint32_t height;
};
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE TextureSize textureSize(TextureHandle texture);
// The sampler's slot in the sampler heap.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE uint32_t samplerDescriptor(Sampler sampler);

// What render steps read to draw a loaded model (C++ only): its GPU buffer
// and resource-heap slot, where each stream starts in it, and the file's
// tables. Valid until the
// model is unloaded.
struct MeshData
{
    slag::Buffer* buffer;
    uint32_t descriptor; // the buffer's slot in the resource heap, as a ByteAddressBuffer
    assets::MeshFileInfo info;
    // Byte offsets into `buffer`; a stream the file lacks has size 0.
    struct Stream
    {
        uint64_t offset;
        uint64_t size;
    };
    Stream positions;
    Stream normals;
    Stream tangents;
    Stream uv0;
    Stream uv1;
    Stream colors;
    Stream outlineNormals; // ADR 0055
    Stream boneIndices;    // ADR 0078: uint16 x skinInfluences per vertex
    Stream boneWeights;    // unorm8 x skinInfluences per vertex
    Stream indices;        // each MeshRange's indexByteOffset is from here
    std::span<const assets::MeshEntry> meshes;
    std::span<const assets::MeshLod> lods;
    std::span<const assets::MeshRange> ranges;
    std::span<const assets::MeshNode> nodes;
    std::span<const assets::MaterialEntry> materials;
    // The skin (ADR 0078, 0080), empty without one: its bones, their names
    // (BoneEntry's offsets are into it), each bone's bind-pose origin in
    // the model's space, and its reach: how far its vertices are from that
    // origin (negative for a bone no vertex follows), for culling posed
    // instances.
    uint32_t skinInfluences;
    std::span<const assets::BoneEntry> bones;
    std::span<const char> names;
    std::span<const Vector3> boneOrigins;
    std::span<const float> boneReach;
};

// Null for a stale handle.
CRUCIBLE_EXPORT const MeshData* meshData(MeshHandle mesh);

} // namespace crucible::render
