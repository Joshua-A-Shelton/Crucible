#pragma once

// The shader compiler (ADR 0025, 0047): Slang source to every format a target
// platform loads (DXIL needs DXC beside Slang), for every entry point, plus
// the layouts of the [GpuShared] structs it declares, checked to be the same
// on D3D12 and Vulkan and as constant-buffer and structured-buffer members.
// Used by shaderfax at build time and by importers in the editor. Editor-only.

#include <crucible/Result.h>
#include <crucible/Shader.h>

#include <cstdint>
#include <span>
#include <string_view>

namespace crucible::importers {

struct ShaderCompileOptions
{
    std::span<const std::string_view> searchPaths; // where `import` finds modules
    // What to produce, in order of preference: the target platform's formats
    // (ADR 0047). Layouts are checked for both D3D12 and Vulkan regardless.
    std::span<const render::ShaderBinaryFormat> formats;
};

enum class SharedFieldKind : uint32_t
{
    Float,
    Int,
    Uint, // bool too: 4 bytes in shaders
};

// One member of a [GpuShared] struct: a scalar, a float vector (columns 2-4),
// or a float4x4 (rows 4, columns 4). Nested structs are refused for now
// (ADR 0047: an NVIDIA driver bug).
struct SharedField
{
    std::string_view name;
    uint32_t offset;
    uint32_t size;
    SharedFieldKind kind;
    uint32_t rows;
    uint32_t columns;
};

struct CompiledShader;

// Compiles one module. InvalidData with Slang's diagnostics in
// lastImportError() if it doesn't compile, uses a type a shared struct can't
// hold, or lays a shared struct out differently anywhere.
Result<CompiledShader*> compileShader(std::string_view path, std::string_view source,
                                      const ShaderCompileOptions& options);
void destroyCompiledShader(CompiledShader* shader);

uint32_t compiledEntryCount(const CompiledShader& shader);
std::string_view compiledEntryName(const CompiledShader& shader, uint32_t index);

// The SHDR file (crucible/Shader.h): one code section per format asked for.
Result<void> writeShaderFile(const CompiledShader& shader, std::string_view path);

uint32_t sharedStructCount(const CompiledShader& shader);
std::string_view sharedStructName(const CompiledShader& shader, uint32_t index);
uint32_t sharedStructSize(const CompiledShader& shader, uint32_t index);
std::span<const SharedField> sharedStructFields(const CompiledShader& shader, uint32_t index);

// Slang's warnings, if it had any.
std::string_view compileWarnings(const CompiledShader& shader);

} // namespace crucible::importers
