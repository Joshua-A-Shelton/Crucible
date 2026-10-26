# 0028 — Audio

**Status:** Accepted (2026-10-25).

## Context

An audio system outputs to the device in small, continuous buffers (late
buffers click or drop out); mixes playing sounds into buses with volumes,
ducking, and effects; spatializes 3D sounds; and decodes or streams audio
data.

Goal 4 applies to sound in two ways: **output latency** (the device buffer;
256–512 samples at 48 kHz is about 5–10 ms) and **timing jitter**. Sounds
started only at frame boundaries play up to a frame late, by a different
amount each time (0–33 ms at 30 fps), which is audible in rhythmic sounds
like footsteps.

## Options

| Option | License | Notes |
|---|---|---|
| SDL3 audio | zlib | Already a dependency (ADR 0018); device output and resampling only |
| miniaudio | Public domain or MIT-0 | Device output, decoders, optional engine with node graph and spatializer |
| SoLoud | zlib | Complete engine; older, less active |
| OpenAL Soft | LGPL | Excluded |
| FMOD, Wwise | Proprietary | Excluded as defaults |
| Steam Audio | Apache 2.0 | HRTF, occlusion, physically based reverb; an optional extra |

Decoders: dr_wav, dr_flac (public domain or MIT-0), stb_vorbis (public
domain or MIT).

## Decision

1. **Device output uses SDL3 audio;** miniaudio is the fallback if SDL3's
   latency measures poorly on a platform.
2. **Crucible's own mixer** runs on a **dedicated real-time audio thread**:
   buses, volumes, ducking, low-pass, a simple reverb, 3D panning, and
   distance attenuation. The game talks to it through a lock-free command
   queue, so the audio thread never waits on the game. This is a recorded
   exception to ADR 0014's single job pool.
3. **Sample-accurate scheduling:** each sound is scheduled at its exact
   event time (ADR 0026) plus a small fixed delay, and the mixer starts it
   at the matching sample.
4. **Formats:** WAV, FLAC, and OGG are imported in the editor (dr_wav,
   dr_flac, stb_vorbis). Short effects are stored as PCM or ADPCM; music
   and long sounds as Vorbis, decoded at runtime with stb_vorbis and
   streamed from disk on background jobs (ADR 0014).
5. **Spatial audio:** panning and attenuation in v1; Steam Audio (HRTF,
   occlusion) after v1, as an optional plugin.
6. **Audio sits behind Crucible's audio API,** a replaceable subsystem
   (ADR 0006), so a game can plug in FMOD or Wwise under its own license.

## Why

SDL3 audio adds no dependency and keeps one platform layer for windows,
input, and audio. A mixer is small next to a physics engine and a good
learning project in basic DSP. Audio has hard deadlines, so mixing can't
wait behind frame jobs; a dedicated thread with a lock-free queue is the
industry-standard design.

Scheduling at exact event times gives every sound the same small delay
instead of a random one, so input, animation, hit detection, and audio
share one clock and timing holds at any frame rate. PCM and ADPCM start
instantly; Vorbis keeps music small.

Given up, for now: HRTF and occlusion, and middleware by default.
