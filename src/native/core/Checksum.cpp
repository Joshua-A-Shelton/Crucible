#include <crucible/Checksum.h>

#include <array>
#include <cstring>

#if defined(__x86_64__) || defined(_M_X64)
// Crucible's x64 baseline (x86-64-v3, ADR 0024's amendment) includes SSE4.2.
#define CRUCIBLE_CRC32C_X64 1
#include <immintrin.h>
#elif (defined(__aarch64__) || defined(_M_ARM64)) && defined(__ARM_FEATURE_CRC32)
#define CRUCIBLE_CRC32C_ARM64 1
#include <arm_acle.h>
#endif

namespace crucible {

namespace {

// The reflected Castagnoli polynomial.
constexpr uint32_t polynomial = 0x82F63B78u;

constexpr std::array<uint32_t, 256> makeTable()
{
    std::array<uint32_t, 256> table{};
    for (uint32_t i = 0; i < 256; ++i)
    {
        uint32_t crc = i;
        for (int bit = 0; bit < 8; ++bit)
        {
            crc = (crc & 1) != 0 ? (crc >> 1) ^ polynomial : crc >> 1;
        }
        table[i] = crc;
    }
    return table;
}

constexpr std::array<uint32_t, 256> table = makeTable();

} // namespace

uint32_t crc32cPortable(std::span<const std::byte> data, uint32_t crc)
{
    crc = ~crc;
    for (std::byte value : data)
    {
        crc = table[(crc ^ static_cast<uint32_t>(value)) & 0xFF] ^ (crc >> 8);
    }
    return ~crc;
}

uint32_t crc32c(std::span<const std::byte> data, uint32_t crc)
{
#if defined(CRUCIBLE_CRC32C_X64) || defined(CRUCIBLE_CRC32C_ARM64)
    // Eight bytes per instruction, then the tail a byte at a time.
    uint64_t state = static_cast<uint32_t>(~crc); // zero-extended: the instruction works on 64 bits
    const std::byte* bytes = data.data();
    size_t remaining = data.size();
    while (remaining >= 8)
    {
        uint64_t word;
        std::memcpy(&word, bytes, 8);
#if defined(CRUCIBLE_CRC32C_X64)
        state = _mm_crc32_u64(state, word);
#else
        state = __crc32cd(static_cast<uint32_t>(state), word);
#endif
        bytes += 8;
        remaining -= 8;
    }
    uint32_t crc32 = static_cast<uint32_t>(state);
    for (; remaining > 0; --remaining, ++bytes)
    {
#if defined(CRUCIBLE_CRC32C_X64)
        crc32 = _mm_crc32_u8(crc32, static_cast<uint8_t>(*bytes));
#else
        crc32 = __crc32cb(crc32, static_cast<uint8_t>(*bytes));
#endif
    }
    return ~crc32;
#else
    return crc32cPortable(data, crc);
#endif
}

} // namespace crucible
