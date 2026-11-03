#pragma once

// Material records (ADR 0052): a material as the runtime loads it. Which
// shader and material type it is, its pipeline state, its parameter bytes
// laid out as the shader reads them, and its textures by asset ID. The
// runtime writes each texture's resource-heap slot into the parameters at
// its offset when the material loads. Names are in docs/naming.md.
//
// A model's generated materials, one per slot, are its MESH file's MTLS
// section: a MaterialInfo per slot (MeshFileInfo::materialCount), then the
// data their offsets point into (offsets are from the section's start).
// M5 step 6b's .crmat files hold one record the same way.

#include <crucible/BinaryFile.h>
#include <crucible/Expose.h>

#include <cstdint>

namespace crucible::assets {

inline constexpr uint32_t meshMaterialsTag = binary::tag("MTLS");
// A MESH file's slots remapped to authored materials (M5 step 6b): a 16-byte
// asset ID per slot, all zero for a slot that keeps its generated material.
inline constexpr uint32_t meshRemappedMaterialsTag = binary::tag("MRMP");
// A .crmat file: kind MATR, one record in an MTLS section.
inline constexpr uint32_t materialFileKind = binary::tag("MATR");
inline constexpr uint32_t materialFileVersion = 1;

enum class CRUCIBLE_EXPOSE AlphaMode : uint32_t
{
    Opaque,
    Mask,  // alpha-tested against alphaCutoff
    Blend, // drawn as Opaque until the transparent pass (M5 step 9)
};

struct CRUCIBLE_EXPOSE MaterialInfo
{
    uint8_t shader[16]; // the material shader's asset ID; all zero for the engine's (default.slang)
    uint32_t type;      // which material type of the shader: for the engine's, 0 DefaultToon, 1 DefaultPbr
    AlphaMode alphaMode;
    uint32_t doubleSided; // 1: both faces drawn
    float alphaCutoff;
    uint32_t parameterOffset; // bytes from the section's start
    uint32_t parameterSize;
    uint32_t textureOffset; // bytes from the section's start: textureCount MaterialTextures
    uint32_t textureCount;
};

struct CRUCIBLE_EXPOSE MaterialTexture
{
    uint32_t offset;     // the TextureSlot's byte offset in the parameters
    uint8_t texture[16]; // the texture's asset ID
};

// A material shader's types (M5 step 6b): the MTYP section of its .crshader,
// which the Shader importer writes from Slang's reflection. The section
// holds a uint32_t type count and parameter count, the MaterialTypeInfos,
// the MaterialParameters, then each type's default parameter bytes and the
// names; offsets are from the section's start. A type's entry points are
// "<name>_forward" and "<name>_mask", beside the shared "materialVertex".
inline constexpr uint32_t shaderMaterialTypesTag = binary::tag("MTYP");

enum class CRUCIBLE_EXPOSE MaterialParameterKind : uint32_t
{
    Float,
    Int,
    Uint,
    Texture, // a TextureSlot: the texture's resource-heap slot
};

struct CRUCIBLE_EXPOSE MaterialTypeInfo
{
    uint32_t nameOffset;
    uint32_t nameLength;
    uint32_t size; // the parameters' bytes
    uint32_t firstParameter;
    uint32_t parameterCount;
    uint32_t defaultsOffset; // `size` bytes: field initializers, zero elsewhere
};

struct CRUCIBLE_EXPOSE MaterialParameter
{
    uint32_t nameOffset;
    uint32_t nameLength;
    uint32_t offset; // in the parameters
    MaterialParameterKind kind;
    uint32_t rows;        // 4 for a float4x4, else 1
    uint32_t columns;     // 1 for a scalar, 2-4 for a vector, 4 for a float4x4
    uint32_t arrayLength; // 0: not an array
    uint32_t arrayStride; // bytes between elements
};

// The engine's material types, MaterialInfo::type when shader is all zero.
enum class CRUCIBLE_EXPOSE DefaultMaterialType : uint32_t
{
    Toon,
    Pbr,
};

} // namespace crucible::assets
