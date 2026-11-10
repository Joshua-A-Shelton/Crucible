// The input contract (ADR 0076): pollEvents and polledText come from the
// installed input source, the platform's until something replaces it, and a
// replacement can wrap the platform's.

#include <crucible/Platform.h>

#include <doctest/doctest.h>

#include <algorithm>
#include <cstring>

using namespace crucible;
using namespace crucible::platform;

namespace {

uint32_t recorded(std::span<InputEvent> events)
{
    if (events.empty())
    {
        return 0;
    }
    events[0] = {};
    events[0].type = InputEventType::KeyDown;
    events[0].timestampNs = 123;
    events[0].code = 4; // A
    return 1;
}

uint32_t recordedText(std::span<uint8_t> out)
{
    constexpr char text[] = "hi";
    std::memcpy(out.data(), text, std::min<size_t>(out.size(), 2));
    return 2;
}

} // namespace

TEST_CASE("Polling reads the installed input source")
{
    CHECK(inputSource().poll == platformInputSource().poll); // the platform's by default
    installInputSource({recorded, recordedText});
    InputEvent events[4];
    REQUIRE(pollEvents(events) == 1);
    CHECK(events[0].type == InputEventType::KeyDown);
    CHECK(events[0].timestampNs == 123);
    uint8_t text[8] = {};
    CHECK(polledText(text) == 2);
    CHECK(text[0] == 'h');
    installInputSource(platformInputSource());
    CHECK(inputSource().poll == platformInputSource().poll);
}
