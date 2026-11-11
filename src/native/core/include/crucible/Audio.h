#pragma once

// The audio contract (ADR 0075): what any audio implementation honours, so
// a game's code and scenes don't change when it does (Crucible's own mixer,
// ADR 0028; or FMOD or Wwise, under the game's own license). Three parts:
//
//   - Components, the data: AudioSource, AudioListener.
//   - A system, the per-frame work: "Audio.Update" (LateUpdate) hands the
//     mixer every source's and the listener's position in one batch and
//     starts sources that play on start. A gameplay system: it runs while
//     the game plays, not while editing.
//   - A service, the calls: timed one-shots, buses and their effects,
//     snapshots, music. The functions below forward to the installed
//     AudioService.
//
// An implementation (a native module, ADR 0074) registers "Audio.Update",
// replacing the default, and installs its service. The engine's own is
// Crucible's mixer (ADR 0082), started by the host when an audio device
// opens (startMixing) or by a headless check (startOfflineMixing). Until one
// is, the null audio stands in: silence that counts what it was asked to do.

#include <crucible/Ecs.h>
#include <crucible/Export.h>
#include <crucible/Expose.h>
#include <crucible/Math.h>
#include <crucible/RenderAssets.h>
#include <crucible/SoundFile.h>

#include <cstdint>
#include <span>
#include <string_view>

namespace crucible::audio {

inline constexpr std::string_view updateSystemName = "Audio.Update";

// Buses mix sounds together: 0 is the master, which the others feed.
inline constexpr uint32_t masterBus = 0;
inline constexpr uint32_t noBus = 0xFFFFFFFFu;

// Crucible's mixer (ADR 0082): it mixes in stereo at mixRate, at most
// maxVoices sounds at once on maxBuses buses. The last bus is the shared
// reverb, which buses' reverb sends feed; music plays on musicBus.
inline constexpr uint32_t mixRate = 48000;
inline constexpr uint32_t maxVoices = 128;
inline constexpr uint32_t maxBuses = 16;
inline constexpr uint32_t reverbBus = maxBuses - 1;
inline constexpr uint32_t musicBus = 1;
// How long after its timestamp a scheduled sound starts: enough to cover a
// frame at 30 fps (events are timestamped up to a frame in the past) and
// the trip to the audio thread, so every sound is late by the same amount.
inline constexpr float defaultScheduleDelay = 0.05f;

enum class CRUCIBLE_EXPOSE Rolloff : uint32_t
{
    Linear,  // full at minDistance, silent at maxDistance
    Inverse, // halves with each doubling past minDistance, silent at maxDistance
    None,    // full volume everywhere
};

// A sound at its entity's position (or everywhere, unless spatial).
struct CRUCIBLE_EXPOSE AudioSource
{
    CRUCIBLE_HINT_ASSET(".crsound") assets::AssetId clip;
    CRUCIBLE_HINT_RANGE(0, 2) float volume;
    CRUCIBLE_HINT_RANGE(0.25f, 4) float pitch;
    bool loop;
    bool playOnStart; // starts when the game starts, or when the source appears
    uint32_t bus;
    bool spatial;
    CRUCIBLE_HINT_RANGE(0, 100) float minDistance; // meters
    CRUCIBLE_HINT_RANGE(0, 1000) float maxDistance;
    Rolloff rolloff;
    CRUCIBLE_HINT_RANGE(0, 1) float occlusion; // how much is in the way: 0 none, 1 all; a backend may use it
};

// The entity whose position and facing the game is heard from (one).
struct CRUCIBLE_EXPOSE AudioListener
{};

// A bus's effects.
struct CRUCIBLE_EXPOSE BusEffects
{
    float lowPassHz; // 0: off
    CRUCIBLE_HINT_RANGE(0, 1) float reverbSend;
    uint32_t duckedBy; // a bus whose sound lowers this one (noBus: none)
    CRUCIBLE_HINT_RANGE(0, 1) float duckAmount;
};

enum class CRUCIBLE_EXPOSE MusicAlign : uint32_t
{
    Now,
    Beat, // on the playing track's next beat
    Bar,  // on its next bar
};

struct CRUCIBLE_EXPOSE MusicTransition
{
    float crossfadeSeconds;
    MusicAlign align;
};

// What an implementation provides for the calls below. Main thread.
struct AudioService
{
    uint32_t (*playOneShot)(assets::AssetId clip, uint64_t atNs, Vector3 position, float volume, uint32_t bus);
    void (*stop)(uint32_t voice, float fadeSeconds);
    void (*setBusVolume)(uint32_t bus, float volume, float seconds);
    void (*setBusEffects)(uint32_t bus, BusEffects effects);
    void (*defineSnapshot)(std::string_view name, uint32_t bus, float volume, BusEffects effects);
    void (*blendToSnapshot)(std::string_view name, float seconds);
    void (*playMusic)(assets::AssetId track, MusicTransition transition);
    void (*stopAll)(float fadeSeconds);
    void (*pauseAll)(bool paused);        // null: nothing to pause
    void (*advancePaused)(float seconds); // null likewise
};

// Replaces the service; the default is the null audio's. Main thread.
CRUCIBLE_EXPORT void installAudio(const AudioService& service);
// The installed service.
CRUCIBLE_EXPORT const AudioService& audioService();

// Plays a clip once, at atNs on the input clock (platform::timestampNow,
// ADR 0028's sample-accurate scheduling; 0: now), at a position (heard
// from everywhere if it's the listener's). Returns its voice, for stop.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE uint32_t playOneShot(assets::AssetId clip, uint64_t atNs, Vector3 position,
                                                     float volume, uint32_t bus);
