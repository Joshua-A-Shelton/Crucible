// Binary files for imported data and their checksum (ADR 0039).

#include <crucible/BinaryFile.h>
#include <crucible/Checksum.h>
#include <crucible/Jobs.h>
#include <crucible/Memory.h>
#include <crucible/Platform.h>

#include <doctest/doctest.h>

#include <cstring>
#include <filesystem>
#include <random>
#include <string>
#include <system_error>
#include <vector>

using namespace crucible;
using namespace crucible::binary;

namespace {

std::span<const std::byte> bytesOf(const std::string& text)
{
    return std::as_bytes(std::span(text.data(), text.size()));
}

std::span<const uint8_t> view(const std::vector<uint8_t>& bytes)
{
    return {bytes.data(), bytes.size()};
}

// Compressible (a repeating pattern), incompressible (random), and empty.
struct Contents
{
    std::vector<uint8_t> pattern;
    std::vector<uint8_t> noise;
    std::vector<uint8_t> empty;

    Contents()
    {
        std::mt19937 random(7);
        pattern.resize(1 << 20);
        for (size_t i = 0; i < pattern.size(); ++i)
        {
            pattern[i] = static_cast<uint8_t>((i * 7) % 61);
        }
        noise.resize(65536);
        for (uint8_t& value : noise)
        {
            value = static_cast<uint8_t>(random());
        }
    }
};

constexpr uint32_t kind = tag("TEST");

Vector<std::byte> makeFile(const Contents& contents, Compression compression)
{
    Writer* writer = createWriter(kind, 3);
    REQUIRE(addSection(*writer, tag("PATT"), 1, view(contents.pattern), 16));
    REQUIRE(addSection(*writer, tag("NOIS"), 2, view(contents.noise), 4096));
    REQUIRE(addSection(*writer, tag("NONE"), 1, view(contents.empty), 256));
    Vector<std::byte> file(MemoryTag::Assets);
    REQUIRE(writeToMemory(*writer, compression, file));
    destroyWriter(writer);
    return file;
}

std::span<const uint8_t> view(const Vector<std::byte>& file)
{
    return {reinterpret_cast<const uint8_t*>(file.data()), file.size()};
}

Status openStatus(std::span<const uint8_t> bytes, uint32_t expectedKind = kind, uint32_t version = 3)
{
    Result<File*> file = parseFile(bytes, expectedKind, version);
    if (file)
    {
        closeFile(file.value());
    }
    return file.status();
}

// Reads every section; Ok only if all read and match the originals.
Status readAll(File& file, const Contents& contents)
{
    const std::vector<uint8_t>* originals[] = {&contents.pattern, &contents.noise, &contents.empty};
    for (uint32_t i = 0; i < 3; ++i)
    {
        std::vector<uint8_t> out(sectionInfo(file, i).size);
        if (Result<void> read = readSection(file, i, {out.data(), out.size()}); !read)
        {
            return read.status();
        }
        if (out != *originals[i])
        {
            return Status::Unsupported; // read "fine" but wrong: the worst outcome
        }
    }
    return Status::Ok;
}

SectionEntry* tableOf(Vector<std::byte>& file)
{
    return reinterpret_cast<SectionEntry*>(file.data() + sizeof(FileHeader));
}

// Rewrites the table's CRC after an edit, so only validation can catch it.
void resealTable(Vector<std::byte>& file)
{
    FileHeader header;
    std::memcpy(&header, file.data(), sizeof(header));
    header.tableCrc = crc32c(std::as_bytes(std::span(tableOf(file), header.sectionCount)));
    std::memcpy(file.data(), &header, sizeof(header));
}

} // namespace

TEST_CASE("CRC32C matches the standard check value and its portable form")
{
    CHECK(crc32c(bytesOf("123456789")) == 0xE3069283u);
    CHECK(crc32c({}) == 0);
    std::mt19937 random(3);
    std::vector<std::byte> data(1000);
    for (std::byte& value : data)
    {
        value = static_cast<std::byte>(random());
    }
    for (size_t length = 0; length <= data.size(); length += 37)
    {
        const std::span<const std::byte> part(data.data(), length);
        CHECK(crc32c(part) == crc32cPortable(part));
        // Continuing over a split gives the CRC of the whole.
        const size_t half = length / 2;
        CHECK(crc32c(part.subspan(half), crc32c(part.first(half))) == crc32c(part));
    }
}

