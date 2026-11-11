#pragma once

// Sound import (ADR 0083): WAV (dr_wav), FLAC (dr_flac), or Ogg Vorbis
// (stb_vorbis) into a .crsound (SoundFile.h). WAV and FLAC are decoded,
// processed (mono, trimmed, normalized, as asked), and stored as 16-bit PCM
// or IMA ADPCM; an .ogg is kept as it is (Vorbis), or decoded and stored
// like the others. The C# importer writes the file. Editor-only.

#include <crucible/Expose.h>
#include <crucible/Result.h>
#include <crucible/SoundFile.h>

#include <cstdint>
#include <span>
#include <string_view>

namespace crucible::importers {

using assets::SoundInfo;

enum class CRUCIBLE_EXPOSE SoundImportEncoding : uint32_t
{
    Auto,   // .ogg stays Vorbis; WAV and FLAC are PCM, or ADPCM past adpcmAfterSeconds
    Pcm,    // 16-bit, exact
    Adpcm,  // IMA ADPCM, a quarter of PCM's size
    Vorbis, // .ogg only: kept as it is (there's no Vorbis encoder)
};

struct CRUCIBLE_EXPOSE SoundSettings
{
    SoundImportEncoding encoding;
    float adpcmAfterSeconds;
    float beatsPerMinute; // music's tempo, for crossfades on a beat or bar; 0 for none
    uint32_t beatsPerBar;
    bool mono;              // mix stereo down to one channel
    bool normalize;         // scale so the loudest sample reaches normalizePeak
    float normalizePeak;    // dBFS
    bool trimSilence;       // cut what's quieter than silenceThreshold from both ends
    float silenceThreshold; // dBFS
};

struct CRUCIBLE_EXPOSE SoundImport;

// Imports a WAV, FLAC, or Ogg Vorbis file, known by its first bytes.
// InvalidData if it can't, with the reason in lastImportError().
CRUCIBLE_EXPOSE Result<SoundImport*> importSound(std::span<const uint8_t> source, SoundSettings settings);
CRUCIBLE_EXPOSE void destroySoundImport(SoundImport* sound);

CRUCIBLE_EXPOSE SoundInfo soundImportInfo(const SoundImport& sound);
// The data section's tag (PCMS, ADPC, or OGGV) and bytes.
CRUCIBLE_EXPOSE uint32_t soundDataTag(const SoundImport& sound);
CRUCIBLE_EXPOSE uint64_t soundDataSize(const SoundImport& sound);
CRUCIBLE_EXPOSE void copySoundData(const SoundImport& sound, std::span<uint8_t> destination);
// What the import couldn't do as asked (a setting that doesn't apply to Vorbis).
CRUCIBLE_EXPOSE uint32_t soundWarningCount(const SoundImport& sound);
CRUCIBLE_EXPOSE std::string_view soundWarning(const SoundImport& sound, uint32_t index);

} // namespace crucible::importers
