#pragma once

// Tilemaps (ADR 0108): one layer of tiles from a .crtilemap, drawn at an
// entity's Transform2D by the Sprites step, sorted like a sprite (as one
// item, or row by row in a layer that y-sorts). A Collider2D of shape
// Tilemap on the same entity collides with its tiles' shapes. Shared with
// C# as Crucible.Render2D.Tilemap.

#include <crucible/AssetSource.h>
#include <crucible/Export.h>
#include <crucible/Expose.h>
#include <crucible/Math.h>

#include <cstdint>

namespace crucible::render2d {

// C# gives a new one a white tint.
struct CRUCIBLE_EXPOSE Tilemap
{
    CRUCIBLE_HINT_ASSET(".crtmap") assets::AssetId map;
    CRUCIBLE_HINT_COLOR Vector4 tint; // multiplied in; white leaves it as it is
    CRUCIBLE_HINT_SORTING_LAYER uint32_t sortingLayer;
    int32_t orderInLayer; // within the layer, higher draws on top
};

// Forgets a tilemap read before (the editor after painting): drawn from the
// new one next frame. True if it was loaded.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE bool reloadTilemap(assets::AssetId map);

} // namespace crucible::render2d
