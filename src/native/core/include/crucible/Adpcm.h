#pragma once

// IMA ADPCM (ADR 0083): 4 bits a sample. Each sample is stored as a step
// from the last one's value, in units of a step size that grows when the
// signal moves fast and shrinks when it moves slowly (an index into
// stepSizes). The encoder runs the decoder alongside itself, so both stay
// in step.
//
// A block holds blockFrames frames; each channel's part of it is a header
// (the first sample, int16, then the step index, uint8, and a zero byte)
// and the other blockFrames - 1 samples as nibbles, low nibble first,
// padded to a whole byte. Blocks start fresh, so playback can start or loop
// at any one. The importer encodes; the mixer decodes as it plays.

#include <crucible/Expose.h>

#include <algorithm>
#include <cstdint>
#include <cstring>
#include <span>

namespace crucible::audio::adpcm {

inline constexpr int16_t stepSizes[89] = {
    7,    8,     9,     10,    11,    12,    13,    14,    16,    17,    19,    21,    23,    25,   28,
    31,   34,    37,    41,    45,    50,    55,    60,    66,    73,    80,    88,    97,    107,  118,
    130,  143,   157,   173,   190,   209,   230,   253,   279,   307,   337,   371,   408,   449,  494,
    544,  598,   658,   724,   796,   876,   963,   1060,  1166,  1282,  1411,  1552,  1707,  1878, 2066,
    2272, 2499,  2749,  3024,  3327,  3660,  4026,  4428,  4871,  5358,  5894,  6484,  7132,  7845, 8630,
    9493, 10442, 11487, 12635, 13899, 15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794, 32767};

// How the step index moves after each nibble (by its magnitude bits).
inline constexpr int8_t indexSteps[8] = {-1, -1, -1, -1, 2, 4, 6, 8};

// Bytes one channel takes in a block.
constexpr uint32_t channelBytes(uint32_t blockFrames)
{
    return 4 + blockFrames / 2;
}

constexpr uint32_t blockBytes(uint32_t blockFrames, uint32_t channels)
{
    return channelBytes(blockFrames) * channels;
}

struct State
{
    int32_t value = 0;
    int32_t index = 0;
};

// One nibble's sample, moving the state on.
inline int16_t decodeNibble(State& state, uint8_t nibble)
{
    const int32_t step = stepSizes[state.index];
    int32_t difference = step >> 3;
    if (nibble & 4)
    {
        difference += step;
    }
    if (nibble & 2)
    {
        difference += step >> 1;
    }
    if (nibble & 1)
    {
        difference += step >> 2;
    }
    state.value = std::clamp(state.value + ((nibble & 8) ? -difference : difference), -32768, 32767);
    state.index = std::clamp(state.index + indexSteps[nibble & 7], 0, 88);
    return static_cast<int16_t>(state.value);
}

// The nibble whose step comes nearest the sample, the state moved as the
// decoder will move it.
inline uint8_t encodeSample(State& state, int16_t sample)
{
    const int32_t step = stepSizes[state.index];
    int32_t difference = sample - state.value;
    uint8_t nibble = 0;
    if (difference < 0)
    {
        nibble = 8;
        difference = -difference;
    }
    if (difference >= step)
    {
        nibble |= 4;
        difference -= step;
    }
    if (difference >= step >> 1)
    {
        nibble |= 2;
        difference -= step >> 1;
    }
    if (difference >= step >> 2)
    {
        nibble |= 1;
    }
    (void)decodeNibble(state, nibble);
    return nibble;
}

// One block from `frames` frames of interleaved samples (fewer than
// blockFrames at the end: the rest are silence).
inline void encodeBlock(std::span<const int16_t> samples, uint32_t frames, uint32_t channels, uint32_t blockFrames,
                        std::span<uint8_t> block)
{
    std::fill(block.begin(), block.end(), uint8_t{0});
    auto at = [&](uint32_t frame, uint32_t channel) -> int16_t {
        return frame < frames ? samples[size_t{frame} * channels + channel] : int16_t{0};
    };
    for (uint32_t channel = 0; channel < channels; ++channel)
    {
        uint8_t* out = block.data() + size_t{channel} * channelBytes(blockFrames);
        State state{at(0, channel), 0};
        // A starting step near the first difference, so the block settles quickly.
        const int32_t first = std::abs(at(1, channel) - at(0, channel));
        while (state.index < 88 && stepSizes[state.index] < first)
        {
            ++state.index;
        }
        const int16_t head = static_cast<int16_t>(state.value);
        std::memcpy(out, &head, 2);
        out[2] = static_cast<uint8_t>(state.index);
        for (uint32_t frame = 1; frame < blockFrames; ++frame)
        {
            const uint8_t nibble = encodeSample(state, at(frame, channel));
            const uint32_t n = frame - 1;
            out[4 + n / 2] |= static_cast<uint8_t>((n & 1) != 0 ? nibble << 4 : nibble);
        }
    }
}

// A whole block back to interleaved samples (blockFrames x channels).
inline void decodeBlock(const uint8_t* block, uint32_t channels, uint32_t blockFrames, int16_t* samples)
{
    for (uint32_t channel = 0; channel < channels; ++channel)
    {
        const uint8_t* in = block + size_t{channel} * channelBytes(blockFrames);
        int16_t head;
        std::memcpy(&head, in, 2);
        State state{head, std::min<int32_t>(in[2], 88)};
        samples[channel] = head;
        for (uint32_t frame = 1; frame < blockFrames; ++frame)
        {
            const uint32_t n = frame - 1;
            const uint8_t nibble = (in[4 + n / 2] >> ((n & 1) * 4)) & 0xF;
            samples[size_t{frame} * channels + channel] = decodeNibble(state, nibble);
        }
    }
}

} // namespace crucible::audio::adpcm
