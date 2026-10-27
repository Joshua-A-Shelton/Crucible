#pragma once

// What the launcher tells the engine's C# entry point (ADR 0011): parsed from
// the command line in C++, the only place it is parsed, and passed by value
// to crucible_engine_run. Names are placeholders (docs/naming.md).

#include <crucible/Expose.h>

#include <cstdint>

namespace crucible {

struct CRUCIBLE_EXPOSE LaunchOptions
{
    uint32_t frameLimit; // --frames N: stop after N frames; 0 runs until quit
    bool headless;       // --headless: no window or rendering; the loop still runs
};

} // namespace crucible
