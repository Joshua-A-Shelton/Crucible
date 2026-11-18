#pragma once

// Reading a game's cooked assets (ADR 0099): an archive written by the
// editor's cooker (crucible/PackFile.h, ADR 0098), opened once, its index
// and scene lists kept in memory, each asset read from the file when asked.
// Installed as the asset source (ADR 0076), it serves the renderer, prefabs,
// and everything else by ID. Reads are positional, so any thread may read
// at once (the source is threadSafe).

#include <crucible/AssetSource.h>
#include <crucible/Export.h>
#include <crucible/Expose.h>
#include <crucible/PackFile.h>
#include <crucible/Result.h>
#include <crucible/Vector.h>

#include <cstdint>
#include <span>
#include <string_view>

namespace crucible::assets {

class CRUCIBLE_EXPORT ArchiveReader
{
public:
    // Opens the archive and reads its header, index, and scene lists: NotFound
    // or IoError for the file, InvalidData if it is damaged, Unsupported if
    // it is another version (cook again).
    static Result<ArchiveReader*> open(std::string_view utf8Path);
    // Closes it; it must not be the installed source any more. Null is ignored.
    static void close(ArchiveReader* reader);

    // The asset's bytes, as the Cache holds them (an imported file, or a
    // scene's or prefab's text): NotFound if the archive hasn't it.
    // Thread-safe.
    Result<void> read(const AssetId& id, Vector<uint8_t>& out) const;
    // Whether it holds the asset.
    bool contains(const AssetId& id) const;
    // The scene the game starts with; all zeros if none.
    AssetId startScene() const;
    // What the scene needs, to load it ahead (ADR 0099); empty if the
    // archive has no such scene.
    std::span<const AssetId> sceneAssets(const AssetId& scene) const;
    // How many assets it holds.
    uint32_t count() const;

    ArchiveReader(const ArchiveReader&) = delete;
    ArchiveReader& operator=(const ArchiveReader&) = delete;

private:
    ArchiveReader();
    ~ArchiveReader();
    const PackEntry* find(const AssetId& id) const;

    void* file = nullptr;
    PackHeader header{};
    Vector<PackEntry> entries{MemoryTag::Assets};
    Vector<PackScene> scenes{MemoryTag::Assets};
    Vector<AssetId> sceneIds{MemoryTag::Assets};
};

// Installs the archive as the asset source (threadSafe). It must stay open
// while installed.
CRUCIBLE_EXPORT void installArchiveSource(const ArchiveReader& reader);

// C#'s Archive: the reader as an address (internal to the engine).
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE_INTERNAL Result<uint64_t> openArchive(std::string_view utf8Path);
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE_INTERNAL void closeArchive(uint64_t archive);
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE_INTERNAL AssetId archiveStartScene(uint64_t archive);
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE_INTERNAL uint32_t archiveAssetCount(uint64_t archive);
// Copies up to into.size() of the scene's assets; returns how many it has.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE_INTERNAL uint32_t archiveSceneAssets(uint64_t archive, AssetId scene,
                                                                     std::span<AssetId> into);
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE_INTERNAL void installArchive(uint64_t archive);

} // namespace crucible::assets
