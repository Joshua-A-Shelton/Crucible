#pragma once

// The imported sound, .crsound (ADR 0082): what the mixer plays. Step 6 of
// M8 imports WAV, FLAC, and OGG into it; until then sounds come from
// audio::createSound.
//
// A SOND file (kind tag("SOND"), version 1):
//   INFO  SoundInfo
//   PCMS  int16 samples, interleaved by channel (Pcm16)
//
// Music carries its tempo, so a crossfade can wait for a beat or a bar.

#include <crucible/BinaryFile.h>
#include <crucible/Expose.h>

#include <cstdint>

namespace crucible::assets {

inline constexpr uint32_t soundFileKind = binary::tag("SOND");
inline constexpr uint32_t soundFileVersion = 1;

enum class CRUCIBLE_EXPOSE SoundEncoding : uint32_t
{
    Pcm16,
};

struct CRUCIBLE_EXPOSE SoundInfo
{
    uint32_t sampleRate; // frames a second
    uint32_t channels;   // 1 or 2
    uint32_t frameCount;
    SoundEncoding encoding;
    float beatsPerMinute; // 0: no tempo (music aligns as if Now)
    uint32_t beatsPerBar;
};

} // namespace crucible::assets
