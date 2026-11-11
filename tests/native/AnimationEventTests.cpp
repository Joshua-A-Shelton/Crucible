// Events and windows (ADR 0081): passed at exact clip times, timestamped on
// the input clock however the frames fall, from the clip with the most
// weight; windows open and close and can be asked about.

#include "AnimationTestSupport.h"

using namespace crucible;
using namespace crucible::animation;
using namespace crucible::animation::testing;

namespace {

const uint32_t footstep = eventName("Footstep");
const uint32_t parry = eventName("Parry");

std::vector<AnimationEvent> eventsOf(const Character& walker)
{
    std::vector<AnimationEvent> found(events(*walker.world, {}));
    (void)events(*walker.world, found);
    return found;
}

// Walk with events and windows for one test, unmarked again after it.
struct WalkMarks
{
    WalkMarks(const std::vector<assets::ClipEvent>& events, const std::vector<assets::ClipWindow>& windows)
    {
        markClip(2, events, windows);
    }
    ~WalkMarks()
    {
        markClip(2, {}, {});
    }
    WalkMarks(const WalkMarks&) = delete;
    WalkMarks& operator=(const WalkMarks&) = delete;
};

// Footsteps at 0 and 0.5 s (each foot planted), as the Walker's sidecar has
// them.
struct Footsteps : WalkMarks
{
    Footsteps()
        : WalkMarks({{0.0f, footstep}, {0.5f, footstep}}, {})
    {}
};

// Every footstep's timestamp over `seconds` at a frame rate.
std::vector<uint64_t> footstepTimes(float frame, float seconds)
{
    Character walker;
    const uint64_t start = walker.clock;
    std::vector<uint64_t> times;
    for (int frames = static_cast<int>(std::lround(seconds / frame)); frames > 0; --frames)
    {
        walker.step(frame);
        for (const AnimationEvent& event : eventsOf(walker))
        {
            CHECK(event.name == footstep);
            times.push_back(event.timestamp - start);
        }
    }
    return times;
}

} // namespace

TEST_CASE("Event names are FNV-1a hashes")
{
    CHECK(eventName("") == 2166136261u);
    CHECK(eventName("a") == 0xe40c292cu);
    CHECK(eventName("Footstep") != eventName("footstep"));
}

TEST_CASE("Events are passed at their clip times and timestamped when they were")
{
    const Footsteps marked;
    Character walker;
    const uint64_t start = walker.clock;
    walker.step(0.1f);
    // The clip just started, so its first moment counts: the frame's start.
    std::vector<AnimationEvent> passed = eventsOf(walker);
    REQUIRE(passed.size() == 1);
    CHECK(passed[0].entity.value == walker.entity.value);
    CHECK(passed[0].kind == AnimationEventKind::Event);
    CHECK(passed[0].name == footstep);
    CHECK(passed[0].clipTime == 0.0f);
    CHECK(passed[0].timestamp == start);
    walker.step(0.3f);
    CHECK(eventsOf(walker).empty());
    walker.step(0.3f); // 0.4 to 0.7: the second, a third of the way through
    passed = eventsOf(walker);
    REQUIRE(passed.size() == 1);
    CHECK(passed[0].clipTime == 0.5f);
    CHECK(static_cast<double>(passed[0].timestamp - start) == doctest::Approx(0.5e9).epsilon(1e-5));
    walker.step(0.4f); // round the loop: 0 again
    passed = eventsOf(walker);
    REQUIRE(passed.size() == 1);
    CHECK(static_cast<double>(passed[0].timestamp - start) == doctest::Approx(1.0e9).epsilon(1e-5));
}

