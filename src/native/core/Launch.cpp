#include <crucible/Launch.h>

#include <crucible/Vector.h>

#include <algorithm>

namespace crucible {

namespace {

Vector<char> latencyLog;
Vector<char> game;
// Every game argument back to back, and where each one ends.
Vector<char> gameArguments;
Vector<uint32_t> gameArgumentEnds;

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

void setGamePath(std::string_view utf8Path)
{
    game.assign(std::span<const char>(utf8Path.data(), utf8Path.size()));
}

std::string_view gamePath()
{
    return {game.data(), game.size()};
}

void addGameArgument(std::string_view utf8Argument)
{
    for (char c : utf8Argument)
    {
        gameArguments.pushBack(c);
    }
    gameArgumentEnds.pushBack(static_cast<uint32_t>(gameArguments.size()));
}

uint32_t gameArgumentCount()
{
    return static_cast<uint32_t>(gameArgumentEnds.size());
}

std::string_view gameArgument(uint32_t index)
{
    if (index >= gameArgumentEnds.size())
    {
        return {};
    }
    const uint32_t begin = index == 0 ? 0 : gameArgumentEnds[index - 1];
    return {gameArguments.data() + begin, gameArgumentEnds[index] - begin};
}

} // namespace crucible
