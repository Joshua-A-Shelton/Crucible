#pragma once

// Lights (ADR 0053): components placed by their entity's WorldTransform.
// Directional and spot lights shine along the entity's +Z; point and spot
// lights sit at its position. Shared with C# as Crucible.Render.*, saved in
// scenes.
//
// Units are unitless and π-normalized, as the shading models expect: a
// white light of intensity 1, facing a white Lambert surface, lights it to
// 1. Point and spot lights fall off with inverse square, smoothly windowed
// to zero at `range`.

#include <crucible/Expose.h>
#include <crucible/Math.h>

namespace crucible::render {

struct CRUCIBLE_EXPOSE DirectionalLight
{
    Vector3 color; // linear
    float intensity;
};

struct CRUCIBLE_EXPOSE PointLight
{
    Vector3 color;
    float intensity;
    float range; // meters: no light past it
};

// The cone's angles are full angles, in radians: inside innerConeAngle the
// light is at full strength, and it fades to nothing at outerConeAngle.
struct CRUCIBLE_EXPOSE SpotLight
{
    Vector3 color;
    float intensity;
    float range;
    float innerConeAngle;
    float outerConeAngle;
};

// Light that isn't from a light: skyColor from above, groundColor from below,
// blended by a surface's normal. One per world: with several, the one with
// the lowest entity index; with none, no ambient light.
struct CRUCIBLE_EXPOSE AmbientLight
{
    Vector3 skyColor;
    Vector3 groundColor;
    float intensity;
};

// The renderer's limits: directional lights past maxDirectionalLights and
// visible point and spot lights past maxVisibleLights are left out, and a
// cluster keeps at most maxLightsPerCluster (ADR 0053).
inline constexpr uint32_t maxDirectionalLights = 4;
inline constexpr uint32_t maxVisibleLights = 1024;
inline constexpr uint32_t maxLightsPerCluster = 64;

// Registers the components. Registering again is harmless.
CRUCIBLE_EXPOSE_INTERNAL void registerLights();

} // namespace crucible::render
