#pragma once

// The imported sound, .crsound (ADR 0082, 0083): what the mixer plays. The
// Sound importer makes it from WAV, FLAC, and OGG; audio::createSound makes
// PCM ones in code.
//
// A SOND file (kind tag("SOND"), version 1):
//   INFO  SoundInfo
//   and, by its encoding, one of:
//   PCMS  int16 samples, interleaved by channel (Pcm16)
//   ADPC  IMA ADPCM blocks of blockFrames frames (ImaAdpcm, crucible/Adpcm.h)
//   OGGV  the Ogg Vorbis file as imported (Vorbis), decoded as it plays
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
    ImaAdpcm, // 4 bits a sample: a quarter of PCM's size, decoded as it plays
    Vorbis,   // about a tenth, decoded ahead of the voice on background jobs
};

struct CRUCIBLE_EXPOSE SoundInfo
{
    uint32_t sampleRate; // frames a second
    uint32_t channels;   // 1 or 2
    uint32_t frameCount;
    SoundEncoding encoding;
    float beatsPerMinute; // 0: no tempo (music aligns as if Now)
    uint32_t beatsPerBar;
    uint32_t blockFrames; // ImaAdpcm: frames a block (even, at most maxAdpcmBlockFrames); else 0
};

inline constexpr uint32_t maxAdpcmBlockFrames = 1024;

} // namespace crucible::assets
