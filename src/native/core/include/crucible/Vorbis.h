#pragma once

// Ogg Vorbis decoding (ADR 0083), through stb_vorbis: the mixer's streams
// decode with it as they play, and the Sound importer measures .ogg files
// with it (and decodes them when they're imported as PCM or ADPCM).

#include <crucible/Export.h>
#include <crucible/SoundFile.h>

#include <cstdint>
#include <span>

namespace crucible::audio {

struct VorbisDecoder;

// Opens an Ogg Vorbis file in memory (not copied: it must outlive the
// decoder). Null if it isn't one, or has more than two channels. Any thread.
CRUCIBLE_EXPORT VorbisDecoder* openVorbis(std::span<const uint8_t> ogg);
CRUCIBLE_EXPORT void closeVorbis(VorbisDecoder* decoder);
// Its rate, channels, and length (encoding Vorbis).
CRUCIBLE_EXPORT assets::SoundInfo vorbisInfo(const VorbisDecoder& decoder);
// Decodes the next frames into `into` (interleaved, as many frames as fit);
// returns how many it decoded, 0 at the end.
CRUCIBLE_EXPORT uint32_t decodeVorbis(VorbisDecoder& decoder, std::span<int16_t> into);
// Back to the start, for looping.
CRUCIBLE_EXPORT bool rewindVorbis(VorbisDecoder& decoder);

} // namespace crucible::audio
