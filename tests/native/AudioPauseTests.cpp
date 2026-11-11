// Pausing the mixer (ADR 0088): a paused sound is silent and carries on
// from where it was; one scheduled for later moves by the pause, so it still
// lands as far after its time; advancing while paused plays that much, then
// holds again.

#include "AudioTestSupport.h"

using namespace crucible;
using namespace crucible::audio;
using namespace crucible::audio::testing;

namespace {

// A ramp, one step a frame: where it's got to shows how far it's played.
std::vector<int16_t> ramp(uint32_t frames)
{
    std::vector<int16_t> samples(frames);
    for (uint32_t i = 0; i < frames; ++i)
    {
        samples[i] = static_cast<int16_t>(i);
    }
    return samples;
}

} // namespace

TEST_CASE("A paused sound is silent and resumes where it was")
{
    Offline mixer;
    const TestSound sound(ramp(24000));
    (void)playOneShot(sound.id, 0, here, 1, masterBus);
    mixer.mix(frameOf(0.1));
    const uint64_t paused = mixer.frames;
    pauseAll(true);
    mixer.mix(frameOf(0.2));
    CHECK(mixer.peak(paused, mixer.frames, 0) == 0.0f);
    const uint64_t resumed = mixer.frames;
    pauseAll(false);
    mixer.mix(frameOf(0.1));
    // The next step of the ramp, as if the pause hadn't been.
    const float step = mixer.left(paused - 1) - mixer.left(paused - 2);
    CHECK(step > 0);
    CHECK(mixer.left(resumed) == doctest::Approx(mixer.left(paused - 1) + step).epsilon(0.001));
}

TEST_CASE("A sound scheduled for later moves by the length of the pause")
{
    Offline mixer;
    const TestSound sound(steady(0.5f, 100));
    (void)playOneShot(sound.id, mixer.at(0.1), here, 1, masterBus); // due at 0.15 s
    mixer.mix(frameOf(0.1));
    pauseAll(true);
    mixer.mix(frameOf(0.2));
    pauseAll(false);
    mixer.mixSeconds(0.3);
    CHECK(mixer.onset(0.1f) == static_cast<int64_t>(frameOf(0.1 + defaultScheduleDelay + 0.2)));
    CHECK(mixerStats().lateStarts == 0);
}

TEST_CASE("Advancing while paused plays that much and holds again")
{
    Offline mixer;
    const TestSound sound(ramp(24000));
    (void)playOneShot(sound.id, 0, here, 1, masterBus);
    mixer.mix(frameOf(0.1));
    const uint64_t paused = mixer.frames;
    pauseAll(true);
    advancePaused(0.05f);
    mixer.mix(frameOf(0.2));
    const uint64_t advanced = paused + frameOf(0.05);
    CHECK(mixer.left(paused) > mixer.left(paused - 1)); // it carried on...
    CHECK(mixer.left(advanced - 1) > mixer.left(paused));
    CHECK(mixer.peak(advanced, mixer.frames, 0) == 0.0f); // ...for 0.05 s
    pauseAll(false);
    mixer.mix(480);
    const float step = mixer.left(paused) - mixer.left(paused - 1);
    CHECK(mixer.left(paused + frameOf(0.2)) == doctest::Approx(mixer.left(advanced - 1) + step).epsilon(0.001));
}
