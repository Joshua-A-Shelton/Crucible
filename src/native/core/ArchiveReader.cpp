// Reading a game's cooked assets (ADR 0099): the archive's file kept open,
// its index and scene lists in memory, each asset read at its offset.

#include <crucible/ArchiveReader.h>
#include <crucible/Log.h>
#include <crucible/Memory.h>

#include <algorithm>
#include <cstring>
#include <new>

#if defined(_WIN32)
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#else
#include <cerrno>
#include <fcntl.h>
#include <sys/stat.h>
#include <unistd.h>
#endif

namespace crucible::assets {

namespace {

// The file, read at offsets so threads don't share a position.
#if defined(_WIN32)
Result<void*> openForReading(std::string_view utf8Path)
{
    const int length = MultiByteToWideChar(CP_UTF8, 0, utf8Path.data(), static_cast<int>(utf8Path.size()), nullptr, 0);
    Vector<wchar_t> wide(MemoryTag::Assets);
    wide.resize(static_cast<size_t>(length) + 1);
    MultiByteToWideChar(CP_UTF8, 0, utf8Path.data(), static_cast<int>(utf8Path.size()), wide.data(), length);
    wide[static_cast<size_t>(length)] = 0;
    HANDLE file =
        CreateFileW(wide.data(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE)
    {
        const DWORD error = GetLastError();
        return error == ERROR_FILE_NOT_FOUND || error == ERROR_PATH_NOT_FOUND ? Status::NotFound : Status::IoError;
    }
    return static_cast<void*>(file);
}

void closeForReading(void* file)
{
    CloseHandle(static_cast<HANDLE>(file));
}

// With a synchronous handle and an OVERLAPPED offset, ReadFile reads there
// whatever another thread does with the handle.
Result<void> readAt(void* file, uint64_t offset, std::span<uint8_t> into)
{
    size_t done = 0;
    while (done < into.size())
    {
        OVERLAPPED at{};
        at.Offset = static_cast<DWORD>(offset + done);
        at.OffsetHigh = static_cast<DWORD>((offset + done) >> 32);
        const DWORD wanted = static_cast<DWORD>((std::min)(into.size() - done, size_t{1} << 30));
        DWORD read = 0;
        if (!ReadFile(static_cast<HANDLE>(file), into.data() + done, wanted, &read, &at) || read == 0)
        {
            return Status::IoError;
        }
        done += read;
    }
    return {};
}
#else
Result<void*> openForReading(std::string_view utf8Path)
{
    Vector<char> path(MemoryTag::Assets);
    path.assign(std::span<const char>(utf8Path.data(), utf8Path.size()));
    path.pushBack(0);
    const int file = ::open(path.data(), O_RDONLY | O_CLOEXEC);
    if (file < 0)
    {
        return errno == ENOENT ? Status::NotFound : Status::IoError;
    }
    // Stored as the descriptor plus one, so a null pointer means none.
    return reinterpret_cast<void*>(static_cast<intptr_t>(file) + 1);
}

int descriptor(void* file)
{
    return static_cast<int>(reinterpret_cast<intptr_t>(file) - 1);
}

void closeForReading(void* file)
{
    ::close(descriptor(file));
}

Result<void> readAt(void* file, uint64_t offset, std::span<uint8_t> into)
{
    size_t done = 0;
    while (done < into.size())
    {
        const ssize_t read =
            ::pread(descriptor(file), into.data() + done, into.size() - done, static_cast<off_t>(offset + done));
        if (read < 0 && errno == EINTR)
        {
            continue;
        }
        if (read <= 0)
        {
            return Status::IoError;
        }
        done += static_cast<size_t>(read);
    }
    return {};
}
#endif

// Asset IDs as the index sorts them: their bytes, unsigned.
int compare(const uint8_t* a, const uint8_t* b)
{
    return std::memcmp(a, b, 16);
}

const ArchiveReader* installedArchive = nullptr;

int32_t readInstalled(const AssetId* id, void* context, AssetDelivery deliver)
{
    if (installedArchive == nullptr)
    {
        return -1;
    }
    Vector<uint8_t> bytes(MemoryTag::Assets);
    if (!installedArchive->read(*id, bytes))
    {
        return -1;
    }
    deliver(context, bytes.data(), bytes.size());
    return 0;
}

ArchiveReader* fromAddress(uint64_t archive)
{
    return reinterpret_cast<ArchiveReader*>(static_cast<uintptr_t>(archive));
}

} // namespace

ArchiveReader::ArchiveReader() = default;

ArchiveReader::~ArchiveReader()
{
    if (file != nullptr)
    {
        closeForReading(file);
    }
}

Result<ArchiveReader*> ArchiveReader::open(std::string_view utf8Path)
{
    Result<void*> opened = openForReading(utf8Path);
    if (!opened)
    {
        return opened.status();
    }
    ArchiveReader* reader =
        new (allocate(sizeof(ArchiveReader), alignof(ArchiveReader), MemoryTag::Assets)) ArchiveReader();
    reader->file = opened.value();
    auto fail = [reader](Status status) -> Result<ArchiveReader*> {
        close(reader);
        return status;
    };

    PackHeader& header = reader->header;
    if (!readAt(reader->file, 0, {reinterpret_cast<uint8_t*>(&header), sizeof(header)}))
    {
        return fail(Status::InvalidData);
    }
    if (header.kind != packFileKind)
    {
        return fail(Status::InvalidData);
    }
    if (header.version != packFileVersion)
    {
        return fail(Status::Unsupported);
    }
    // The index and scene lists, which end the file, read at once and checked.
    const uint64_t indexBytes = uint64_t{header.entryCount} * sizeof(PackEntry);
    const uint64_t sceneBytes = uint64_t{header.sceneCount} * sizeof(PackScene);
    if (header.indexOffset < sizeof(PackHeader) || header.indexOffset > header.fileSize ||
        indexBytes + sceneBytes > header.fileSize - header.indexOffset)
    {
        return fail(Status::InvalidData);
    }
    const uint64_t idBytes = header.fileSize - header.indexOffset - indexBytes - sceneBytes;
    if (idBytes % sizeof(AssetId) != 0)
    {
        return fail(Status::InvalidData);
    }
    reader->entries.resize(header.entryCount);
    reader->scenes.resize(header.sceneCount);
    reader->sceneIds.resize(static_cast<size_t>(idBytes / sizeof(AssetId)));
    if (!readAt(reader->file, header.indexOffset,
                {reinterpret_cast<uint8_t*>(reader->entries.data()), static_cast<size_t>(indexBytes)}) ||
        !readAt(reader->file, header.indexOffset + indexBytes,
                {reinterpret_cast<uint8_t*>(reader->scenes.data()), static_cast<size_t>(sceneBytes)}) ||
        !readAt(reader->file, header.indexOffset + indexBytes + sceneBytes,
                {reinterpret_cast<uint8_t*>(reader->sceneIds.data()), static_cast<size_t>(idBytes)}))
    {
        return fail(Status::InvalidData);
    }
    for (size_t i = 0; i < reader->entries.size(); ++i)
    {
        const PackEntry& entry = reader->entries[i];
        if (entry.offset > header.indexOffset || entry.size > header.indexOffset - entry.offset ||
            (i > 0 && compare(reader->entries[i - 1].id, entry.id) >= 0))
        {
            return fail(Status::InvalidData);
        }
    }
    for (size_t i = 0; i < reader->scenes.size(); ++i)
    {
        const PackScene& scene = reader->scenes[i];
        if (uint64_t{scene.first} + scene.count > reader->sceneIds.size() ||
            (i > 0 && compare(reader->scenes[i - 1].id, scene.id) >= 0))
        {
            return fail(Status::InvalidData);
        }
    }
    return reader;
}

void ArchiveReader::close(ArchiveReader* reader)
{
    if (reader == nullptr)
    {
        return;
    }
    if (installedArchive == reader)
    {
        log::warning("an archive was closed while installed as the asset source");
        installedArchive = nullptr;
    }
    reader->~ArchiveReader();
    crucible::free(reader, sizeof(ArchiveReader), MemoryTag::Assets);
}

const PackEntry* ArchiveReader::find(const AssetId& id) const
{
    size_t low = 0, high = entries.size();
    while (low < high)
    {
        const size_t middle = low + (high - low) / 2;
        const int order = compare(entries[middle].id, id.bytes);
        if (order == 0)
        {
            return &entries[middle];
        }
        if (order < 0)
        {
            low = middle + 1;
        }
        else
        {
            high = middle;
        }
    }
    return nullptr;
}

Result<void> ArchiveReader::read(const AssetId& id, Vector<uint8_t>& out) const
{
    const PackEntry* entry = find(id);
    if (entry == nullptr)
    {
        return Status::NotFound;
    }
    out.resize(static_cast<size_t>(entry->size));
    return readAt(file, entry->offset, out.span());
}

bool ArchiveReader::contains(const AssetId& id) const
{
    return find(id) != nullptr;
}

AssetId ArchiveReader::startScene() const
{
    AssetId id;
    std::memcpy(id.bytes, header.startScene, 16);
    return id;
}

std::span<const AssetId> ArchiveReader::sceneAssets(const AssetId& scene) const
{
    size_t low = 0, high = scenes.size();
    while (low < high)
    {
        const size_t middle = low + (high - low) / 2;
        const int order = compare(scenes[middle].id, scene.bytes);
        if (order == 0)
        {
            return sceneIds.span().subspan(scenes[middle].first, scenes[middle].count);
        }
        if (order < 0)
        {
            low = middle + 1;
        }
        else
        {
            high = middle;
        }
    }
    return {};
}

uint32_t ArchiveReader::count() const
{
    return static_cast<uint32_t>(entries.size());
}

void installArchiveSource(const ArchiveReader& reader)
{
    installedArchive = &reader;
    installAssetSource({.read = readInstalled, .threadSafe = true});
}

Result<uint64_t> openArchive(std::string_view utf8Path)
{
    Result<ArchiveReader*> reader = ArchiveReader::open(utf8Path);
    if (!reader)
    {
        return reader.status();
    }
    return static_cast<uint64_t>(reinterpret_cast<uintptr_t>(reader.value()));
}

void closeArchive(uint64_t archive)
{
    ArchiveReader::close(fromAddress(archive));
}

AssetId archiveStartScene(uint64_t archive)
{
    return fromAddress(archive)->startScene();
}

uint32_t archiveAssetCount(uint64_t archive)
{
    return fromAddress(archive)->count();
}

uint32_t archiveSceneAssets(uint64_t archive, AssetId scene, std::span<AssetId> into)
{
    const std::span<const AssetId> assets = fromAddress(archive)->sceneAssets(scene);
    std::memcpy(into.data(), assets.data(), (std::min)(into.size(), assets.size()) * sizeof(AssetId));
    return static_cast<uint32_t>(assets.size());
}

void installArchive(uint64_t archive)
{
    installArchiveSource(*fromAddress(archive));
}

} // namespace crucible::assets