TEST_CASE("Binary files round-trip at every compression level")
{
    const Contents contents;
    for (Compression compression : {Compression::None, Compression::Fast, Compression::Small})
    {
        CAPTURE(static_cast<int>(compression));
        Vector<std::byte> bytes = makeFile(contents, compression);
        Result<File*> opened = parseFile(view(bytes), kind, 3);
        REQUIRE(opened);
        File& file = *opened.value();
        REQUIRE(sectionCount(file) == 3);
        CHECK(findSection(file, tag("NOIS")) == 1);
        CHECK(findSection(file, tag("ZZZZ")) == -1);
        const SectionInfo pattern = sectionInfo(file, 0);
        CHECK(pattern.tag == tag("PATT"));
        CHECK(pattern.version == 1);
        CHECK(pattern.size == contents.pattern.size());
        // The pattern shrinks; noise doesn't, so it stays raw at every level.
        CHECK(pattern.codec == (compression == Compression::None ? Codec::Raw : Codec::Zstd));
        CHECK(sectionInfo(file, 1).codec == Codec::Raw);
        CHECK(sectionInfo(file, 2).codec == Codec::Raw);
        CHECK(readAll(file, contents) == Status::Ok);

        // Raw sections are used where they lie, at their alignment.
        Result<std::span<const std::byte>> noise = rawSection(file, 1);
        REQUIRE(noise);
        CHECK(reinterpret_cast<uintptr_t>(noise.value().data()) % 4096 == 0);
        CHECK(std::memcmp(noise.value().data(), contents.noise.data(), contents.noise.size()) == 0);
        CHECK(rawSection(file, 0).status() == (compression == Compression::None ? Status::Ok : Status::Unsupported));
        closeFile(&file);

        if (compression != Compression::None)
        {
            CHECK(bytes.size() < contents.pattern.size() / 4 + contents.noise.size() + 8192);
        }
    }
}

TEST_CASE("A smaller level gives a smaller file")
{
    const Contents contents;
    CHECK(makeFile(contents, Compression::Small).size() <= makeFile(contents, Compression::Fast).size());
}

TEST_CASE("Damaged and stale files are refused")
{
    const Contents contents;
    const Vector<std::byte> good = makeFile(contents, Compression::Fast);
    const uint64_t pattern = tableOf(const_cast<Vector<std::byte>&>(good))[0].offset;
    const uint64_t noise = tableOf(const_cast<Vector<std::byte>&>(good))[1].offset;

    auto damaged = [&](size_t at) {
        Vector<std::byte> copy = good.copy();
        copy[at] ^= std::byte{0x40};
        return copy;
    };

    CHECK(openStatus(view(good)) == Status::Ok);
    CHECK(openStatus(view(damaged(0))) == Status::InvalidData);                       // magic
    CHECK(openStatus(view(damaged(sizeof(FileHeader) + 20))) == Status::InvalidData); // table
    CHECK(openStatus(view(good).first(good.size() - 1)) == Status::InvalidData);      // truncated
    CHECK(openStatus({}) == Status::InvalidData);
    CHECK(openStatus(view(good), tag("MESH")) == Status::InvalidData); // another kind
    CHECK(openStatus(view(good), kind, 2) == Status::Unsupported);     // stale: reimport

    Vector<std::byte> otherContainer = good.copy();
    otherContainer[4] = std::byte{2};
    CHECK(openStatus(view(otherContainer)) == Status::Unsupported);

    // Payloads open fine (their bounds are right) and fail when read.
    for (uint64_t at : {pattern + 100, noise + 100})
    {
        Vector<std::byte> copy = damaged(at);
        Result<File*> file = parseFile(view(copy), kind, 3);
        REQUIRE(file);
        CHECK(readAll(*file.value(), contents) == Status::InvalidData);
        closeFile(file.value());
    }
}

TEST_CASE("Section tables pointing outside the file are refused")
{
    const Contents contents;
    const Vector<std::byte> good = makeFile(contents, Compression::Fast);
    auto edited = [&](auto edit) {
        Vector<std::byte> copy = good.copy();
        edit(tableOf(copy)[1]);
        resealTable(copy);
        return openStatus(view(copy));
    };
    CHECK(edited([&](SectionEntry& entry) {
              entry.offset = good.size();
              entry.storedSize = 1;
          }) == Status::InvalidData);
    CHECK(edited([](SectionEntry& entry) { entry.storedSize = ~0ull; }) == Status::InvalidData);
    CHECK(edited([](SectionEntry& entry) { entry.offset = 0; }) == Status::InvalidData);  // over the header
    CHECK(edited([](SectionEntry& entry) { entry.offset += 1; }) == Status::InvalidData); // misaligned
    CHECK(edited([](SectionEntry& entry) { entry.alignment = 3; }) == Status::InvalidData);
    CHECK(edited([](SectionEntry& entry) { entry.alignment = 8192; }) == Status::InvalidData);
    CHECK(edited([](SectionEntry& entry) { entry.codec = 7; }) == Status::InvalidData);
    CHECK(edited([](SectionEntry& entry) { entry.size = entry.storedSize + 1; }) == Status::InvalidData); // raw
    CHECK(edited([](SectionEntry& entry) { entry.reserved = 1; }) == Status::InvalidData);

    Vector<std::byte> tooMany = good.copy();
    FileHeader header;
    std::memcpy(&header, tooMany.data(), sizeof(header));
    header.sectionCount = maxSections + 1;
    std::memcpy(tooMany.data(), &header, sizeof(header));
    CHECK(openStatus(view(tooMany)) == Status::InvalidData);
}

