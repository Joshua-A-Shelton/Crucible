#include <crucible/BinaryFile.h>

#include <crucible/Checksum.h>
#include <crucible/Jobs.h>
#include <crucible/Memory.h>
#include <crucible/Platform.h>

#define ZSTD_STATIC_LINKING_ONLY // ZSTD_customMem: zstd allocates through Crucible's allocator
#include <zstd.h>

#include <cstring>

namespace crucible::binary {

namespace {

// zstd's free gives no size and Crucible's needs one, so each block keeps its
// size in front of it.
constexpr size_t sizePrefix = 16;

void* zstdAllocate(void*, size_t size)
{
    auto* block = static_cast<std::byte*>(tryAllocate(size + sizePrefix, 16, MemoryTag::Assets));
    if (block == nullptr)
    {
        return nullptr;
    }
    std::memcpy(block, &size, sizeof(size));
    return block + sizePrefix;
}

void zstdFree(void*, void* address)
{
    if (address == nullptr)
    {
        return;
    }
    std::byte* block = static_cast<std::byte*>(address) - sizePrefix;
    size_t size;
    std::memcpy(&size, block, sizeof(size));
    free(block, size + sizePrefix, MemoryTag::Assets);
}

constexpr ZSTD_customMem zstdMemory{zstdAllocate, zstdFree, nullptr};

int levelOf(Compression compression)
{
    return compression == Compression::Small ? 19 : 3;
}

bool isPowerOfTwo(uint32_t value)
{
    return value != 0 && (value & (value - 1)) == 0;
}

uint64_t alignUp(uint64_t value, uint64_t alignment)
{
    return (value + alignment - 1) & ~(alignment - 1);
}

} // namespace

// ---------------------------------------------------------------------------
// Writing
// ---------------------------------------------------------------------------

struct PendingSection
{
    uint32_t tag;
    uint32_t version;
    uint32_t alignment;
    Vector<std::byte> data{MemoryTag::Assets};
    // Filled while writing.
    Vector<std::byte> compressed{MemoryTag::Assets};
    Codec codec = Codec::Raw;
    uint32_t crc = 0;
    bool failed = false;
};

struct Writer
{
    uint32_t kind;
    uint32_t kindVersion;
    Vector<PendingSection*> sections{MemoryTag::Assets};
};

Writer* createWriter(uint32_t kind, uint32_t kindVersion)
{
    Writer* writer = create<Writer>(MemoryTag::Assets);
    writer->kind = kind;
    writer->kindVersion = kindVersion;
    return writer;
}

void destroyWriter(Writer* writer)
{
    if (writer == nullptr)
    {
        return;
    }
    for (PendingSection* section : writer->sections)
    {
        destroy(MemoryTag::Assets, section);
    }
    destroy(MemoryTag::Assets, writer);
}

Result<void> addSection(Writer& writer, uint32_t tag, uint32_t version, std::span<const uint8_t> data,
                        uint32_t alignment)
{
    if (!isPowerOfTwo(alignment) || alignment > maxAlignment || data.size() > maxSectionSize ||
        writer.sections.size() >= maxSections)
    {
        return Status::InvalidData;
    }
    PendingSection* section = create<PendingSection>(MemoryTag::Assets);
    section->tag = tag;
    section->version = version;
    section->alignment = alignment;
    section->data.assign(std::as_bytes(data));
    writer.sections.pushBack(section);
    return {};
}

namespace {

struct CompressJob
{
    Writer* writer;
    Compression compression;
};

// One job per section: checksum it, and compress it unless that doesn't
// make it smaller.
void compressSections(void* data, uint32_t begin, uint32_t end)
{
    const CompressJob& job = *static_cast<const CompressJob*>(data);
    for (uint32_t i = begin; i < end; ++i)
    {
        PendingSection& section = *job.writer->sections[i];
        section.crc = crc32c(section.data.span());
        section.codec = Codec::Raw;
        if (job.compression == Compression::None || section.data.empty())
        {
            continue;
        }
        ZSTD_CCtx* context = ZSTD_createCCtx_advanced(zstdMemory);
        if (context == nullptr)
        {
            section.failed = true;
            continue;
        }
        section.compressed.resize(ZSTD_compressBound(section.data.size()));
        const size_t size = ZSTD_compressCCtx(context, section.compressed.data(), section.compressed.size(),
                                              section.data.data(), section.data.size(), levelOf(job.compression));
        ZSTD_freeCCtx(context);
        if (ZSTD_isError(size))
        {
            section.failed = true;
            continue;
        }
        if (size < section.data.size())
        {
            section.compressed.resize(size);
            section.codec = Codec::Zstd;
        }
        else
        {
            section.compressed.clear(); // stored raw: it didn't shrink
        }
    }
}

} // namespace

Result<void> writeToMemory(Writer& writer, Compression compression, Vector<std::byte>& file)
{
    CompressJob job{&writer, compression};
    jobs::parallelFor(static_cast<uint32_t>(writer.sections.size()), 1, compressSections, &job);

    const uint32_t count = static_cast<uint32_t>(writer.sections.size());
    Vector<SectionEntry> table(MemoryTag::Assets);
    table.resize(count);
    uint64_t offset = sizeof(FileHeader) + uint64_t{count} * sizeof(SectionEntry);
    for (uint32_t i = 0; i < count; ++i)
    {
        const PendingSection& section = *writer.sections[i];
        if (section.failed)
        {
            return Status::OutOfHostMemory;
        }
        const bool compressed = section.codec == Codec::Zstd;
        offset = alignUp(offset, section.alignment);
        table[i] = {
            .tag = section.tag,
            .version = section.version,
            .codec = static_cast<uint32_t>(section.codec),
            .alignment = section.alignment,
            .offset = offset,
            .storedSize = compressed ? section.compressed.size() : section.data.size(),
            .size = section.data.size(),
            .crc = section.crc,
            .reserved = 0,
        };
        offset += table[i].storedSize;
    }

    const FileHeader header{
        .magic = fileMagic,
        .containerVersion = containerVersion,
        .kind = writer.kind,
        .kindVersion = writer.kindVersion,
        .sectionCount = count,
        .tableCrc = crc32c(std::as_bytes(table.span())),
        .fileSize = offset,
    };
    file.clear();
    file.resize(offset); // zeroed, so the padding is too
    std::memcpy(file.data(), &header, sizeof(header));
    std::memcpy(file.data() + sizeof(header), table.data(), table.span().size_bytes());
    for (uint32_t i = 0; i < count; ++i)
    {
        const PendingSection& section = *writer.sections[i];
        const Vector<std::byte>& stored = section.codec == Codec::Zstd ? section.compressed : section.data;
        if (!stored.empty())
        {
            std::memcpy(file.data() + table[i].offset, stored.data(), stored.size());
        }
    }
    return {};
}

Result<void> writeFile(Writer& writer, std::string_view utf8Path, Compression compression)
{
    Vector<std::byte> file(MemoryTag::Assets);
    if (Result<void> written = writeToMemory(writer, compression, file); !written)
    {
        return written;
    }
    return platform::writeFileAtomically(utf8Path, file.span());
}

// ---------------------------------------------------------------------------
// Reading
// ---------------------------------------------------------------------------

struct File
{
    // The whole file, aligned for the largest section alignment allowed, so
    // a raw section can be used where it lies.
    std::byte* bytes = nullptr;
    uint64_t size = 0;
    FileHeader header{};
    const SectionEntry* table = nullptr;
};

namespace {

File* allocateFile(uint64_t size)
{
    File* file = create<File>(MemoryTag::Assets);
    file->size = size;
    file->bytes = static_cast<std::byte*>(tryAllocate(size == 0 ? 1 : size, maxAlignment, MemoryTag::Assets));
    if (file->bytes == nullptr)
    {
        destroy(MemoryTag::Assets, file);
        return nullptr;
    }
    return file;
}

// Everything a reader relies on before touching a section: nothing in the
// file is trusted until it passes.
Status validate(File& file, uint32_t kind, uint32_t kindVersion)
{
    if (file.size < sizeof(FileHeader))
    {
        return Status::InvalidData;
    }
    std::memcpy(&file.header, file.bytes, sizeof(FileHeader));
    const FileHeader& header = file.header;
    if (header.magic != fileMagic || header.fileSize != file.size)
    {
        return Status::InvalidData;
    }
    if (header.containerVersion != containerVersion)
    {
        return Status::Unsupported;
    }
    if (header.kind != kind)
    {
        return Status::InvalidData;
    }
    if (header.kindVersion != kindVersion)
    {
        return Status::Unsupported;
    }
    const uint64_t tableEnd = sizeof(FileHeader) + uint64_t{header.sectionCount} * sizeof(SectionEntry);
    if (header.sectionCount > maxSections || tableEnd > file.size)
    {
        return Status::InvalidData;
    }
    file.table = reinterpret_cast<const SectionEntry*>(file.bytes + sizeof(FileHeader));
    const std::span<const SectionEntry> table(file.table, header.sectionCount);
    if (crc32c(std::as_bytes(table)) != header.tableCrc)
    {
        return Status::InvalidData;
    }
    for (const SectionEntry& entry : table)
    {
        const bool knownCodec =
            entry.codec == static_cast<uint32_t>(Codec::Raw) || entry.codec == static_cast<uint32_t>(Codec::Zstd);
        if (!knownCodec || !isPowerOfTwo(entry.alignment) || entry.alignment > maxAlignment ||
            entry.offset % entry.alignment != 0 || entry.offset < tableEnd || entry.offset > file.size ||
            entry.storedSize > file.size - entry.offset || entry.size > maxSectionSize || entry.reserved != 0 ||
            (entry.codec == static_cast<uint32_t>(Codec::Raw) && entry.storedSize != entry.size))
        {
            return Status::InvalidData;
        }
    }
    return Status::Ok;
}

Result<File*> finishOpening(File* file, uint32_t kind, uint32_t kindVersion)
{
    if (const Status status = validate(*file, kind, kindVersion); status != Status::Ok)
    {
        closeFile(file);
        return status;
    }
    return file;
}

} // namespace

Result<File*> openFile(std::string_view utf8Path, uint32_t kind, uint32_t kindVersion)
{
    const Result<uint64_t> size = platform::fileSize(utf8Path);
    if (!size)
    {
        return size.status();
    }
    File* file = allocateFile(size.value());
    if (file == nullptr)
    {
        return Status::OutOfHostMemory;
    }
    if (Result<void> read = platform::readFile(utf8Path, {file->bytes, file->size}); !read)
    {
        closeFile(file);
        return read.status();
    }
    return finishOpening(file, kind, kindVersion);
}

Result<File*> parseFile(std::span<const uint8_t> bytes, uint32_t kind, uint32_t kindVersion)
{
    File* file = allocateFile(bytes.size());
    if (file == nullptr)
    {
        return Status::OutOfHostMemory;
    }
    if (!bytes.empty())
    {
        std::memcpy(file->bytes, bytes.data(), bytes.size());
    }
    return finishOpening(file, kind, kindVersion);
}

void closeFile(File* file)
{
    if (file == nullptr)
    {
        return;
    }
    free(file->bytes, file->size == 0 ? 1 : file->size, MemoryTag::Assets);
    destroy(MemoryTag::Assets, file);
}

uint32_t sectionCount(const File& file)
{
    return file.header.sectionCount;
}

SectionInfo sectionInfo(const File& file, uint32_t index)
{
    if (index >= file.header.sectionCount)
    {
        return {};
    }
    const SectionEntry& entry = file.table[index];
    return {entry.tag, entry.version, entry.size, entry.alignment, static_cast<Codec>(entry.codec)};
}

int32_t findSection(const File& file, uint32_t tag)
{
    for (uint32_t i = 0; i < file.header.sectionCount; ++i)
    {
        if (file.table[i].tag == tag)
        {
            return static_cast<int32_t>(i);
        }
    }
    return -1;
}

Result<void> readSection(const File& file, uint32_t index, std::span<uint8_t> destination)
{
    if (index >= file.header.sectionCount || destination.size() != file.table[index].size)
    {
        return Status::InvalidData;
    }
    const SectionEntry& entry = file.table[index];
    const std::byte* stored = file.bytes + entry.offset;
    if (entry.codec == static_cast<uint32_t>(Codec::Raw))
    {
        if (entry.size != 0)
        {
            std::memcpy(destination.data(), stored, entry.size);
        }
    }
    else
    {
        ZSTD_DCtx* context = ZSTD_createDCtx_advanced(zstdMemory);
        if (context == nullptr)
        {
            return Status::OutOfHostMemory;
        }
        const size_t size =
            ZSTD_decompressDCtx(context, destination.data(), destination.size(), stored, entry.storedSize);
        ZSTD_freeDCtx(context);
        if (ZSTD_isError(size) || size != entry.size)
        {
            return Status::InvalidData;
        }
    }
    if (crc32c(std::as_bytes(destination)) != entry.crc)
    {
        return Status::InvalidData;
    }
    return {};
}

namespace {

struct ReadJob
{
    const File* file;
    std::span<SectionRead> reads;
};

void readSectionJobs(void* data, uint32_t begin, uint32_t end)
{
    const ReadJob& job = *static_cast<const ReadJob*>(data);
    for (uint32_t i = begin; i < end; ++i)
    {
        SectionRead& read = job.reads[i];
        const std::span<uint8_t> destination(reinterpret_cast<uint8_t*>(read.destination.data()),
                                             read.destination.size());
        read.status = readSection(*job.file, read.index, destination).status();
    }
}

} // namespace

void readSections(const File& file, std::span<SectionRead> reads)
{
    ReadJob job{&file, reads};
    jobs::parallelFor(static_cast<uint32_t>(reads.size()), 1, readSectionJobs, &job);
}

Result<std::span<const std::byte>> rawSection(const File& file, uint32_t index)
{
    if (index >= file.header.sectionCount)
    {
        return Status::InvalidData;
    }
    const SectionEntry& entry = file.table[index];
    if (entry.codec != static_cast<uint32_t>(Codec::Raw))
    {
        return Status::Unsupported;
    }
    const std::span<const std::byte> bytes(file.bytes + entry.offset, entry.size);
    if (crc32c(bytes) != entry.crc)
    {
        return Status::InvalidData;
    }
    return bytes;
}

} // namespace crucible::binary
