#include <crucible/Launch.h>

#include <algorithm>
#include <string>

namespace crucible {

namespace {

std::string latencyLog;

} // namespace

void setLatencyLogPath(std::string_view utf8Path)
{
    latencyLog = utf8Path;
}

uint32_t latencyLogPath(std::span<uint8_t> buffer)
{
    const size_t copied = std::min(buffer.size(), latencyLog.size());
    std::copy_n(reinterpret_cast<const uint8_t*>(latencyLog.data()), copied, buffer.begin());
    return static_cast<uint32_t>(latencyLog.size());
}

} // namespace crucible
