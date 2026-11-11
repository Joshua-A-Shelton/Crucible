// Sample-accurate scheduling (ADR 0028, 0082): a sound starts at its
// timestamp plus the delay, to the frame, however its command falls between
// the device's requests; late ones start at once and are counted.

#include "AudioTestSupport.h"

using namespace crucible;
using namespace crucible::audio;
using namespace crucible::audio::testing;

namespace {

// A short click: ten frames at half scale.
std::vector<int16_t> click()
{
    return steady(0.5f, 10);
}

} // namespace

TEST_CASE("A scheduled sound starts at its time plus the delay to the frame")
{
    Offline mixer;
    const TestSound sound(click());
    (void)playOneShot(sound.id, mixer.at(0.1), here, 1, masterBus);
    mixer.mixSeconds(0.3);
    CHECK(mixer.onset(0.1f) == static_cast<int64_t>(frameOf(0.1 + defaultScheduleDelay)));
}

TEST_CASE("Sounds asked for from different frames keep their spacing")
{
    Offline mixer;
    const TestSound sound(click());
    // Footsteps half a second apart, each asked for at a different point
    // between the device's requests, a little before its time.
    const double times[] = {0.2, 0.7, 1.2, 1.7};
    const uint64_t gaps[] = {frameOf(0.13), frameOf(0.5) + 77, frameOf(0.49) + 301, frameOf(0.5) - 123};
    for (int i = 0; i < 4; ++i)
    {
        mixer.mix(gaps[i]);
        (void)playOneShot(sound.id, mixer.at(times[i]), here, 1, masterBus);
    }
    mixer.mixSeconds(0.5);
    int64_t from = 0;
    for (const double time : times)
    {
        CAPTURE(time);
        const int64_t onset = mixer.onset(0.1f, static_cast<uint64_t>(from));
        CHECK(onset == static_cast<int64_t>(frameOf(time + defaultScheduleDelay)));
        from = onset + 20;
    }
    CHECK(mixerStats().lateStarts == 0);
}

TEST_CASE("A sound asked for too late starts at once and is counted")
{
    Offline mixer;
    const TestSound sound(click());
    mixer.mix(frameOf(0.1));
    (void)playOneShot(sound.id, mixer.at(0.0), here, 1, masterBus); // due at 0.05 s
    mixer.mix(frameOf(0.1));
    CHECK(mixer.onset(0.1f) == static_cast<int64_t>(frameOf(0.1)));
    CHECK(mixerStats().lateStarts == 1);
}

TEST_CASE("The delay can be changed and now means now")
{
    Offline mixer;
    const TestSound sound(click());
    setScheduleDelay(0.02f);
    CHECK(scheduleDelay() == 0.02f);
    (void)playOneShot(sound.id, mixer.at(0.1), here, 1, masterBus);
    mixer.mixSeconds(0.2);
    CHECK(mixer.onset(0.1f) == static_cast<int64_t>(frameOf(0.12)));
    (void)playOneShot(sound.id, 0, here, 1, masterBus);
    mixer.mixSeconds(0.1);
    CHECK(mixer.onset(0.1f, frameOf(0.15)) == static_cast<int64_t>(frameOf(0.2)));
}
