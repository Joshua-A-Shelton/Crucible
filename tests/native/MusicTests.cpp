// Music (ADR 0082): tracks loop on the music bus, crossfade over their time,
// and switch on the playing track's next beat or bar, to the frame.

#include "AudioTestSupport.h"

using namespace crucible;
using namespace crucible::audio;
using namespace crucible::audio::testing;

namespace {

// 120 beats a minute in 4/4: a beat is half a second, a bar two.
constexpr float tempo = 120;

} // namespace

TEST_CASE("Music loops on the music bus")
{
    Offline mixer;
    const TestSound track(steady(0.25f, 4800), mixRate, 1, tempo);
    playMusic(track.id, {0, MusicAlign::Now});
    mixer.mixSeconds(0.5); // five times round
    CHECK(mixer.peak(frameOf(0.45), frameOf(0.5), 0) > 0.15f);
    setBusVolume(musicBus, 0, 0);
    mixer.mixSeconds(0.1);
    CHECK(mixer.peak(frameOf(0.52), frameOf(0.6), 0) == 0.0f);
    setBusVolume(musicBus, 1, 0);
}

TEST_CASE("A cut on the bar starts the next track on the bar to the frame")
{
    Offline mixer;
    const TestSound first(steady(0.25f, mixRate), mixRate, 1, tempo);
    const TestSound second(steady(-0.25f, mixRate), mixRate, 1, tempo);
    playMusic(first.id, {0, MusicAlign::Now});
    mixer.mixSeconds(0.7);
    playMusic(second.id, {0, MusicAlign::Bar});
    mixer.mixSeconds(1.6);
    const uint64_t bar = frameOf(2.0);
    CHECK(mixer.left(bar - 1) > 0.15f);
    CHECK(mixer.left(bar) < -0.15f);
}

TEST_CASE("A switch on the beat waits for the next beat")
{
    Offline mixer;
    const TestSound first(steady(0.25f, mixRate), mixRate, 1, tempo);
    const TestSound second(steady(-0.25f, mixRate), mixRate, 1, tempo);
    playMusic(first.id, {0, MusicAlign::Now});
    mixer.mixSeconds(0.6);
    playMusic(second.id, {0, MusicAlign::Beat});
    mixer.mixSeconds(0.6);
    CHECK(mixer.left(frameOf(1.0) - 1) > 0.15f);
    CHECK(mixer.left(frameOf(1.0)) < -0.15f);
}

TEST_CASE("A crossfade blends the two tracks over its time")
{
    Offline mixer;
    const TestSound first(steady(0.2f, mixRate), mixRate, 1, tempo);
    const TestSound second(steady(0.6f, mixRate), mixRate, 1, tempo);
    playMusic(first.id, {0, MusicAlign::Now});
    mixer.mixSeconds(0.1);
    playMusic(second.id, {1.0f, MusicAlign::Now});
    mixer.mixSeconds(1.2);
    // Half way: half of each.
    CHECK(mixer.left(frameOf(0.6)) == doctest::Approx((0.1f + 0.3f) * centre).epsilon(0.01));
    CHECK(mixer.left(frameOf(1.2)) == doctest::Approx(0.6f * centre).epsilon(1e-3));
}

TEST_CASE("Without a tempo music switches at once")
{
    Offline mixer;
    const TestSound first(steady(0.25f, mixRate));
    const TestSound second(steady(-0.25f, mixRate));
    playMusic(first.id, {0, MusicAlign::Now});
    mixer.mixSeconds(0.3);
    playMusic(second.id, {0, MusicAlign::Bar});
    mixer.mixSeconds(0.1);
    CHECK(mixer.left(frameOf(0.3) + 1) < -0.15f);
}
