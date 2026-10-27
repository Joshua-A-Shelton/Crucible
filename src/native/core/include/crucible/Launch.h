#pragma once

// What the launcher tells the engine's C# entry point (ADR 0011): parsed from
// the command line in C++, the only place it is parsed, and passed by value
// to crucible_engine_run. Names are placeholders (docs/naming.md).

#include <crucible/Export.h>
#include <crucible/Expose.h>

#include <cstdint>
#include <span>
#include <string_view>

namespace crucible {

struct CRUCIBLE_EXPOSE LaunchOptions
{
    uint32_t frameLimit; // --frames N: stop after N frames; 0 runs until quit
    bool headless;       // --headless: no window; frames render into an offscreen surface
    bool noPacing;       // --no-pacing: skip BeginFrame's pacing wait (ADR 0003 lets games opt out)
};

// --latency <file>: where the latency harness writes its CSV. Strings can't
// be fields of an exposed struct (ADR 0017), so the launcher stores the path
// here and C# reads it back. Empty: the harness is off.
CRUCIBLE_EXPORT void setLatencyLogPath(std::string_view utf8Path);
// Copies as much of the UTF-8 path as fits into `buffer` and returns its
// full length; call again with a larger buffer if it didn't fit.
CRUCIBLE_EXPOSE uint32_t latencyLogPath(std::span<uint8_t> buffer);

} // namespace crucible
