#pragma once

// M1 boundary probe (docs/milestones.md): exercises the binding generator's
// layout handling until real engine types do. Remove once they exist.

#include <crucible/Expose.h>

#include <cstdint>

namespace crucible {

// Built to hit the classic layout traps: a bool (one byte in C++, not
// blittable in .NET) and padding after it, then tail padding before an
// 8-byte-aligned double.
struct CRUCIBLE_EXPOSE BindingProbe
{
    int32_t number; // offset 0
    bool flag;      // offset 4, then 3 bytes of padding
    float value;    // offset 8
    uint8_t small;  // offset 12, then 3 bytes of padding
    double wide;    // offset 16; size 24
};

CRUCIBLE_EXPOSE BindingProbe makeBindingProbe(int32_t number, bool flag, float value, uint8_t small, double wide);

// Sums the numeric fields, plus 1000 if flag is set, so a test can check every
// field crossed the boundary intact.
CRUCIBLE_EXPOSE double sumBindingProbe(BindingProbe probe);

} // namespace crucible
