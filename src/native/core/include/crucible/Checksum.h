#pragma once

// CRC32C (Castagnoli), the checksum of Crucible's binary sections (ADR 0039):
// chosen over plain CRC32 because x64 and ARM64 CPUs compute it in hardware.
// Names are placeholders (docs/naming.md).

#include <cstddef>
#include <cstdint>
#include <span>

namespace crucible {

// The CRC32C of `data`. Pass a previous result as `crc` to continue it over
// more data.
uint32_t crc32c(std::span<const std::byte> data, uint32_t crc = 0);

// The table-driven form every platform can run; crc32c uses the CPU's
// instruction where there is one. Exposed so tests compare the two.
uint32_t crc32cPortable(std::span<const std::byte> data, uint32_t crc = 0);

} // namespace crucible