// Stops a voice, fading out over fadeSeconds.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE void stop(uint32_t voice, float fadeSeconds);
// A bus's volume, reached over seconds.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE void setBusVolume(uint32_t bus, float volume, float seconds);
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE void setBusEffects(uint32_t bus, BusEffects effects);
// Snapshots: named mixer states ("underwater", "paused"), each a bus's
// volume and effects, set bus by bus; blending to one moves every bus it
// names to its settings over seconds.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE void defineSnapshot(std::string_view name, uint32_t bus, float volume,
                                                    BusEffects effects);
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE void blendToSnapshot(std::string_view name, float seconds);
// Switches the music to a track, crossfading, now or on a beat or bar.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE void playMusic(assets::AssetId track, MusicTransition transition);
// Stops every sound, music and sources included, fading out over
// fadeSeconds; sources that play on start start again when next seen (the
// editor's Stop).
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE void stopAll(float fadeSeconds);
// Holds every sound where it is, music included, silent, or lets them go
// on from there (the editor's Pause, ADR 0088). Sounds scheduled for later
// move by the length of the pause, so they still land on their frames.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE void pauseAll(bool paused);
// While paused: plays the next seconds of audio, then holds again (the
// editor's Step, one fixed tick).
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE_INTERNAL void advancePaused(float seconds);

// --- Crucible's mixer (ADR 0082) --------------------------------------------

struct CRUCIBLE_EXPOSE MixerStats
{
    uint32_t voices;          // playing now
    uint32_t lateStarts;      // scheduled sounds that arrived after their time, started at once
    uint32_t underruns;       // times the device found nothing queued (an estimate of dropouts)
    float latencyEstimate;    // seconds from mixing to the speaker: the device's buffer and what's queued
    uint32_t droppedCommands; // commands the full queue turned away
    uint32_t starvedBlocks;   // blocks a Vorbis voice had nothing decoded for (ADR 0083)
};

// Opens the default audio device and installs the mixer; false (the null
// audio stays) if there's no device. Main thread.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE bool startMixing();
// The mixer without a device: the caller pulls samples with mixOffline, the
// first at startNs on the input clock (a headless check, tests).
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE void startOfflineMixing(uint64_t startNs);
// Mixes the next into.size() / 2 frames, interleaved stereo, offline.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE void mixOffline(std::span<float> into);
// Closes the device (or offline mixing) and puts the null audio back.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE void stopMixing();
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE void setScheduleDelay(float seconds);
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE float scheduleDelay();
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE MixerStats mixerStats();

// A sound made in code (frames x channels samples, interleaved), playable
// by its ID like an imported one; destroySound frees it once nothing plays it.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE assets::AssetId createSound(assets::SoundInfo info, std::span<const int16_t> samples);
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE void destroySound(assets::AssetId sound);

// What the null audio was asked to do, for tests.
struct CRUCIBLE_EXPOSE NullAudioCounts
{
    uint32_t plays; // playOneShot calls
    uint32_t stops;
    uint32_t busChanges;     // setBusVolume and setBusEffects calls
    uint32_t snapshots;      // blendToSnapshot calls
    uint32_t musicChanges;   // playMusic calls
    uint32_t sourcesStarted; // AudioSources Audio.Update started (playOnStart)
    uint32_t stopAlls;       // stopAll calls
};
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE_INTERNAL NullAudioCounts nullAudioCounts();

// Registers the components and, unless an implementation registered its
// own first, the null audio's system. The engine calls it at start-up.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE_INTERNAL void registerAudio();

} // namespace crucible::audio
