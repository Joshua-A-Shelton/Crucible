#pragma once

// What the importers share (ADR 0042, 0043): error reporting through
// lastImportError, colour-space conversions, and half floats. Internal.

#include <crucible/Result.h>

#include <algorithm>
#include <cmath>
#include <cstdint>

namespace crucible::importers {

// Sets the reason lastImportError() reports on this thread and returns
// InvalidData, for `return fail("...")`.
Status fail(const char* format, ...);
void clearError();

inline float srgbToLinear(float c)
{
    return c <= 0.04045f ? c / 12.92f : std::pow((c + 0.055f) / 1.055f, 2.4f);
}

inline float linearToSrgb(float c)
{
    return c <= 0.0031308f ? c * 12.92f : 1.055f * std::pow(c, 1.0f / 2.4f) - 0.055f;
}

inline uint8_t toByte(float value)
{
    return static_cast<uint8_t>(std::lround(std::clamp(value, 0.0f, 1.0f) * 255.0f));
}

// IEEE half precision, rounded to nearest even; values past its range
// become infinity, as the GPU's own conversion does.
uint16_t toHalf(float value);

} // namespace crucible::importers
