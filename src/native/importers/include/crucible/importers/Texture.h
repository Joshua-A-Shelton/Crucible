#pragma once

// Texture import (ADR 0022, 0042): decode a source image, convert it to
// linear light, make mipmaps with a Kaiser-windowed sinc, and compress every
// mip to its GPU format, block by block on the job system. The C# texture
// importer writes the result as a binary file (ADR 0039). Editor-only.
// Names are placeholders (docs/naming.md).

#include <crucible/Expose.h>
#include <crucible/Result.h>
#include <crucible/TextureFile.h>

#include <cstdint>
#include <span>
#include <string_view>

namespace crucible::importers {

// The file's layout is the core's (TextureFile.h): the runtime reads it too.
using assets::TextureFormat;
using assets::TextureInfo;

// What a texture is for, which decides its format and how it's filtered.
enum class CRUCIBLE_EXPOSE TextureUsage : uint32_t
{
    Color,         // sRGB colour, with or without alpha: BC7, sRGB
    Data,          // linear values (roughness, masks, flow): BC7, linear
    NormalMap,     // tangent-space normals: BC5 (X and Y; Z is rebuilt), renormalized per mip
    SingleChannel, // one linear channel (height, occlusion), from red: BC4
    Hdr,           // linear high dynamic range: RGBA16F, uncompressed
    Ui,            // UI and pixel art: RGBA8 sRGB, uncompressed, exact
};

struct CRUCIBLE_EXPOSE TextureSettings
{
    TextureUsage usage;
    bool mipmaps;               // a full chain down to 1x1, or the top level only
    bool wrap;                  // filter across edges as if tiled (else clamp)
    bool preserveAlphaCoverage; // keep the share of texels with alpha >= alphaCutoff in every mip
    float alphaCutoff;          // for alpha-tested surfaces (foliage, fences)
    float rdoLambda;            // BC7 rate-distortion trade-off; 0 turns it off
    uint32_t quality;           // BC7 encoder effort, 0 (fast) to 4 (best)
};

struct CRUCIBLE_EXPOSE TextureImport;

// Imports the image in `source` (PNG, JPEG, TGA, or Radiance HDR).
// InvalidData if it can't, with the reason in lastImportError().
CRUCIBLE_EXPOSE Result<TextureImport*> importTexture(std::span<const uint8_t> source, TextureSettings settings);
CRUCIBLE_EXPOSE void destroyTextureImport(TextureImport* texture);

CRUCIBLE_EXPOSE TextureInfo textureInfo(const TextureImport& texture);
// A mip's encoded bytes: its blocks (or texels) row by row.
CRUCIBLE_EXPOSE uint64_t textureMipSize(const TextureImport& texture, uint32_t mip);
CRUCIBLE_EXPOSE void copyTextureMip(const TextureImport& texture, uint32_t mip, std::span<uint8_t> destination);

// Why the last import on this thread failed.
CRUCIBLE_EXPOSE std::string_view lastImportError();

// Decodes encoded texels back to RGBA8 (BC4 and BC5 fill the channels they
// have, the rest 0, alpha 255; RGBA16F isn't decoded): for previews, and for
// tests measuring quality. InvalidData if the sizes don't match.
CRUCIBLE_EXPOSE Result<void> decodeTexels(TextureFormat format, uint32_t width, uint32_t height,
                                          std::span<const uint8_t> encoded, std::span<uint8_t> rgba);

} // namespace crucible::importers
