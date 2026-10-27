#pragma once

// M1 boundary probe (docs/milestones.md): exercises the binding generator's
// layout handling until real engine types do. Remove once they exist.

#include <crucible/Expose.h>
#include <crucible/Result.h>

#include <cstdint>
#include <span>
#include <string_view>

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

// An enum with a one-byte underlying type and a gap in its values.
enum class CRUCIBLE_EXPOSE ProbeKind : uint8_t
{
    Zero,
    One,
    Large = 200,
};

// Zero -> One -> Large -> Zero: an enum in both directions.
CRUCIBLE_EXPOSE ProbeKind nextProbeKind(ProbeKind kind);

// A fixed-size array field, with a byte after it.
struct CRUCIBLE_EXPOSE ProbeArray
{
    int32_t values[3];
    uint8_t tail;
};

CRUCIBLE_EXPOSE ProbeArray makeProbeArray(int32_t first, int32_t second, int32_t third, uint8_t tail);

// values weighted 1, 10, 100, plus tail * 1000: each element must arrive in
// its own place.
CRUCIBLE_EXPOSE int32_t weighProbeArray(ProbeArray probe);

// sumBindingProbe over every element: a read-only span.
CRUCIBLE_EXPOSE double sumBindingProbes(std::span<const BindingProbe> probes);

// Sets each element's number to its index; returns the count: a writable span.
CRUCIBLE_EXPOSE uint32_t fillBindingProbes(std::span<BindingProbe> probes);

// FNV-1a over the UTF-8 bytes, so a test can check the exact bytes arrived.
CRUCIBLE_EXPOSE uint32_t hashText(std::string_view text);

// The three Result forms: a value that throws in C# on failure (NotFound for
// a negative number), the Status form with a bool value, and Result<void>.
CRUCIBLE_EXPOSE Result<BindingProbe> findBindingProbe(int32_t number);
CRUCIBLE_EXPOSE_STATUS Result<bool> isBindingProbeEven(int32_t number);
CRUCIBLE_EXPOSE Result<void> checkBindingProbe(int32_t number);

} // namespace crucible
