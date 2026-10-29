#pragma once

// The imported texture file, .crtexture (ADR 0042, 0049): written by the
// texture importer, read by the runtime (RenderAssets.h). Names are in
// docs/naming.md.
//
// A TXTR file (kind tag("TXTR"), version 1):
//   INFO        TextureInfo
//   MIP0..MIPF  each mip's blocks or pixels, top level first, rows tightly
//               packed (block rows for compressed formats)

#include <crucible/BinaryFile.h>
#include <crucible/Expose.h>

#include <cstdint>

namespace crucible::assets {

inline constexpr uint32_t textureFileKind = binary::tag("TXTR");
inline constexpr uint32_t textureFileVersion = 1;

// The section holding a mip: MIP0 for the top level, then MIP1 … MIPF.
constexpr uint32_t textureMipTag(uint32_t mip)
{
    constexpr char digits[] = "0123456789ABCDEF";
    const char letters[5] = {'M', 'I', 'P', digits[mip & 15], '\0'};
    return binary::tag(letters);
}

enum class CRUCIBLE_EXPOSE TextureFormat : uint32_t
{
    Bc7,
    Bc5,
    Bc4,
    Rgba8,
    Rgba16F,
};

// The imported texture, as the binary file's INFO section stores it.
struct CRUCIBLE_EXPOSE TextureInfo
{
    TextureFormat format;
    uint32_t width;
    uint32_t height;
    uint32_t mipCount;
    uint32_t srgb; // 1 if the GPU should read it as sRGB
};

} // namespace crucible::assets
