#pragma once

// Tilesets and tilemaps as imported (ADR 0108).
//
// A tileset, .crtls: written by the Tileset importer from an authored
// .crtileset (JSON). A TSET file (kind tag("TSET"), version 1):
//   INFO  TilesetInfo
// The tiles' shapes stay in the authored file: the Tilemap importer reads
// them from there and bakes each map's collision, so nothing at run time
// reads a tileset.
//
// A tilemap, .crtmap: written by the Tilemap importer from an authored
// .crtilemap (JSON), with everything drawing and collision need, so the
// runtime reads nothing else. A TMAP file (kind tag("TMAP"), version 1):
//   INFO  TilemapInfo
//   CELL  TilemapCell, one per painted cell
//   LOOP  TilemapLoop, one per collision outline (optional)
//   PNTS  Vector2, the outlines' points, in meters in the map's space
//
// Cell (x, y) covers [x, x + 1) by [y, y + 1) tiles from the map's origin,
// y up. Outlines run counterclockwise around solid ground and clockwise
// around holes in it (as chains do, ChainFile.h), always closed.

#include <crucible/AssetSource.h>
#include <crucible/BinaryFile.h>
#include <crucible/Expose.h>

#include <cstdint>

namespace crucible::assets {

inline constexpr uint32_t tilesetFileKind = binary::tag("TSET");
inline constexpr uint32_t tilesetFileVersion = 1;
inline constexpr uint32_t tilemapFileKind = binary::tag("TMAP");
inline constexpr uint32_t tilemapFileVersion = 1;

struct CRUCIBLE_EXPOSE TilesetInfo
{
    AssetId texture;
    uint32_t tileWidth; // pixels
    uint32_t tileHeight;
    uint32_t columns; // tiles across the texture
    uint32_t tileCount;
};

struct CRUCIBLE_EXPOSE TilemapInfo
{
    AssetId texture;     // its tileset's
    uint32_t tileWidth;  // pixels
    uint32_t tileHeight; // pixels
    float cellWidth;     // meters: the tile's pixels over the texture's pixels per unit
    float cellHeight;
    uint32_t cellCount;
    uint32_t loopCount;
    uint32_t pointCount;
    uint32_t pad;
};

// Bits of TilemapCell::flags.
enum class CRUCIBLE_EXPOSE TileFlip : uint32_t
{
    X = 1,
    Y = 2,
};

struct CRUCIBLE_EXPOSE TilemapCell
{
    int32_t x;
    int32_t y;
    uint32_t pixelX; // the tile's top-left corner in the texture
    uint32_t pixelY;
    uint32_t flags; // TileFlip bits
    uint32_t pad;
};

struct CRUCIBLE_EXPOSE TilemapLoop
{
    uint32_t first; // into PNTS
    uint32_t count; // at least 3
};

} // namespace crucible::assets
