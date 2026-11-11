// The audio contract's engine side (ADR 0075): the installed service and
// the null audio that stands in until an implementation replaces it (the
// engine's own mixer, ADR 0082: MixerService.cpp).

#include "Mixer.h"

#include <crucible/Audio.h>
#include <crucible/HashMap.h>
#include <crucible/Systems.h>

#include <atomic>

namespace crucible::audio {

namespace {

// What the null audio was asked; the main thread writes, tests read.
struct Counts
{
    std::atomic<uint32_t> plays{0};
    std::atomic<uint32_t> stops{0};
    std::atomic<uint32_t> busChanges{0};
    std::atomic<uint32_t> snapshots{0};
    std::atomic<uint32_t> musicChanges{0};
    std::atomic<uint32_t> sourcesStarted{0};
    std::atomic<uint32_t> stopAlls{0};
    std::atomic<uint32_t> nextVoice{1};
};

Counts counts;

uint32_t countPlay(assets::AssetId, uint64_t, Vector3, float, uint32_t)
{
    ++counts.plays;
    return counts.nextVoice++;
}

void countStop(uint32_t, float)
{
    ++counts.stops;
}

void countBusVolume(uint32_t, float, float)
{
    ++counts.busChanges;
}

void countBusEffects(uint32_t, BusEffects)
{
    ++counts.busChanges;
}

void ignoreSnapshot(std::string_view, uint32_t, float, BusEffects) {}

void countSnapshot(std::string_view, float)
{
    ++counts.snapshots;
}

void countMusic(assets::AssetId, MusicTransition)
{
    ++counts.musicChanges;
}

struct Started;
void countStopAll(float);

constexpr AudioService nullAudio{countPlay,     countStop,  countBusVolume, countBusEffects, ignoreSnapshot,
                                 countSnapshot, countMusic, countStopAll,   nullptr,         nullptr};

AudioService installed = nullAudio;

// The sources the null audio has started, per world, so each starts once.
struct Started
{
    ecs::World* world = nullptr;
    HashMap<uint64_t, bool> entities{MemoryTag::General};
    ecs::Query* sources = nullptr;
};

Started started;

void countStopAll(float)
{
    ++counts.stopAlls;
    started.entities.clear();
}

void update(const loop::SystemContext& context, void*)
{
    // Crucible's mixer, once started, does the work.
    if (updateMixerSources(context))
    {
        return;
    }
    if (started.world != context.world)
    {
        if (started.sources != nullptr)
        {
            ecs::destroyQuery(started.sources);
        }
        const ecs::ComponentId required[] = {ecs::componentIdOf<AudioSource>};
        started.sources = ecs::createQuery(*context.world, {.required = required});
        started.entities.clear();
        started.world = context.world;
    }
    ecs::forEachChunk(
        *started.sources,
        [](const ecs::ChunkView& view, void*) {
            const std::span<const AudioSource> sources = ecs::readColumn<AudioSource>(view);
            const ecs::Entity* entities = ecs::entitiesOf(view);
            for (size_t i = 0; i < sources.size(); ++i)
            {
                if (sources[i].playOnStart && started.entities.find(entities[i].value) == nullptr)
                {
                    started.entities.insertOrAssign(entities[i].value, true);
                    ++counts.sourcesStarted;
                }
            }
        },
        nullptr);
}

} // namespace

void installAudio(const AudioService& service)
{
    installed = service;
}

const AudioService& audioService()
{
    return installed;
}

uint32_t playOneShot(assets::AssetId clip, uint64_t atNs, Vector3 position, float volume, uint32_t bus)
{
    return installed.playOneShot(clip, atNs, position, volume, bus);
}

void stop(uint32_t voice, float fadeSeconds)
{
    installed.stop(voice, fadeSeconds);
}

void setBusVolume(uint32_t bus, float volume, float seconds)
{
    installed.setBusVolume(bus, volume, seconds);
}

void setBusEffects(uint32_t bus, BusEffects effects)
{
    installed.setBusEffects(bus, effects);
}

void defineSnapshot(std::string_view name, uint32_t bus, float volume, BusEffects effects)
{
    installed.defineSnapshot(name, bus, volume, effects);
}

void blendToSnapshot(std::string_view name, float seconds)
{
    installed.blendToSnapshot(name, seconds);
}

void playMusic(assets::AssetId track, MusicTransition transition)
{
    installed.playMusic(track, transition);
}

void stopAll(float fadeSeconds)
{
    installed.stopAll(fadeSeconds);
}

void pauseAll(bool paused)
{
    if (installed.pauseAll != nullptr)
    {
        installed.pauseAll(paused);
    }
}

void advancePaused(float seconds)
{
    if (installed.advancePaused != nullptr)
    {
        installed.advancePaused(seconds);
    }
}

NullAudioCounts nullAudioCounts()
{
    return {counts.plays,        counts.stops,          counts.busChanges, counts.snapshots,
            counts.musicChanges, counts.sourcesStarted, counts.stopAlls};
}

const AudioService& nullAudioService()
{
    return nullAudio;
}

void registerAudio()
{
    ecs::registerComponent<AudioSource>("Crucible.Audio.AudioSource");
    ecs::registerComponent<AudioListener>("Crucible.Audio.AudioListener");
    loop::registerDefaultSystem(
        {.name = updateSystemName, .phase = "LateUpdate", .function = update, .gameplay = true});
}

} // namespace crucible::audio
