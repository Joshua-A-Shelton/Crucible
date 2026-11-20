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
    bool unlit; // left as drawn by 2D lights (ADR 0113)
    // Which of the project's sorting layers it's in; gameplay changes it to
    // move it between floors.
    CRUCIBLE_HINT_SORTING_LAYER uint32_t sortingLayer;
    int32_t orderInLayer; // within the layer, higher draws on top
};

// How a PixelCamera's image fills a window that isn't a whole multiple of
// its resolution (ADR 0106).
enum class CRUCIBLE_EXPOSE PixelFit : uint32_t
{
    Sharp, // fill it: the largest whole-number scale, then the rest blending only pixel edges
    Exact, // whole-number scales only, black bars around
};

// The 2D camera (ADR 0106): sprites are drawn at its virtual resolution and
// scaled up to the window, everything on the art's grid. Placed by its
// entity's transform (looking along +Z, Y up), snapped to the grid. Not a
// render::Camera, so the 3D keeps its own. Without one, sprites draw with
// the main camera at the window's resolution. C# gives a new one 320x180
// at 16 pixels a meter.
struct CRUCIBLE_EXPOSE PixelCamera
{
    uint32_t width; // the virtual resolution, in pixels
    uint32_t height;
    float pixelsPerUnit; // virtual pixels a meter
    PixelFit fit;
};

// Frame animation (ADR 0107), the 2D counterpart of animation::Animator:
// plays a .crspriteclip on its entity's Sprite, setting its texture and
// region each frame, or lets a .crstatemachine choose the clips. The
// machine's parameters are set, and its events read, with the calls 3D
// characters use (animation::setFloat, animation::events, ...). Played by
// "Animation.Update". C# gives a new one a speed of 1.
struct CRUCIBLE_EXPOSE SpriteAnimator
{
    CRUCIBLE_HINT_ASSET(".crspriteclip") assets::AssetId clip; // played when there's no state machine
    float speed;                                               // 1 as authored; 0 holds the frame
    CRUCIBLE_HINT_ASSET(".crstatemachine") assets::AssetId stateMachine;
};

// Registers Sprite, PixelCamera, and SpriteAnimator. Registering again is
// harmless.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE_INTERNAL void registerSprite();

// Which sorting layers y-sort: bit N for layer N (up to 32 layers), from
// the project's settings (C#'s RuntimeSettings). Main thread.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE void setYSortLayers(uint32_t mask);
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE uint32_t ySortLayers();

// Which sorting layers 2D lights leave as drawn (ADR 0113): bit N for
// layer N.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE void setUnlitLayers(uint32_t mask);
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE uint32_t unlitLayers();

// Which sorting layers draw behind the 3D (ADR 0106): bit N for layer N.
// Through a PixelCamera, they show only where the 3D drew nothing.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE void setBehind3DLayers(uint32_t mask);
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE uint32_t behind3DLayers();

} // namespace crucible::render2d