TEST_CASE("Random damage never reads back wrong data")
{
    const Contents contents;
    const Vector<std::byte> good = makeFile(contents, Compression::Fast);
    std::mt19937 random(11);
    int refused = 0;
    for (int round = 0; round < 300; ++round)
    {
        Vector<std::byte> copy = good.copy();
        for (int flip = 1 + static_cast<int>(random() % 4); flip > 0; --flip)
        {
            copy[random() % copy.size()] ^= static_cast<std::byte>(1u << (random() % 8));
        }
        Result<File*> file = parseFile(view(copy), kind, 3);
        if (!file)
        {
            ++refused;
            continue;
        }
        const Status status = readAll(*file.value(), contents);
        // Damage either changes nothing that is read, or is caught.
        CHECK(status != Status::Unsupported);
        refused += status != Status::Ok ? 1 : 0;
        closeFile(file.value());
    }
    CHECK(refused > 250); // almost every flip lands in checked bytes
}

TEST_CASE("Binary files are written atomically and read back from disk")
{
    std::error_code error;
    const std::filesystem::path folder = std::filesystem::temp_directory_path(error) / "crucible-binary-test";
    std::filesystem::remove_all(folder, error);
    REQUIRE(std::filesystem::create_directories(folder, error));
    const std::string path = (folder / "asset.bin").string();

    const Contents contents;
    Writer* writer = createWriter(kind, 3);
    REQUIRE(addSection(*writer, tag("PATT"), 1, view(contents.pattern), 16));
    REQUIRE(addSection(*writer, tag("NOIS"), 2, view(contents.noise), 4096));
    REQUIRE(addSection(*writer, tag("NONE"), 1, view(contents.empty), 256));
    CHECK(addSection(*writer, tag("BAD!"), 1, view(contents.empty), 24).status() == Status::InvalidData);
    REQUIRE(writeFile(*writer, path, Compression::Fast));
    REQUIRE(writeFile(*writer, path, Compression::Fast)); // replacing works too
    destroyWriter(writer);

    // Only the file itself is left: no temporary names.
    int entries = 0;
    for (const auto& entry : std::filesystem::directory_iterator(folder, error))
    {
        (void)entry;
        ++entries;
    }
    CHECK(entries == 1);

    Result<File*> file = openFile(path, kind, 3);
    REQUIRE(file);
    CHECK(readAll(*file.value(), contents) == Status::Ok);
    closeFile(file.value());

    CHECK(openFile((folder / "missing.bin").string(), kind, 3).status() == Status::NotFound);
    CHECK(platform::fileSize((folder / "missing.bin").string()).status() == Status::NotFound);
    std::filesystem::remove_all(folder, error);
}

TEST_CASE("Sections read in parallel and memory is returned")
{
    REQUIRE(jobs::initialize(3));
    const MemoryUsage before = memoryUsage(MemoryTag::Assets);
    {
        std::vector<std::vector<uint8_t>> originals(64);
        Writer* writer = createWriter(kind, 1);
        for (uint32_t i = 0; i < originals.size(); ++i)
        {
            originals[i].assign(10'000 + i * 1000, static_cast<uint8_t>(i));
            REQUIRE(addSection(*writer, tag("SECT"), i, view(originals[i]), 16));
        }
        Vector<std::byte> bytes(MemoryTag::Assets);
        REQUIRE(writeToMemory(*writer, Compression::Fast, bytes));
        destroyWriter(writer);

        Result<File*> file = parseFile(view(bytes), kind, 1);
        REQUIRE(file);
        std::vector<std::vector<std::byte>> outputs(originals.size());
        std::vector<SectionRead> reads;
        for (uint32_t i = 0; i < originals.size(); ++i)
        {
            outputs[i].resize(sectionInfo(*file.value(), i).size + (i == 5 ? 1 : 0)); // one wrong size
            reads.push_back({i, outputs[i], Status::Unsupported});
        }
        readSections(*file.value(), reads);
        for (uint32_t i = 0; i < originals.size(); ++i)
        {
            CAPTURE(i);
            if (i == 5)
            {
                CHECK(reads[i].status == Status::InvalidData);
                continue;
            }
            REQUIRE(reads[i].status == Status::Ok);
            CHECK(std::memcmp(outputs[i].data(), originals[i].data(), originals[i].size()) == 0);
        }
        closeFile(file.value());
    }
    // zstd's contexts and every buffer went through the allocator and back.
    const MemoryUsage after = memoryUsage(MemoryTag::Assets);
    CHECK(after.bytes == before.bytes);
    CHECK(after.allocations == before.allocations);
    jobs::shutdown();
}
