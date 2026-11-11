// Buses (ADR 0082): volume fades, the low-pass, the reverb send's tail,
// ducking, and snapshots.

#include "AudioTestSupport.h"

using namespace crucible;
using namespace crucible::audio;
using namespace crucible::audio::testing;

namespace {

constexpr uint32_t effects = 2; // a bus of the tests' own
constexpr BusEffects none{0, 0, noBus, 0};

} // namespace

TEST_CASE("A bus's volume fades over its time")
{
    Offline mixer;
    const TestSound sound(steady(0.5f, mixRate));
    (void)playOneShot(sound.id, 0, here, 1, effects);
    mixer.mixSeconds(0.1);
    setBusVolume(effects, 0, 0.5f);
    mixer.mixSeconds(0.6);
    const float full = 0.5f * centre;
    CHECK(mixer.left(frameOf(0.05)) == doctest::Approx(full).epsilon(1e-3));
    CHECK(mixer.left(frameOf(0.35)) == doctest::Approx(full * 0.5f).epsilon(0.03));
    CHECK(mixer.peak(frameOf(0.61), frameOf(0.7), 0) == 0.0f);
    setBusVolume(effects, 1, 0);
}

TEST_CASE("A low-pass lets low tones through and quiets high ones")
{
    auto level = [](float hertz) {
        Offline mixer;
        setBusEffects(effects, {500, 0, noBus, 0});
        const TestSound sound(sine(hertz, 0.5));
        (void)playOneShot(sound.id, 0, here, 1, effects);
        mixer.mixSeconds(0.4);
        return mixer.peak(frameOf(0.2), frameOf(0.4), 0) / (0.5f * centre);
    };
    CHECK(level(100) > 0.95f);
    CHECK(level(5000) < 0.15f);
}

TEST_CASE("A reverb send leaves a tail after the sound ends")
{
    auto tail = [](float send) {
        Offline mixer;
        setBusEffects(effects, {0, send, noBus, 0});
        const TestSound sound(sine(440, 0.1));
        (void)playOneShot(sound.id, 0, here, 1, effects);
        mixer.mixSeconds(0.6);
        return mixer.peak(frameOf(0.2), frameOf(0.6), 0);
    };
    CHECK(tail(0) == 0.0f);
    CHECK(tail(0.5f) > 1e-3f);
}

TEST_CASE("Ducking lowers a bus while another plays")
{
    Offline mixer;
    constexpr uint32_t voices = 3;
    setBusEffects(effects, {0, 0, voices, 0.5f});
    const TestSound music(steady(0.5f, mixRate));
    // A square wave: it averages to nothing, so the average is the music.
    std::vector<int16_t> square(mixRate / 2);
    for (size_t i = 0; i < square.size(); ++i)
    {
        square[i] = (i / 48) % 2 == 0 ? 16384 : -16384;
    }
    const TestSound speech(square);
    (void)playOneShot(music.id, 0, here, 1, effects);
    mixer.mixSeconds(0.1);
    (void)playOneShot(speech.id, 0, here, 1, voices);
    mixer.mixSeconds(0.3);
    const float full = 0.5f * centre;
    CHECK(mixer.average(frameOf(0.02), frameOf(0.08), 0) == doctest::Approx(full).epsilon(1e-3));
    CHECK(mixer.average(frameOf(0.2), frameOf(0.2) + 960, 0) == doctest::Approx(full * 0.5f).epsilon(0.02));
    setBusEffects(effects, none);
}

TEST_CASE("Blending to a snapshot moves its buses over time")
{
    Offline mixer;
    defineSnapshot("Quiet", effects, 0.25f, none);
    defineSnapshot("Loud", effects, 1.0f, none);
    const TestSound sound(steady(0.5f, mixRate));
    (void)playOneShot(sound.id, 0, here, 1, effects);
    mixer.mixSeconds(0.1);
    blendToSnapshot("Quiet", 0.2f);
    mixer.mixSeconds(0.4);
    CHECK(mixer.left(frameOf(0.4)) == doctest::Approx(0.25f * 0.5f * centre).epsilon(1e-3));
    blendToSnapshot("Loud", 0);
    mixer.mixSeconds(0.1);
    CHECK(mixer.left(frameOf(0.55)) == doctest::Approx(0.5f * centre).epsilon(1e-3));
}
