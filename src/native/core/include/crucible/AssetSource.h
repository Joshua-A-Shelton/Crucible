#pragma once

// The runtime asset contract (ADR 0076): one source of every asset's bytes
// by ID, which the renderer (RenderAssets), prefabs, and anything else at
// runtime read through. The editor installs its asset database; a game run
// from a project, its imported files (AssetCache); a native module (ADR
// 0074) could install pack files or a network stream. Main thread.

#include <crucible/Export.h>
#include <crucible/Expose.h>
#include <crucible/Result.h>

#include <cstdint>
#include <span>

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

// Hands an asset's bytes over, valid only during the call.
using AssetDelivery = void (*)(void* context, const uint8_t* data, uint64_t size);

struct AssetSource
{
    // Calls deliver with the asset's bytes and returns 0, or returns -1 if
    // it has no such asset.
    int32_t (*read)(const AssetId* id, void* context, AssetDelivery deliver);
};

// Replaces the source; until one is installed, there are no assets.
CRUCIBLE_EXPORT void installAssetSource(const AssetSource& source);
// The installed source (one finding nothing before any is).
CRUCIBLE_EXPORT const AssetSource& assetSource();
// Whether a source was installed: a default (a game's imported files) is
// installed only if a module hasn't installed its own.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE_INTERNAL bool assetSourceInstalled();

// C#'s source: an [UnmanagedCallersOnly] function as an address (the
// generator doesn't do function pointers), with AssetSource::read's
// signature. Internal to the engine.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE_INTERNAL void setAssetReader(uint64_t readerAddress);

// Reading through the source from C#: the asset's size (its bytes kept for
// copyAsset), or UINT64_MAX if the source has no such asset; then its
// bytes, as many as fit, returning how many were copied.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE_INTERNAL uint64_t assetSize(AssetId id);
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE_INTERNAL uint64_t copyAsset(AssetId id, std::span<uint8_t> out);

} // namespace crucible::assets
