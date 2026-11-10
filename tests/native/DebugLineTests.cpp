// Debug lines (ADR 0079): the frame's list, its limit, and the system that
// clears it.

#include <crucible/DebugLines.h>
#include <crucible/RenderSchedule.h>
#include <crucible/Systems.h>

#include <doctest/doctest.h>

#include <ostream>
#include <string_view>

using namespace crucible;

TEST_CASE("Debug lines are kept for the frame")
{
    debug::clearDebugLines();
    debug::drawLine({0, 0, 0}, {1, 0, 0}, {1, 0, 0, 1});
    debug::drawLine({0, 0, 0}, {0, 1, 0}, {0, 1, 0, 1});
    REQUIRE(debug::debugLineCount() == 2);
    const std::span<const debug::DebugLine> lines = debug::debugLines();
    CHECK(lines[1].to.y == 1.0f);
    CHECK(lines[1].color.y == 1.0f);
    debug::clearDebugLines();
    CHECK(debug::debugLineCount() == 0);
}

TEST_CASE("Lines past the limit are dropped")
{
    debug::clearDebugLines();
    for (uint32_t i = 0; i < debug::maxDebugLines + 10; ++i)
    {
        debug::drawLine({0, 0, 0}, {1, 1, 1}, {1, 1, 1, 1});
    }
    CHECK(debug::debugLineCount() == debug::maxDebugLines);
    debug::clearDebugLines();
}

TEST_CASE("DebugLines.Clear empties the list when each frame begins")
{
    debug::registerDebugLines();
    bool found = false;
    for (uint32_t i = 0; i < loop::nativeSystemCount(); ++i)
    {
        if (loop::nativeSystemName(i) == debug::clearSystemName)
        {
            found = true;
            CHECK(loop::nativeSystemPhase(i) == "BeginFrame");
            CHECK_FALSE(loop::nativeSystemIsGameplay(i));
            debug::drawLine({0, 0, 0}, {1, 0, 0}, {1, 1, 1, 1});
            auto* function = reinterpret_cast<loop::SystemFunction>(loop::nativeSystemFunction(i));
            function({}, loop::nativeSystemUserData(i));
            CHECK(debug::debugLineCount() == 0);
        }
    }
    CHECK(found);
}

TEST_CASE("The DebugLines step is named")
{
    CHECK(std::string_view(render::steps::debugLines) == "DebugLines");
}