TEST_CASE("Event times don't depend on the frame rate")
{
    const Footsteps marked;
    const std::vector<uint64_t> slow = footstepTimes(1.0f / 30, 2.9f);
    const std::vector<uint64_t> fast = footstepTimes(1.0f / 144, 2.9f);
    REQUIRE(slow.size() == 6);
    REQUIRE(fast.size() == slow.size());
    for (size_t i = 0; i < slow.size(); ++i)
    {
        CAPTURE(i);
        // Half a second apart, within 50 µs, at either rate.
        CHECK(std::fabs(static_cast<double>(slow[i]) - 0.5e9 * static_cast<double>(i)) < 5e4);
        CHECK(std::fabs(static_cast<double>(fast[i]) - static_cast<double>(slow[i])) < 5e4);
    }
}

TEST_CASE("Windows open and close at their clip times and can be asked about")
{
    const WalkMarks marked({}, {{0.2f, 0.35f, parry}});
    Character walker;
    const uint64_t start = walker.clock;
    walker.step(0.1f);
    OpenWindow open[2]{};
    CHECK(openWindows(*walker.world, walker.entity, open) == 0);
    walker.step(0.15f); // 0.1 to 0.25: opened two thirds of the way through
    std::vector<AnimationEvent> passed = eventsOf(walker);
    REQUIRE(passed.size() == 1);
    CHECK(passed[0].kind == AnimationEventKind::WindowOpen);
    CHECK(passed[0].name == parry);
    CHECK(static_cast<double>(passed[0].timestamp - start) == doctest::Approx(0.2e9).epsilon(1e-5));
    REQUIRE(openWindows(*walker.world, walker.entity, open) == 1);
    CHECK(open[0].name == parry);
    CHECK(open[0].opened == passed[0].timestamp);
    // It will close 0.1 s after this frame, the clip playing at speed 1.
    CHECK(static_cast<double>(open[0].closes - start) == doctest::Approx(0.35e9).epsilon(1e-5));

    walker.step(0.05f);
    CHECK(eventsOf(walker).empty());
    walker.step(0.1f); // 0.3 to 0.4
    passed = eventsOf(walker);
    REQUIRE(passed.size() == 1);
    CHECK(passed[0].kind == AnimationEventKind::WindowClose);
    CHECK(passed[0].clipTime == 0.35f);
    CHECK(static_cast<double>(passed[0].timestamp - start) == doctest::Approx(0.35e9).epsilon(1e-5));
    CHECK(openWindows(*walker.world, walker.entity, open) == 0);
}

TEST_CASE("A window closes when its clip stops playing")
{
    const WalkMarks marked({}, {{0.0f, 0.9f, parry}});
    Character walker;
    walker.step(0.1f);
    REQUIRE(eventsOf(walker).size() == 1);
    REQUIRE(play(*walker.world, walker.entity, idOf(3), 0));
    walker.step(0.1f);
    const std::vector<AnimationEvent> passed = eventsOf(walker);
    REQUIRE(passed.size() == 1);
    CHECK(passed[0].kind == AnimationEventKind::WindowClose);
    CHECK(passed[0].timestamp == walker.clock); // at the frame's start: no clip time passed it
}

TEST_CASE("During a crossfade the clip with the most weight passes events")
{
    const Footsteps marked;
    Character walker;
    walker.step(0.3f); // the footstep at 0
    REQUIRE(eventsOf(walker).size() == 1);
    REQUIRE(play(*walker.world, walker.entity, idOf(3), 1.0f)); // Idle, fading in over 1 s
    walker.step(0.3f);                                          // Walk 0.3 to 0.6, all its weight still
    CHECK(eventsOf(walker).size() == 1);
    walker.step(0.3f);
    CHECK(eventsOf(walker).empty());
    walker.step(0.3f); // Walk round to 0.2, but Idle weighs more now
    CHECK(eventsOf(walker).empty());
}

TEST_CASE("Layers pass their clips' events")
{
    const Footsteps marked;
    Character walker(3); // Idle, with Walk over it
    REQUIRE(setLayer(*walker.world, walker.entity, 0, {idOf(2), 1.0f, LayerBlend::Override, 1.0f, true, 0}, {}));
    walker.step(0.6f);
    CHECK(eventsOf(walker).size() == 2);
}
