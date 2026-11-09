#pragma once

// How a camera's image is finished (ADR 0055): exposure, bloom, tonemapping,
// and color grading, on the camera's entity beside Camera. Shared with C# as
// Crucible.Render.PostProcess, saved in scenes; registered with the camera
// (registerCamera).
//
// Components start zeroed, so every field is neutral at zero: a camera with
// a PostProcess left at zero looks the same as one without (no bloom, PBR
// Neutral tonemapping, no grading).

#include <crucible/Expose.h>
#include <crucible/Math.h>

#include <cstdint>

namespace crucible::render {

enum class CRUCIBLE_EXPOSE Tonemapper : uint32_t
{
    PbrNeutral, // Khronos PBR Neutral: authored colors stay as authored until very bright
    AgX,        // photographic, highlights desaturating toward white
    Aces,       // filmic, with ACES's contrast and highlight hue shifts
};

struct CRUCIBLE_EXPOSE PostProcess
{
    float exposure; // EV: each +1 doubles the light before tonemapping
    Tonemapper tonemapper;
    CRUCIBLE_HINT_RANGE(0, 1) float bloomStrength; // how much of the bloom is mixed in; 0.04 is a good start
    float bloomThreshold;                          // only light past this blooms (with a soft knee); 0 for all of it
    // Grading, after tonemapping except white balance (before it):
    CRUCIBLE_HINT_HEADER("Grading") CRUCIBLE_HINT_RANGE(-1, 1) float contrast; // -1 to 1, around mid-gray
    CRUCIBLE_HINT_RANGE(-1, 1) float saturation;                               // -1 (gray) to 1
    CRUCIBLE_HINT_RANGE(-1, 1) float temperature;                              // -1 (cooler, bluer) to 1 (warmer)
    CRUCIBLE_HINT_RANGE(-1, 1) float tint;                                     // -1 (greener) to 1 (more magenta)
    Vector3 lift;                                                              // added to the shadows, per channel
    Vector3 gamma; // raises (positive) or lowers the midtones, per channel
    Vector3 gain;  // scales the highlights: 0 is x1, 1 is x2, per channel
};

} // namespace crucible::render
