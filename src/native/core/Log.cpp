// The core's messages (ADR 0087): printed, handed to the sink, and kept in a
// fixed ring (no allocation, so it works on any path) behind a lock that's
// held only to copy a message in or out; they're rare, never on a hot path.

#include <crucible/Log.h>

#include <algorithm>
#include <cstdarg>
#include <cstdio>
#include <cstring>
#include <mutex>

namespace crucible::log {

namespace {

constexpr uint32_t capacity = 256;
constexpr size_t longest = 1024;

struct Message
{
    LogLevel level = LogLevel::Info;
    uint32_t length = 0;
    char text[longest];
};

std::mutex lock;
Message pending[capacity];
uint32_t pendingStart = 0; // the oldest
uint32_t pendingCount = 0;
Message taken[capacity];
uint32_t takenCount = 0;
LogSink sink = nullptr;
void* sinkContext = nullptr;

void write(LogLevel level, const char* format, va_list arguments)
{
    char text[longest];
    const int written = std::vsnprintf(text, sizeof(text), format, arguments);
    const size_t length = written < 0 ? 0 : std::min(static_cast<size_t>(written), sizeof(text) - 1);
    std::fprintf(level == LogLevel::Info ? stdout : stderr, "crucible: %.*s\n", static_cast<int>(length), text);
    if (level == LogLevel::Info)
    {
        std::fflush(stdout);
    }
    LogSink current;
    void* context;
    {
        const std::lock_guard<std::mutex> guard(lock);
        Message& slot = pending[(pendingStart + pendingCount) % capacity];
        if (pendingCount == capacity)
        {
            pendingStart = (pendingStart + 1) % capacity; // the oldest makes way
        }
        else
        {
            ++pendingCount;
        }
        slot.level = level;
        slot.length = static_cast<uint32_t>(length);
        std::memcpy(slot.text, text, length);
        current = sink;
        context = sinkContext;
    }
    if (current != nullptr)
    {
        current(level, {text, length}, context);
    }
}

} // namespace

void info(const char* format, ...)
{
    va_list arguments;
    va_start(arguments, format);
    write(LogLevel::Info, format, arguments);
    va_end(arguments);
}

void warning(const char* format, ...)
{
    va_list arguments;
    va_start(arguments, format);
    write(LogLevel::Warning, format, arguments);
    va_end(arguments);
}

void error(const char* format, ...)
{
    va_list arguments;
    va_start(arguments, format);
    write(LogLevel::Error, format, arguments);
    va_end(arguments);
}

void setSink(LogSink next, void* context)
{
    const std::lock_guard<std::mutex> guard(lock);
    sink = next;
    sinkContext = context;
}

uint32_t takeMessages()
{
    const std::lock_guard<std::mutex> guard(lock);
    for (uint32_t i = 0; i < pendingCount; ++i)
    {
        taken[i] = pending[(pendingStart + i) % capacity];
    }
    takenCount = pendingCount;
    pendingStart = 0;
    pendingCount = 0;
    return takenCount;
}

LogLevel takenLevel(uint32_t index)
{
    return index < takenCount ? taken[index].level : LogLevel::Info;
}

std::string_view takenText(uint32_t index)
{
    return index < takenCount ? std::string_view(taken[index].text, taken[index].length) : std::string_view();
}

} // namespace crucible::log
