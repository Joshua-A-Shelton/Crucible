#include <crucible/Launch.h>

#include <crucible/Vector.h>

#include <algorithm>

namespace crucible {

namespace {

Vector<char> latencyLog;

} // namespace

void setLatencyLogPath(std::string_view utf8Path)
{
    latencyLog.assign(std::span<const char>(utf8Path.data(), utf8Path.size()));
}

uint32_t latencyLogPath(std::span<uint8_t> buffer)
{
    const size_t copied = std::min(buffer.size(), latencyLog.size());
    std::copy_n(reinterpret_cast<const uint8_t*>(latencyLog.data()), copied, buffer.begin());
    return static_cast<uint32_t>(latencyLog.size());
}

} // namespace crucible
