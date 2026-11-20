#pragma once

// 2D lights (ADR 0113): light for the pixel-art 2D path's sprites and
// tiles. Each frame the Sprites step adds the lights into a light buffer the
// size of the image the sprites draw into (a pixel camera's virtual
// resolution, else the view's), starting from the ambient light; a lit
// sprite's colors are multiplied by the buffer at its pixel, up to twice
// the art's. Light changes a pixel at a time, so it stays on the pixel grid.
// Without an AmbientLight2D the ambient is white: the art as drawn, lights
// only brightening. Light multiplies the art's linear colors (as the GPU
// decodes its sRGB), as 3D lights do, so a light's color is linear; under
// white light the art is exactly as drawn.
// A sorting layer marked Unlit (the project's settings), or a Sprite or
// Tilemap with `unlit`, isn't lit. Shared with C# as Crucible.Render2D.

#include <crucible/Expose.h>
#include <crucible/Math.h>

#include <cstdint>

namespace crucible::render2d {

// A circle of light at the entity, fading to nothing at `range` meters.
// `bands` 0 fades smoothly; more steps the fade into that many flat rings.
// C# gives a new one white light, intensity 1, and a range of 3 m.
struct CRUCIBLE_EXPOSE PointLight2D
{
    CRUCIBLE_HINT_COLOR Vector3 color; // linear
    float intensity;
    float range; // meters: no light past it
    CRUCIBLE_HINT_RANGE(0, 16) int32_t bands;
};

// A point light within a cone shining along the entity's local down (-Y),
// so a lamp shines down unturned. The cone's angles are full angles, in
// radians, as SpotLight's (ADR 0053): full strength inside innerConeAngle,
// nothing outside outerConeAngle.
struct CRUCIBLE_EXPOSE SpotLight2D
{
    CRUCIBLE_HINT_COLOR Vector3 color; // linear
    float intensity;
    float range;
    CRUCIBLE_HINT_RANGE(0, 6.28318f) float innerConeAngle;
    CRUCIBLE_HINT_RANGE(0, 6.28318f) float outerConeAngle;
    CRUCIBLE_HINT_RANGE(0, 16) int32_t bands;
};

// The light everything lit starts from (a night's dark blue). One per
// world: with several, the one with the lowest entity index.
struct CRUCIBLE_EXPOSE AmbientLight2D
{
    CRUCIBLE_HINT_COLOR Vector3 color; // linear
    float intensity;
};

} // namespace crucible::render2d
