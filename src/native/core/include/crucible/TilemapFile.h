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
// runtime reads nothing else. A TMAP file (kind tag("TMAP"), version 2):
//   INFO  TilemapInfo
//   TXTR  AssetId, one per texture its tilesets use (ADR 0112)
//   LAYR  TilemapLayer, one per layer, in the map's order (ADR 0112)
//   CELL  TilemapCell, one per painted cell, layer by layer
//   LOOP  TilemapLoop, one per collision outline, layer by layer (optional)
//   PNTS  Vector2, the outlines' points, in meters in the map's space
//
// A Tilemap component draws one layer, named by its id; a Collider2D of
// shape Tilemap on the same entity collides with that layer's outlines.
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
inline constexpr uint32_t tilemapFileVersion = 2;

struct CRUCIBLE_EXPOSE TilesetInfo
{
    AssetId texture;
    uint32_t tileWidth; // pixels
    uint32_t tileHeight;
    uint32_t columns; // tiles across the texture
    uint32_t tileCount;
};

// Every tileset of a map has its tile size, and every texture its pixels
// per unit, so the map is one grid (ADR 0112).
struct CRUCIBLE_EXPOSE TilemapInfo
{
    uint32_t tileWidth;  // pixels
    uint32_t tileHeight; // pixels
    float cellWidth;     // meters: the tile's pixels over the textures' pixels per unit
    float cellHeight;
    uint32_t textureCount;
    uint32_t layerCount;
    uint32_t cellCount;
    uint32_t loopCount;
    uint32_t pointCount;
    uint32_t pad;
};

struct CRUCIBLE_EXPOSE TilemapLayer
{
    uint32_t id;        // the layer's own, kept while it's renamed or moved (Tilemap::layer)
    uint32_t firstCell; // into CELL
    uint32_t cellCount;
    uint32_t firstLoop; // into LOOP
    uint32_t loopCount;
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
    uint32_t flags;   // TileFlip bits
    uint32_t texture; // into TXTR
};

struct CRUCIBLE_EXPOSE TilemapLoop
{
    uint32_t first; // into PNTS
    uint32_t count; // at least 3
};

} // namespace crucible::assets
