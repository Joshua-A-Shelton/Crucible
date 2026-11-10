#pragma once

// The M7 demonstration's native module (ADR 0077), copied into a new
// project: a C++ component and system, and a function C# calls.

#include <crucible/Expose.h>

#include <cstdint>

namespace demo {

// Marks an entity the Demo.Pulse system counts.
struct CRUCIBLE_EXPOSE Pulse
{
    CRUCIBLE_HINT_RANGE(0, 10) float rate; // pulses per second (for show)
};

// How many entities the Demo.Pulse system saw last frame.
CRUCIBLE_EXPOSE uint32_t pulsesLastFrame();

} // namespace demo
