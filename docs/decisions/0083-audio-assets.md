# 0083 — Audio assets

**Status:** Accepted (2026-11-10); M8 step 6 built, then tried by hand by
the owner (an .ogg and a WAV of their own imported in the editor, settings
changed and reimported, played by an AudioSource). Names, including those
added while building, settled by the owner as drafted (docs/naming.md).

## Context

The mixer (ADR 0082) plays `.crsound` files, but only sounds made in code
existed. ADR 0028 chose WAV, FLAC, and OGG imported in the editor (dr_wav,
dr_flac, stb_vorbis), short effects stored as PCM or ADPCM, and music as
Vorbis, decoded at runtime on background jobs.

## Decision

Decided by the owner (2026-11-10):

1. **The libraries are approved:** dr_wav 0.14.6 and dr_flac 0.13.4 (public
   domain or MIT-0), editor-only in the importers, pinned at the commit both
   released from; stb_vorbis (public domain or MIT), in the importers and
   the shipped core, from the stb commit already pinned for stb_image.
   Neither licence option chosen asks for a notice, so licenses/ is
   unchanged.
2. **Vorbis comes only from .ogg sources,** kept byte for byte: there's no
   Vorbis encoder (libvorbis is BSD-3). WAV and FLAC become PCM or ADPCM.
3. **Vorbis streams from memory:** the compressed file is loaded whole and
   decoded a little ahead of each voice on background jobs. The asset
   source's contract (ADR 0076) is unchanged; disk streaming can come with
   pack files.
4. **IMA ADPCM** for compressed effects: 4 bits a sample in blocks that
   start fresh, so playback can start or loop at any block.
5. **Auto encoding:** .ogg stays Vorbis; WAV and FLAC are PCM up to 5 s
   (`AdpcmAfterSeconds`) and ADPCM past it.
6. **Processing:** the source rate is kept (the mixer resamples as it
   plays); `Mono`, `Normalize` (to a peak in dBFS), and `TrimSilence`
   (below a threshold in dBFS) are settings, off by default.

## How it works

- **Import** (crucible/importers/Sound.h, the C# `SoundImporter`). The format
  is known by its first bytes. WAV and FLAC are decoded to floats (dr_libs,
  their allocations through Crucible's allocator); an .ogg imported as PCM
  or ADPCM is decoded through the core's stb_vorbis. Then mono, trim, and
  normalize, in that order, and PCM (rounded, clamped) or ADPCM (1024-frame
  blocks). An .ogg kept as Vorbis is only measured; the settings that change
  samples warn that they don't apply. Asking Vorbis of a WAV warns and
  imports it as Auto. The tempo (`BeatsPerMinute`, `BeatsPerBar`) goes into
  `SoundInfo` for the mixer's beat- and bar-aligned music.
- **The file** (SoundFile.h): `INFO` and one data section: `PCMS`, `ADPC`, or
  `OGGV`. `SoundInfo::blockFrames`* says how long an ADPCM block is.
- **IMA ADPCM** (crucible/Adpcm.h*): each sample is a 4-bit step from the
  last one, in units of a step size that adapts (89 sizes); each channel's
  part of a block has a header (the first sample and step index) and the
  rest as nibbles. The encoder runs the decoder alongside itself. A 440 Hz
  tone comes back above 30 dB signal to noise (`AdpcmTests`).
- **ADPCM playback:** the mixer decodes a whole block when a voice first
  reads into it, keeping each voice's last two (the four samples cubic
  Hermite reads span at most two blocks): 1 MB, allocated with the mixer.
- **Vorbis playback** (crucible/Vorbis.h*, Vorbis.cpp*):
  - A voice of a Vorbis sound gets a **stream**: a ring of one second of
    decoded frames, counted from the voice's start (laps included). The
    main thread decodes the first half second before sending the play, so
    the voice starts at once.
  - **The pump** (each "Audio.Update", each service call, each offline mix)
    gives every stream less than half full a background job, which decodes
    until the ring is full again (rewinding at the end if it loops).
  - **The ring** has one writer (the job: `written`) and one reader (the audio
    thread: `released`, two frames behind the voice for Hermite), so neither
    waits.
  - **Starving:** a voice reading past what's decoded plays silence and counts
    a starved block (`MixerStats::starvedBlocks`).
  - **Freeing:** a stream is freed when its voice ends, after its job
    finishes.
  - **stb_vorbis memory:** it allocates from a buffer it's given, from
    Crucible's allocator (256 KB, doubled until it fits).
- **The editor** imports .wav, .flac, and .ogg.

## As built

- crucible/SoundFile.h (`ImaAdpcm`, `Vorbis`, `blockFrames`*,
  `maxAdpcmBlockFrames`*), crucible/Adpcm.h*, crucible/Vorbis.h*;
  src/native/core/Vorbis.cpp*, the mixer's ADPCM cache and streams;
  src/native/importers/Sound.cpp*, DrLibs.c*; cmake: dr_libs fetched,
  stb_vorbis built in the core (and listed for NativeAOT links).
- Tests: `SoundImportTests`, `AdpcmTests`, `VorbisStreamingTests` (native),
  `SoundImporterTests` (C#), on tests/native/data/sounds: a 440 Hz tone in
  WAV, FLAC (stereo, 44.1 kHz), and OGG, made by `make_sounds.sh` with
  ffmpeg.
- Not done: streaming from disk; Vorbis encoding; more than two channels
  (refused at import).
