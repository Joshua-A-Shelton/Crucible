#pragma once

// The core's messages (ADR 0087): information, warnings, and errors, from
// any thread. Each is printed as it always was ("crucible: ...", warnings
// and errors to stderr, information to stdout), handed to a sink if one is
// set, and kept for C# to take (the editor's Log shows them).

#include <crucible/Export.h>
#include <crucible/Expose.h>

#include <cstdint>
#include <string_view>

namespace crucible::log {

enum class CRUCIBLE_EXPOSE LogLevel : uint32_t
{
    Info,
    Warning,
    Error,
};

// Called with each message, on the thread that wrote it.
using LogSink = void (*)(LogLevel level, std::string_view text, void* context);

// printf-style; a message longer than 1023 bytes is cut.
CRUCIBLE_EXPORT void info(const char* format, ...);
CRUCIBLE_EXPORT void warning(const char* format, ...);
CRUCIBLE_EXPORT void error(const char* format, ...);

// Native code's own sink (a module's, a tool's); null for none.
CRUCIBLE_EXPORT void setSink(LogSink sink, void* context);

// The messages since the last take (the newest 256), for C#: take them,
// then read each by index until the next take. Main thread.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE_INTERNAL uint32_t takeMessages();
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE_INTERNAL LogLevel takenLevel(uint32_t index);
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE_INTERNAL std::string_view takenText(uint32_t index);

} // namespace crucible::log
