# 0082 — The mixer

**Status:** Accepted (2026-11-10); M8 step 5 built. Names settled by the
owner as drafted (docs/naming.md); names added while building are marked *.

## Context

ADR 0028 chose SDL3 for device output and Crucible's own mixer on a
dedicated audio thread, with a lock-free command queue and sample-accurate
scheduling. ADR 0075 defined the audio contract (`AudioSource`,
`AudioListener`, "Audio.Update", one-shots at exact times, buses and their
effects, snapshots, music) with a null implementation. Step 5 builds the
mixer behind that contract. Step 6 imports sound files; step 7 schedules
step 4's footsteps through it.

## Decision

Decided by the owner (2026-11-10):

1. **In the core.** Crucible's mixer is the engine's default audio: the host
   starts it when a device opens (`startMixing`), and the null audio stays
   for tests and when there's none. A native module can still replace it.
2. **SDL's stream callback mixes.** SDL3's audio stream asks for exactly
   the frames the device needs, on SDL's own high-priority device thread,
   which is ADR 0028's dedicated audio thread. No second buffer, no extra
   block of latency.
3. **A fixed, configurable schedule delay,** 50 ms by default
   (`setScheduleDelay`). Step 4's events are timestamped up to a frame in
   the past, so the delay covers a 30 fps frame plus the trip to the audio
   thread; every sound is late by the same amount, so rhythm is exact. A
   sound that still arrives after its time starts at once and is counted
   (`MixerStats::lateStarts`).
4. **The whole contract now:** one-shots and sources, buses with volume,
   low-pass, a reverb send, ducking, snapshots, 3D panning and attenuation,
   and music crossfades now, on a beat, or on a bar.
5. **The runtime sound format now** (`.crsound`, 16-bit PCM), with
   `createSound` for sounds made in code; step 6 imports into it.
6. **Cubic Hermite resampling, stereo output, a Freeverb-style reverb.**

## How it works

- **Threads.** The main thread's side (MixerService.cpp*) owns sounds,
  the service, and "Audio.Update". The audio thread's side (Mixer.cpp*)
  owns voices and buses. They share two single-producer single-consumer
  rings: commands in (4096), feedback out (1024: voices ended, sounds no
  longer played). The audio thread never locks, allocates, or reads files.
  A full command ring turns commands away (`droppedCommands`).
- **The clock.** Each callback records which output frame it's mixing and
  when, on the input clock. Callbacks come in bursts, so the time is
  smoothed against the frames mixed (2% of each error), and a jump of more
  than 200 ms resets it. A scheduled sound starts at the frame its
  timestamp plus the delay falls on, inside a block if need be. Offline
  mixing uses exact times, so tests are exact to the frame.
- **Voices.** Each block (up to 512 frames), each voice reads its sound at
  `pitch × sound rate / 48 kHz` per frame, by cubic Hermite through four
  samples. Spatial voices are attenuated by their rolloff (one-shots: full
  to 1 m, inverse to 50 m, then silent) and panned at constant power by the
  direction to the listener's right. Gains ramp across a block; fades
  (stops, crossfades) move sample by sample from the frame they start, so a
  cut on a bar lands on the bar. A new voice starts at its full gain, so
  onsets are as sharp as the sound.
- **Buses.** 16; 0 is the master, 1 the music bus (`musicBus`*), 15 the
  shared reverb (`reverbBus`). Each applies a one-pole low-pass, ducking,
  and its volume, then adds into the master and, by its send, into the
  reverb. Effects and volumes glide over their seconds. Ducking follows the
  ducking bus's peak level (a quick rise, 0.3 s fall): at -12 dBFS
  or louder it applies the whole `duckAmount`, quieter proportionally less,
  using the previous block's level so bus order doesn't matter.
- **The reverb** is Freeverb (Jezar's public-domain design): eight parallel
  damped combs into four series all-passes per side, the right side's
  delays 23 samples longer, lengths scaled from 44.1 to 48 kHz.
- **The master** is clamped to [-1, 1].
- **Music** plays on the music bus and loops. A switch aligned to a beat or
  bar starts the new track on the playing track's next beat or bar,
  counted from its start, by its `SoundInfo` tempo (`beatsPerMinute`,
  `beatsPerBar`; step 6 sets them on import). Without a tempo it switches
  at once.
- **Snapshots** are kept on the main thread: blending to one sends each of
  its buses' volume and effects, gliding over the seconds.
- **"Audio.Update"** sends the listener (the first `AudioListener`, by its
  world transform), starts sources that play on start (once), sends each
  playing source's volume, pitch, and position every frame, and stops the
  sources whose entity or component went (50 ms fade).
- **`stopAll`*** (added to the contract) stops every sound and forgets the
  sources, so the editor's Stop silences the game and the next Play starts
  its sources again.
- **Latency** is estimated, not measured: the device's buffer and SDL's
  stream buffer (twice the device's sample frames). The host logs it at
  start (about 11 ms on the owner's Linux machine with PipeWire). Dropouts
  are estimated too: a callback much later than the last one's frames
  last. Measuring true latency needs a loopback cable
  (docs/open-items.md).

## As built

- crucible/SoundFile.h; crucible/Audio.h (the mixer's calls, `stopAll`,
  `MemoryTag::Audio`*); src/native/core/Mixer.h, Mixer.cpp,
  MixerService.cpp; SDL's audio subsystem turned on
  (cmake/CrucibleDependencies.cmake).
- C#: `AudioMixer` (`ScheduleDelay`, `Stats`, `CreateSound`*,
  `DestroySound`*, and the constants); the host starts and stops the mixer
  in windowed runs; the editor's Stop calls `stopAll`.
- samples/Metronome, and its headless check (`samples.metronome`*): the
  clicks scheduled from frames through the real loop, mixed offline, each
  onset at its frame exactly.
- Tests: `MixerTests`, `AudioSchedulingTests`, `AudioEffectTests`,
  `SpatialAudioTests`, `MusicTests`, all offline.
- Not done: the `occlusion` field is ignored (the contract lets a backend
  ignore it); surround output; Vorbis streaming and ADPCM (step 6).
