#pragma once

// The imported frame animation, .crspriteclip (ADR 0107): written by the
// texture importer, one per clip in a sprite sheet's import settings, as a
// sub-asset of the sheet ("Hero.png / Walk"); read by SpriteAnimator.
//
// A SPCL file (kind tag("SPCL"), version 1):
//   INFO  SpriteClipInfo
//   FRMS  SpriteClipFrame, one per frame, in playing order
//   EVNT  ClipEvent, by time (optional), as 3D clips have them (ClipFile.h)
// Each frame is baked to its region of the sheet, so the runtime never
// slices; a frame's events are at its start.

#include <crucible/AssetSource.h>
#include <crucible/BinaryFile.h>
#include <crucible/Expose.h>

#include <cstdint>

namespace crucible::assets {

inline constexpr uint32_t spriteClipFileKind = binary::tag("SPCL");
inline constexpr uint32_t spriteClipFileVersion = 1;

struct CRUCIBLE_EXPOSE SpriteClipInfo
{
    AssetId texture; // the sheet it was cut from
    float duration;  // seconds, its frames' durations summed
    uint32_t frameCount;
    uint32_t loop; // 1 to start over at its end, played alone
    uint32_t pad;
};

struct CRUCIBLE_EXPOSE SpriteClipFrame
{
    uint32_t x; // the region, in pixels from the sheet's top-left corner
    uint32_t y;
    uint32_t width;
    uint32_t height;
    float start; // seconds from the clip's start
    float duration;
};

} // namespace crucible::assets
