#pragma once

// The core's messages (ADR 0087), and C#'s (ADR 0103): information,
// warnings, and errors, from any thread. Each is printed as it always was
// ("crucible: ...", warnings and errors to stderr, information to stdout),
// written to the log file if one is set, handed to a sink if one is set,
// and kept for C# to take (the editor's Log shows them). A Shipping build
// (CRUCIBLE_SHIPPING) compiles information out: info() does nothing, and
// write() drops it.

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
#if defined(CRUCIBLE_SHIPPING)
inline void info(const char*, ...) {}
#else
CRUCIBLE_EXPORT void info(const char* format, ...);
#endif
CRUCIBLE_EXPORT void warning(const char* format, ...);
CRUCIBLE_EXPORT void error(const char* format, ...);

// A message already written out, as C# writes them (ADR 0103): printed as
// it is, without the core's "crucible: ".
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE void write(LogLevel level, std::string_view text);

// A package's log file (ADR 0103): every message from now on is also
// written to the UTF-8 path. Nothing is created until the first message;
// then a file already there is renamed to previousPath (replacing it), and
// the path's folder is made if it's missing. False if a file is already
// set. Main thread, before messages are written.
CRUCIBLE_EXPORT bool setLogFile(std::string_view utf8Path, std::string_view utf8PreviousPath);

// Native code's own sink (a module's, a tool's); null for none.
CRUCIBLE_EXPORT void setSink(LogSink sink, void* context);

// The messages since the last take (the newest 256), for C#: take them,
// then read each by index until the next take. Main thread.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE_INTERNAL uint32_t takeMessages();
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE_INTERNAL LogLevel takenLevel(uint32_t index);
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE_INTERNAL std::string_view takenText(uint32_t index);

} // namespace crucible::log
