#pragma once

// Compiled shaders (ADR 0047): a binary file (ADR 0039) of kind SHDR, made by
// the shader compiler at build or import time, holding each entry point's
// DXIL and SPIR-V side by side. The runtime hands an entry point to Slag as a
// list of binaries, of which Slag uses the first its backend accepts. Names
// are placeholders (docs/naming.md).
//
// A SHDR file (version 1):
//   ENTS  ShaderEntryInfo, one per entry point
//   NAME  UTF-8 entry point names, by offset and length
//   DXIL  every entry point's DXIL, back to back (absent when built without it)
//   SPRV  every entry point's SPIR-V, back to back

#include <crucible/BinaryFile.h>
#include <crucible/Result.h>

#include <cstdint>
#include <span>
#include <string_view>

namespace slag {
struct ShaderStage;
} // namespace slag

namespace crucible::render {

inline constexpr uint32_t shaderFileKind = binary::tag("SHDR");
inline constexpr uint32_t shaderFileVersion = 1;

enum class ShaderStageKind : uint32_t
{
    Vertex,
    Pixel,
    Compute,
};

struct ShaderEntryInfo
{
    ShaderStageKind stage;
    uint32_t nameOffset; // into NAME
    uint32_t nameLength;
    uint32_t dxilOffset; // into DXIL; dxilSize 0 when the file has none
    uint32_t dxilSize;
    uint32_t spirvOffset; // into SPRV
    uint32_t spirvSize;
    uint32_t threadGroupSize[3]; // compute only
};
static_assert(sizeof(ShaderEntryInfo) == 40);

struct ShaderFile;

// Reads a SHDR file's bytes; InvalidData if they aren't one.
Result<ShaderFile*> loadShaderFile(std::span<const uint8_t> bytes);
void destroyShaderFile(ShaderFile* file);

uint32_t shaderEntryCount(const ShaderFile& file);
const ShaderEntryInfo& shaderEntry(const ShaderFile& file, uint32_t index);
std::string_view shaderEntryName(const ShaderFile& file, uint32_t index);
// -1 if there is no entry point of that name.
int32_t findShaderEntry(const ShaderFile& file, std::string_view name);

// The entry point as Slag takes it: DXIL first, then SPIR-V. Valid while the
// file is.
void shaderStage(const ShaderFile& file, uint32_t index, slag::ShaderStage& stage);

} // namespace crucible::render
