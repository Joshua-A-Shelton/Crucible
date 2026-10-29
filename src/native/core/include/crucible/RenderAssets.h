#pragma once

// GPU assets (ADR 0049): imported models (.crmesh) and textures (.crtexture)
// loaded into Slag buffers and textures for render steps to draw.
//
// By asset ID, acquire loads on first use and counts uses; release unloads
// after the last. The bytes come from the reader C# registers (the asset
// database in the editor). load / unload take bytes directly, for data
// outside the asset system. Loading is synchronous (ADR 0021's asynchronous
// loading comes with cooking). Unloading never frees what a submitted frame
// may still use: Slag's DeferredDestroyer and DescriptorAllocator wait for
// the renderer's work semaphore.
//
// Threading: like the renderer, on the launcher's main thread.

#include <crucible/Expose.h>
#include <crucible/MeshFile.h>
#include <crucible/Result.h>

#include <cstdint>
#include <span>

namespace slag {
class Buffer;
}

namespace crucible::assets {

// An asset's GUID, the same 16 bytes as C#'s AssetId.
struct CRUCIBLE_EXPOSE_AS("Crucible.Assets.AssetId") AssetId
{
    uint8_t bytes[16];
};

inline bool operator==(const AssetId& a, const AssetId& b)
{
    for (int i = 0; i < 16; ++i)
    {
        if (a.bytes[i] != b.bytes[i])
        {
            return false;
        }
    }
    return true;
}

} // namespace crucible::assets

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

// The samplers made when the renderer starts, until materials choose their
// own (M5 step 6). Linear is trilinear with anisotropy.
enum class CRUCIBLE_EXPOSE Sampler : uint32_t
{
    LinearRepeat,
    LinearClamp,
    PointClamp,
};

// By asset ID. NotFound if the reader has no such asset; InvalidData if its
// bytes aren't a model (or texture); Unsupported before the renderer starts.
CRUCIBLE_EXPOSE Result<MeshHandle> acquireMesh(assets::AssetId model);
CRUCIBLE_EXPOSE void releaseMesh(MeshHandle mesh);
CRUCIBLE_EXPOSE Result<TextureHandle> acquireTexture(assets::AssetId texture);
CRUCIBLE_EXPOSE void releaseTexture(TextureHandle texture);

// From bytes, outside the asset system.
CRUCIBLE_EXPOSE Result<MeshHandle> loadMesh(std::span<const uint8_t> bytes);
CRUCIBLE_EXPOSE void unloadMesh(MeshHandle mesh);
CRUCIBLE_EXPOSE Result<TextureHandle> loadTexture(std::span<const uint8_t> bytes);
CRUCIBLE_EXPOSE void unloadTexture(TextureHandle texture);

// The texture's slot in the resource heap, which shaders read it through
// (ResourceDescriptorHeap[slot]); 0 for a stale handle.
CRUCIBLE_EXPOSE uint32_t textureDescriptor(TextureHandle texture);
// The sampler's slot in the sampler heap.
CRUCIBLE_EXPOSE uint32_t samplerDescriptor(Sampler sampler);

// The C# side's reader: an [UnmanagedCallersOnly] function, as an address
// (the generator doesn't do function pointers), called as
//     int32_t reader(const AssetId* id, void* context,
//                    void (*deliver)(void* context, const uint8_t* data, uint64_t size))
// It calls `deliver` with the asset's imported bytes before returning 0, or
// returns -1 if there is no such asset. Internal to the engine.
CRUCIBLE_EXPOSE_INTERNAL void setAssetReader(uint64_t readerAddress);

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
    Stream indices; // each MeshRange's indexByteOffset is from here
    std::span<const assets::MeshEntry> meshes;
    std::span<const assets::MeshLod> lods;
    std::span<const assets::MeshRange> ranges;
    std::span<const assets::MeshNode> nodes;
    std::span<const assets::MaterialEntry> materials;
};

// Null for a stale handle.
const MeshData* meshData(MeshHandle mesh);

} // namespace crucible::render
