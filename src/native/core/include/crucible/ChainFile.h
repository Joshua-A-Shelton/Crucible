#pragma once

// The imported chain, .crchn (ADR 0095): written by the Chain importer from
// an authored .crchain (JSON), read by 2D physics for Chain colliders.
//
// A CHAN file (kind tag("CHAN"), version 1):
//   INFO  ChainInfo
//   PNTS  Vector2, one per point, in the entity's space
//
// The points run so the solid side is on their right (Box2D's winding):
// counterclockwise around a solid island, clockwise around a cave.

#include <crucible/BinaryFile.h>
#include <crucible/Expose.h>

#include <cstdint>

namespace crucible::assets {

inline constexpr uint32_t chainFileKind = binary::tag("CHAN");
inline constexpr uint32_t chainFileVersion = 1;

// The closest two neighboring points may be, in meters (Box2D's linear
// slop): closer, a segment is too short to collide with.
inline constexpr float minChainPointSpacing = 0.005f;

struct CRUCIBLE_EXPOSE ChainInfo
{
    uint32_t pointCount; // at least 2, or 3 for a loop
    uint32_t loop;       // 0 or 1: the last point joins the first
};

} // namespace crucible::assets
