#pragma once

// The sky (ADR 0055): drawn after Opaque wherever nothing else is. Shared
// with C# as Crucible.Render.Sky, saved in scenes; registered with the
// lights (registerLights). One per world: with several, the one with the
// lowest entity index; with none, the sky is renderFrame's clear color.
//
// A gradient for now: zenithColor straight up, horizonColor at the horizon,
// groundColor below it, times intensity (linear, the lights' units). The
// sun's disk sits toward the first directional light, in its color.

#include <crucible/Expose.h>
#include <crucible/Math.h>

namespace crucible::render {

struct CRUCIBLE_EXPOSE Sky
{
    CRUCIBLE_HINT_COLOR Vector3 zenithColor;
    CRUCIBLE_HINT_COLOR Vector3 horizonColor;
    CRUCIBLE_HINT_COLOR Vector3 groundColor;
    float intensity;
    CRUCIBLE_HINT_RANGE(0, 0.5f) float sunSize; // the disk's angular diameter, in radians; 0 for none
    float sunIntensity;                         // times the sun's color with its intensity
};

} // namespace crucible::render
