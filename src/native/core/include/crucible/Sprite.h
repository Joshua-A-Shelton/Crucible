#pragma once

// Sprites (ADR 0104): pictures drawn at an entity's Transform2D, for the
// pixel-art 2D path (ADR 0005). The Sprites render step draws them over the
// finished image, after Tonemap, so their colors are exactly the texture's,
// sorted by sorting layer, then order in layer, then (in layers that y-sort)
// lower on screen in front. Shared with C# as Crucible.Render2D.Sprite.

#include <crucible/Export.h>
#include <crucible/Expose.h>
#include <crucible/Math.h>
#include <crucible/RenderAssets.h>

#include <cstdint>

namespace crucible::render2d {

// The part of a texture a sprite shows, in pixels from its top-left corner.
struct CRUCIBLE_EXPOSE SpriteRegion
{
    uint32_t x;
    uint32_t y;
    uint32_t width; // 0 with height: the whole texture
    uint32_t height;
};

// A sprite's size in meters is its region's in pixels over its texture's
// pixels per unit (an import setting, 16 by default). C# gives a new one a
// centered pivot and a white tint.
struct CRUCIBLE_EXPOSE Sprite
{
    CRUCIBLE_HINT_ASSET(".crtexture") assets::AssetId texture;
    SpriteRegion region;
    // The point of the picture on the entity's position: (0, 0) its
    // bottom-left corner, (1, 1) its top-right.
    Vector2 pivot;
    CRUCIBLE_HINT_COLOR Vector4 tint; // multiplied in; white leaves it as it is
    bool flipX;
    bool flipY;
    // Which of the project's sorting layers it's in; gameplay changes it to
    // move it between floors.
    CRUCIBLE_HINT_SORTING_LAYER uint32_t sortingLayer;
    int32_t orderInLayer; // within the layer, higher draws on top
};

// Registers Sprite. Registering again is harmless.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE_INTERNAL void registerSprite();

// Which sorting layers y-sort: bit N for layer N (up to 32 layers), from
// the project's settings (C#'s RuntimeSettings). Main thread.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE void setYSortLayers(uint32_t mask);
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE uint32_t ySortLayers();

} // namespace crucible::render2d
