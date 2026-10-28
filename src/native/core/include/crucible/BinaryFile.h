#pragma once

// Crucible's binary files for imported data (ADR 0021, 0039): a header, a
// table of sections, and each section's bytes, aligned, compressed with zstd
// where that helps, and checked with a CRC32C. Payloads are plain fixed-size
// little-endian data with offsets instead of pointers, so a section is used
// as it is once read. Imported data is a cache: a file from another version
// is rejected and reimported, never migrated. Names are placeholders
// (docs/naming.md).
//
//     Writer* writer = createWriter(tag("TEXT"), 1);
//     addSection(*writer, tag("MIP0"), 1, pixels, 16);
//     writeFile(*writer, path, Compression::Fast);
//
//     Result<File*> file = openFile(path, tag("TEXT"), 1);
//     int32_t mip = findSection(*file.value(), tag("MIP0"));
//     readSection(*file.value(), mip, destination);

#include <crucible/Expose.h>
#include <crucible/Result.h>
#include <crucible/Vector.h>

#include <cstddef>
#include <cstdint>
#include <span>
#include <string_view>

namespace crucible::binary {

// A four-letter tag ("MESH", "MIP0") as a number: the letters' bytes in file
// order, so a hex dump reads them.
constexpr uint32_t tag(const char (&letters)[5])
{
    return static_cast<uint32_t>(static_cast<uint8_t>(letters[0])) |
           static_cast<uint32_t>(static_cast<uint8_t>(letters[1])) << 8 |
           static_cast<uint32_t>(static_cast<uint8_t>(letters[2])) << 16 |
           static_cast<uint32_t>(static_cast<uint8_t>(letters[3])) << 24;
}

inline constexpr uint32_t fileMagic = tag("CRBN");
inline constexpr uint32_t containerVersion = 1;
inline constexpr uint32_t maxSections = 4096;
inline constexpr uint32_t maxAlignment = 4096;
// C# reads sections into spans, which are indexed by int.
inline constexpr uint64_t maxSectionSize = 0x7FFF'FFFF;

// How a section is stored.
enum class CRUCIBLE_EXPOSE Codec : uint32_t
{
    Raw = 0,
    Zstd = 1,
};

// How hard writeFile compresses. Every level decompresses at the same speed.
enum class CRUCIBLE_EXPOSE Compression : uint32_t
{
    None,  // stored raw
    Fast,  // zstd level 3: the import cache, written on every import
    Small, // zstd level 19: shipped data, written once
};

// On disk, little-endian (every target is). The table's CRC32C covers every
// SectionEntry; each entry's covers its section's uncompressed bytes.
struct FileHeader
{
    uint32_t magic;            // fileMagic
    uint32_t containerVersion; // this layout's version
    uint32_t kind;             // what the file holds: tag("MESH"), tag("TEXT"), ...
    uint32_t kindVersion;      // that kind's layout version
    uint32_t sectionCount;
    uint32_t tableCrc;
    uint64_t fileSize;
};
static_assert(sizeof(FileHeader) == 32);

struct SectionEntry
{
    uint32_t tag;
    uint32_t version;   // the section's own layout version
    uint32_t codec;     // Codec
    uint32_t alignment; // of the payload in the file and of its destination
    uint64_t offset;    // from the start of the file
    uint64_t storedSize;
    uint64_t size; // uncompressed
    uint32_t crc;  // CRC32C of the uncompressed bytes
    uint32_t reserved;
};
static_assert(sizeof(SectionEntry) == 48);

// What a reader needs to know about a section before reading it.
struct CRUCIBLE_EXPOSE SectionInfo
{
    uint32_t tag;
    uint32_t version;
    uint64_t size;
    uint32_t alignment;
    Codec codec;
};

// ---------------------------------------------------------------------------
// Writing
// ---------------------------------------------------------------------------

struct CRUCIBLE_EXPOSE Writer;

CRUCIBLE_EXPOSE Writer* createWriter(uint32_t kind, uint32_t kindVersion);
CRUCIBLE_EXPOSE void destroyWriter(Writer* writer);

// Copies the data into the writer. `alignment` is a power of two up to
// maxAlignment; InvalidData otherwise, or if there are too many sections or
// the data is too large.
CRUCIBLE_EXPOSE Result<void> addSection(Writer& writer, uint32_t tag, uint32_t version, std::span<const uint8_t> data,
                                        uint32_t alignment);

// Compresses the sections (one job each) and writes the file atomically.
CRUCIBLE_EXPOSE Result<void> writeFile(Writer& writer, std::string_view utf8Path, Compression compression);

// The same, into memory: for tests and archives.
Result<void> writeToMemory(Writer& writer, Compression compression, Vector<std::byte>& file);

// ---------------------------------------------------------------------------
// Reading
// ---------------------------------------------------------------------------

struct CRUCIBLE_EXPOSE File;

// Reads and validates the file: its header, table, and every section's
// bounds (sections' contents are checked when read). Fails with NotFound or
// IoError for the file itself, InvalidData if it is damaged or of another
// kind, and Unsupported if it has another container or kind version: stale
// data to reimport.
CRUCIBLE_EXPOSE Result<File*> openFile(std::string_view utf8Path, uint32_t kind, uint32_t kindVersion);

// The same from bytes in memory, which are copied.
CRUCIBLE_EXPOSE Result<File*> parseFile(std::span<const uint8_t> bytes, uint32_t kind, uint32_t kindVersion);

CRUCIBLE_EXPOSE void closeFile(File* file);

CRUCIBLE_EXPOSE uint32_t sectionCount(const File& file);
CRUCIBLE_EXPOSE SectionInfo sectionInfo(const File& file, uint32_t index);
// The first section with the tag, or -1.
CRUCIBLE_EXPOSE int32_t findSection(const File& file, uint32_t tag);

// Decompresses the section into `destination`, which must be its size, and
// checks its CRC32C: InvalidData if it doesn't match. A file is never
// changed after opening, so any thread may read sections at once.
CRUCIBLE_EXPOSE Result<void> readSection(const File& file, uint32_t index, std::span<uint8_t> destination);

struct SectionRead
{
    uint32_t index;
    std::span<std::byte> destination;
    Status status; // set by readSections
};

// Reads several sections, one job each (ADR 0014), and returns when all are
// done; each read's status says how it went.
void readSections(const File& file, std::span<SectionRead> reads);

// A raw section where it lies in the file's buffer, after checking its
// CRC32C: no copy. Unsupported for a compressed section.
Result<std::span<const std::byte>> rawSection(const File& file, uint32_t index);

} // namespace crucible::binary
