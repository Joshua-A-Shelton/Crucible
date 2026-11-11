#pragma once

// What the mixer's tests share (ADR 0082): offline mixing from a known
// start, sounds made in code, and measuring what comes out.

#include <crucible/Audio.h>

#include <doctest/doctest.h>

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <vector>

namespace crucible::audio::testing {

constexpr float centre = 0.70710678f; // a mono sound in the middle, each side

// The mixer offline for one test, frame 0 at `start` on the input clock.
struct Offline
{
    uint64_t start = 1'000'000'000;
    uint64_t frames = 0; // mixed so far
    std::vector<float> out;

    Offline()
    {
        setScheduleDelay(defaultScheduleDelay);
        startOfflineMixing(start);
    }

    ~Offline()
    {
        stopMixing();
        setScheduleDelay(defaultScheduleDelay);
    }

    Offline(const Offline&) = delete;
    Offline& operator=(const Offline&) = delete;

    // Mixes `count` more frames, in pieces of 480 (10 ms) as a device would ask.
    void mix(uint64_t count)
    {
        const size_t at = out.size();
        out.resize(at + count * 2);
        for (uint64_t done = 0; done < count;)
        {
            const uint64_t piece = std::min<uint64_t>(480, count - done);
            mixOffline(std::span(out.data() + at + done * 2, piece * 2));
            done += piece;
        }
        frames += count;
    }

    void mixSeconds(double seconds)
    {
        mix(static_cast<uint64_t>(std::llround(seconds * mixRate)));
    }

    // A moment `seconds` after frame 0, on the input clock.
    uint64_t at(double seconds) const
    {
        return start + static_cast<uint64_t>(std::llround(seconds * 1e9));
    }

    float left(uint64_t frame) const
    {
        return out[frame * 2];
    }

    float right(uint64_t frame) const
    {
        return out[frame * 2 + 1];
    }

    // The first frame from `from` on where either side passes `level`, or -1.
    int64_t onset(float level, uint64_t from = 0) const
    {
        for (uint64_t f = from; f < frames; ++f)
        {
            if (std::fabs(left(f)) > level || std::fabs(right(f)) > level)
            {
                return static_cast<int64_t>(f);
            }
        }
        return -1;
    }

    // The largest magnitude in [from, to) on a side (0 left, 1 right).
    float peak(uint64_t from, uint64_t to, int side) const
    {
        float most = 0;
        for (uint64_t f = from; f < std::min(to, frames); ++f)
        {
            most = std::max(most, std::fabs(out[f * 2 + side]));
        }
        return most;
    }

    float average(uint64_t from, uint64_t to, int side) const
    {
        double sum = 0;
        for (uint64_t f = from; f < to; ++f)
        {
            sum += out[f * 2 + side];
        }
        return static_cast<float>(sum / static_cast<double>(to - from));
    }
};

inline uint64_t frameOf(double seconds)
{
    return static_cast<uint64_t>(std::llround(seconds * mixRate));
}

// A sound made in code, destroyed with the test.
struct TestSound
{
    assets::AssetId id{};

    TestSound(std::vector<int16_t> samples, uint32_t rate = mixRate, uint32_t channels = 1, float bpm = 0,
              uint32_t beatsPerBar = 4)
    {
        const assets::SoundInfo info{
            rate, channels,   static_cast<uint32_t>(samples.size() / channels), assets::SoundEncoding::Pcm16,
            bpm,  beatsPerBar};
        id = createSound(info, samples);
        REQUIRE(id.bytes[0] != 0);
    }

    ~TestSound()
    {
        destroySound(id);
    }

    TestSound(const TestSound&) = delete;
    TestSound& operator=(const TestSound&) = delete;
};

// A steady level (DC) for `frames` frames: easy to measure after gains.
inline std::vector<int16_t> steady(float level, uint32_t frames)
{
    return std::vector<int16_t>(frames, static_cast<int16_t>(std::lround(level * 32768.0f)));
}

inline std::vector<int16_t> sine(float hertz, double seconds, float amplitude = 0.5f, uint32_t rate = mixRate)
{
    std::vector<int16_t> samples(static_cast<size_t>(seconds * rate));
    for (size_t i = 0; i < samples.size(); ++i)
    {
        samples[i] = static_cast<int16_t>(
            std::lround(amplitude * 32767.0 * std::sin(2 * 3.14159265358979 * hertz * static_cast<double>(i) / rate)));
    }
    return samples;
}

// The listener's own position: heard in the middle at full volume.
constexpr Vector3 here{0, 0, 0};

} // namespace crucible::audio::testing
