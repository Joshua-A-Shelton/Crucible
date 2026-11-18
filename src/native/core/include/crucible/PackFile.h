#pragma once

// A game's cooked assets (ADR 0098): one archive, written by the editor's
// cooker (C#, Crucible.Assets' ArchiveWriter) and read at run time
// (crucible/ArchiveReader.h, ADR 0099).
//
//   PackHeader, at 0
//   each entry's bytes, starting on a packAlignment boundary
//   PackEntry[entryCount], at indexOffset, sorted by asset ID (its 16 bytes,
//     compared as unsigned bytes), so a reader finds one by binary search
//   PackScene[sceneCount], right after the index, sorted the same way
//   the scenes' asset IDs (16 bytes each), right after them
//
// An entry holds what the Cache holds for that asset: an imported file (a
// CRBN file, crucible/BinaryFile.h, its sections compressed at
// Compression::Small) or a file read as it is (a scene, a prefab).
// Little-endian, as every target is.

#include <cstdint>

#include <crucible/BinaryFile.h>

namespace crucible::assets {

inline constexpr uint32_t packFileKind = binary::tag("PACK");
inline constexpr uint32_t packFileVersion = 2;
// Entries start on page boundaries, so a range of one can be read or mapped
// on its own.
inline constexpr uint64_t packAlignment = 4096;

struct PackHeader
{
    uint32_t kind;    // packFileKind
    uint32_t version; // packFileVersion
    uint32_t entryCount;
    uint32_t sceneCount;
    uint64_t indexOffset; // where the PackEntry table starts
    uint64_t fileSize;
    uint8_t startScene[16]; // the scene the game starts with (its ID); zero if none
};
static_assert(sizeof(PackHeader) == 48);

struct PackEntry
{
    uint8_t id[16];     // the asset's ID, as AssetId stores it
    uint64_t offset;    // from the start of the file
    uint64_t size;      // in bytes
    char extension[16]; // its kind, as the Cache names it (".crmesh", ".crscene"), zero-padded
};
static_assert(sizeof(PackEntry) == 48);

// A scene and what it needs (ADR 0099): every asset it reaches, other scenes
// and what only they reach left out, so it can be loaded ahead.
struct PackScene
{
    uint8_t id[16];
    uint32_t first; // its first asset ID, counted in the array after the PackScenes
    uint32_t count;
};
static_assert(sizeof(PackScene) == 24);

} // namespace crucible::assets
