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

// The engine's material types, MaterialInfo::type when shader is all zero.
enum class CRUCIBLE_EXPOSE DefaultMaterialType : uint32_t
{
    Toon,
    Pbr,
};

} // namespace crucible::assets
