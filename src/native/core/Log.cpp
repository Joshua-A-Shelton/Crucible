// The core's messages (ADR 0087, 0103): printed, written to the log file,
// handed to the sink, and kept in a fixed ring (no allocation, so it works
// on any path) behind a lock that's held only to copy a message in or out;
// they're rare, never on a hot path.

#include <crucible/Log.h>

#include <algorithm>
#include <cstdarg>
#include <cstdio>
#include <cstring>
#include <mutex>

#if defined(_WIN32)
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#else
#include <sys/stat.h>
#endif

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

// The log file (setLogFile): its paths, fixed buffers, and the file once
// the first message opens it.
constexpr size_t longestPath = 1024;
char filePath[longestPath] = {};
char previousPath[longestPath] = {};
bool fileSet = false;
std::FILE* file = nullptr;

#if defined(_WIN32)
bool widen(const char* utf8, wchar_t (&wide)[longestPath])
{
    return MultiByteToWideChar(CP_UTF8, 0, utf8, -1, wide, static_cast<int>(longestPath)) > 0;
}
#endif

// The file's folder made, an old file moved aside, and the file opened.
// Under the lock.
void openFile()
{
    fileSet = false; // once, opened or not
#if defined(_WIN32)
    wchar_t path[longestPath];
    wchar_t previous[longestPath];
    if (!widen(filePath, path) || !widen(previousPath, previous))
    {
        return;
    }
    for (wchar_t* c = path; *c != 0; ++c)
    {
        if ((*c == L'\\' || *c == L'/') && c != path)
        {
            const wchar_t separator = *c;
            *c = 0;
            CreateDirectoryW(path, nullptr);
            *c = separator;
        }
    }
    MoveFileExW(path, previous, MOVEFILE_REPLACE_EXISTING);
    file = _wfopen(path, L"wb");
#else
    for (char* c = filePath; *c != 0; ++c)
    {
        if (*c == '/' && c != filePath)
        {
            *c = 0;
            mkdir(filePath, 0755);
            *c = '/';
        }
    }
    std::rename(filePath, previousPath);
    file = std::fopen(filePath, "wb");
#endif
}

void write(LogLevel level, const char* prefix, const char* text, size_t length)
{
#if defined(CRUCIBLE_SHIPPING)
    if (level == LogLevel::Info)
    {
        return;
    }
#endif
    std::fprintf(level == LogLevel::Info ? stdout : stderr, "%s%.*s\n", prefix, static_cast<int>(length), text);
    if (level == LogLevel::Info)
    {
        std::fflush(stdout);
    }
    LogSink current;
    void* context;
    {
        const std::lock_guard<std::mutex> guard(lock);
        if (fileSet)
        {
            openFile();
        }
        if (file != nullptr)
        {
            std::fprintf(file, "%s%.*s\n", prefix, static_cast<int>(length), text);
            std::fflush(file);
        }
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

void write(LogLevel level, const char* format, va_list arguments)
{
    char text[longest];
    const int written = std::vsnprintf(text, sizeof(text), format, arguments);
    const size_t length = written < 0 ? 0 : std::min(static_cast<size_t>(written), sizeof(text) - 1);
    write(level, "crucible: ", text, length);
}

} // namespace

#if !defined(CRUCIBLE_SHIPPING)
void info(const char* format, ...)
{
    va_list arguments;
    va_start(arguments, format);
    write(LogLevel::Info, format, arguments);
    va_end(arguments);
}
#endif

void write(LogLevel level, std::string_view text)
{
    write(level, "", text.data(), std::min(text.size(), longest - 1));
}

bool setLogFile(std::string_view utf8Path, std::string_view utf8PreviousPath)
{
    const std::lock_guard<std::mutex> guard(lock);
    if (fileSet || file != nullptr || utf8Path.size() >= longestPath || utf8PreviousPath.size() >= longestPath)
    {
        return false;
    }
    std::memcpy(filePath, utf8Path.data(), utf8Path.size());
    filePath[utf8Path.size()] = 0;
    std::memcpy(previousPath, utf8PreviousPath.data(), utf8PreviousPath.size());
    previousPath[utf8PreviousPath.size()] = 0;
    fileSet = true;
    return true;
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
