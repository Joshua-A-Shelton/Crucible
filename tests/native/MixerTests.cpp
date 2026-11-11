// The mixer (ADR 0082), offline: silence, one-shots played as they are,
// resampling (a sound's own rate, cubic Hermite against a true sine),
// stopping with a fade, clipping, and sounds made in code freed.

#include "AudioTestSupport.h"

using namespace crucible;
using namespace crucible::audio;
using namespace crucible::audio::testing;

TEST_CASE("Nothing playing mixes silence")
{
    Offline mixer;
    mixer.mix(4800);
    CHECK(mixer.peak(0, 4800, 0) == 0.0f);
    CHECK(mixer.peak(0, 4800, 1) == 0.0f);
    CHECK(mixerStats().voices == 0);
}

TEST_CASE("A one-shot at the listener plays its samples in the middle")
{
    Offline mixer;
    const TestSound sound(steady(0.5f, 4800));
    const uint32_t voice = playOneShot(sound.id, 0, here, 1, masterBus);
    CHECK(voice != 0);
    mixer.mix(480);
    CHECK(mixerStats().voices == 1);
    mixer.mix(9600 - 480);
    for (const uint64_t frame : {0u, 1u, 2400u, 4799u})
    {
        CAPTURE(frame);
        CHECK(mixer.left(frame) == doctest::Approx(0.5f * centre).epsilon(1e-4));
        CHECK(mixer.right(frame) == doctest::Approx(0.5f * centre).epsilon(1e-4));
    }
    CHECK(mixer.peak(4800, 9600, 0) == 0.0f);
    CHECK(mixerStats().voices == 0);
    CHECK(playOneShot({}, 0, here, 1, masterBus) == 0); // no such sound
}

TEST_CASE("A sound at another rate plays for its own length")
{
    Offline mixer;
    const TestSound sound(steady(0.5f, 2400), 24000); // 0.1 s
    (void)playOneShot(sound.id, 0, here, 1, masterBus);
    mixer.mix(9600);
    CHECK(mixer.onset(0.1f) == 0);
    CHECK(mixer.peak(4790, 4800, 0) > 0.3f);
    CHECK(mixer.peak(4810, 9600, 0) == 0.0f);
}

TEST_CASE("Resampling keeps a sine a sine")
{
    // 1 kHz at 44.1 kHz, played at 48 kHz: cubic Hermite against the true
    // curve, within about -60 dB.
    Offline mixer;
    const TestSound sound(sine(1000, 0.5, 0.5f, 44100), 44100);
    (void)playOneShot(sound.id, 0, here, 1, masterBus);
    mixer.mix(frameOf(0.4));
    float worst = 0;
    for (uint64_t frame = 100; frame < frameOf(0.4); ++frame)
    {
        const double seconds = static_cast<double>(frame) / mixRate;
        const float expected = 0.5f * centre * static_cast<float>(std::sin(2 * 3.14159265358979 * 1000 * seconds));
        worst = std::max(worst, std::fabs(mixer.left(frame) - expected));
    }
    CHECK(worst < 1e-3f);
}

TEST_CASE("Stopping a voice fades it out")
{
    Offline mixer;
    const TestSound sound(steady(0.5f, mixRate));
    const uint32_t voice = playOneShot(sound.id, 0, here, 1, masterBus);
    mixer.mix(frameOf(0.1));
    stop(voice, 0.1f);
    mixer.mix(frameOf(0.3));
    const float full = 0.5f * centre;
    const uint64_t stopped = frameOf(0.1);
    CHECK(mixer.left(stopped + frameOf(0.05)) == doctest::Approx(full * 0.5f).epsilon(0.02));
    CHECK(mixer.peak(stopped + frameOf(0.1) + 1, mixer.frames, 0) == 0.0f);
    CHECK(mixerStats().voices == 0);
}

TEST_CASE("The master clips at full scale")
{
    Offline mixer;
    const TestSound sound(steady(0.9f, 4800));
    (void)playOneShot(sound.id, 0, here, 1, masterBus);
    (void)playOneShot(sound.id, 0, here, 1, masterBus);
    mixer.mix(960);
    CHECK(mixer.left(100) == 1.0f);
}

TEST_CASE("A sound made in code stops playing when it's destroyed")
{
    Offline mixer;
    assets::AssetId id;
    {
        const TestSound sound(steady(0.5f, mixRate));
        id = sound.id;
        (void)playOneShot(sound.id, 0, here, 1, masterBus);
        mixer.mix(480);
    }
    mixer.mix(960);
    CHECK(mixer.peak(480, 1440, 0) == 0.0f);
    CHECK(playOneShot(id, 0, here, 1, masterBus) == 0);
    // Sounds that can't be played aren't made.
    const std::vector<int16_t> samples(30);
    CHECK(createSound({mixRate, 3, 10, assets::SoundEncoding::Pcm16, 0, 4}, samples).bytes[0] == 0);
    CHECK(createSound({mixRate, 1, 40, assets::SoundEncoding::Pcm16, 0, 4}, samples).bytes[0] == 0);
}
