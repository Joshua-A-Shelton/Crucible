// Crucible's mixer, the main thread's side (ADR 0082): the SDL device whose
// callback mixes, offline mixing, the sounds the voices play, the audio
// service, and "Audio.Update" for AudioSources and the listener. Commands go
// to the audio thread through the mixer's queue; what comes back (voices
// ended, sounds no longer played) is read here.

#include "Mixer.h"

#include <crucible/AssetSource.h>
#include <crucible/BinaryFile.h>
#include <crucible/HashMap.h>
#include <crucible/Platform.h>
#include <crucible/Transform.h>

#include <SDL3/SDL.h>

#include <algorithm>
#include <cstring>

namespace crucible::audio {

namespace {

constexpr MemoryTag tag = MemoryTag::Audio;

struct AssetIdHash
{
    uint64_t operator()(const assets::AssetId& id) const
    {
        uint64_t low, high;
        std::memcpy(&low, id.bytes, 8);
        std::memcpy(&high, id.bytes + 8, 8);
        return Hash<uint64_t>()(low ^ (high * 0x9e3779b97f4a7c15ull));
    }
};

enum class Mode
{
    Off,
    Device,
    Offline,
};

Mode mode = Mode::Off;
Mixer* mixer = nullptr;
SDL_AudioStream* stream = nullptr;
uint64_t offlineStart = 0;
uint64_t offlineFrames = 0;
float pendingDelay = defaultScheduleDelay;
uint32_t nextVoice = 1;
uint32_t createdSounds = 0;

// Sounds by ID, loaded or created; null for one that couldn't be read, so
// it isn't read again. Destroyed ones wait in `forgetting` until the audio
// thread says nothing plays them.
HashMap<assets::AssetId, Sound*, AssetIdHash> sounds{tag};
Vector<Sound*> forgetting{tag};

struct Snapshot
{
    char name[64];
    uint32_t nameLength;
    uint32_t bus;
    float volume;
    BusEffects effects;
};
Vector<Snapshot> snapshots{tag};

// The AudioSources the mixer plays, per entity of the world it last saw.
struct SourceState
{
    uint32_t voice = 0;
    bool started = false;
    uint32_t seen = 0;
};
const ecs::World* sourceWorld = nullptr;
HashMap<uint64_t, SourceState> sourceStates{tag};
uint32_t frameMark = 0;

// --- Sounds -----------------------------------------------------------------

bool valid(const assets::SoundInfo& info, size_t samples)
{
    return info.sampleRate > 0 && (info.channels == 1 || info.channels == 2) &&
           info.encoding == assets::SoundEncoding::Pcm16 && uint64_t{info.frameCount} * info.channels <= samples;
}

Sound* loadSound(const assets::AssetId& id)
{
    Vector<uint8_t> bytes(tag);
    auto deliver = [](void* context, const uint8_t* data, uint64_t size) {
        static_cast<Vector<uint8_t>*>(context)->assign(std::span<const uint8_t>(data, static_cast<size_t>(size)));
    };
    if (assets::assetSource().read(&id, &bytes, deliver) != 0)
    {
        return nullptr;
    }
    Result<binary::File*> opened = binary::parseFile(bytes.span(), assets::soundFileKind, assets::soundFileVersion);
    if (!opened)
    {
        return nullptr;
    }
    binary::File* file = opened.value();
    Sound* sound = create<Sound>(tag);
    bool read = false;
    const int32_t info = binary::findSection(*file, binary::tag("INFO"));
    const int32_t samples = binary::findSection(*file, binary::tag("PCMS"));
    if (info >= 0 && samples >= 0 &&
        binary::sectionInfo(*file, static_cast<uint32_t>(info)).size == sizeof(assets::SoundInfo))
    {
        sound->samples.resize(static_cast<size_t>(binary::sectionInfo(*file, static_cast<uint32_t>(samples)).size) / 2);
        read = binary::readSection(*file, static_cast<uint32_t>(info),
                                   std::span(reinterpret_cast<uint8_t*>(&sound->info), sizeof(assets::SoundInfo))) &&
               binary::readSection(
                   *file, static_cast<uint32_t>(samples),
                   std::span(reinterpret_cast<uint8_t*>(sound->samples.data()), sound->samples.size() * 2));
    }
    binary::closeFile(file);
    if (!read || !valid(sound->info, sound->samples.size()))
    {
        destroy(tag, sound);
        return nullptr;
    }
    return sound;
}

const Sound* soundOf(const assets::AssetId& id)
{
    if (Sound** found = sounds.find(id))
    {
        return *found;
    }
    Sound* loaded = loadSound(id);
    sounds.insertOrAssign(id, loaded);
    return loaded;
}

// What the audio thread said: voices that ended, sounds it let go of.
void pump()
{
    Feedback feedback;
    while (mixer != nullptr && receive(*mixer, feedback))
    {
        if (feedback.kind == FeedbackKind::VoiceEnded)
        {
            for (auto& entry : sourceStates)
            {
                if (entry.value.voice == feedback.voice)
                {
                    entry.value.voice = 0;
                }
            }
            continue;
        }
        for (size_t i = 0; i < forgetting.size(); ++i)
        {
            if (forgetting[i] == feedback.sound)
            {
                destroy(tag, forgetting[i]);
                forgetting.removeSwap(i);
                break;
            }
        }
    }
}

// --- The service --------------------------------------------------------------

uint32_t mixerPlayOneShot(assets::AssetId clip, uint64_t atNs, Vector3 position, float volume, uint32_t bus)
{
    pump();
    const Sound* sound = soundOf(clip);
    if (sound == nullptr || mixer == nullptr)
    {
        return 0;
    }
    Command command{.kind = CommandKind::Play, .voice = nextVoice++, .sound = sound, .atNs = atNs};
    command.volume = volume;
    command.bus = bus;
    command.spatial = true;
    command.position = position;
    return send(*mixer, command) ? command.voice : 0;
}

void mixerStop(uint32_t voice, float fadeSeconds)
{
    Command command{.kind = CommandKind::Stop, .voice = voice};
    command.seconds = fadeSeconds;
    (void)send(*mixer, command);
}

void mixerBusVolume(uint32_t bus, float volume, float seconds)
{
    Command command{.kind = CommandKind::BusVolume};
    command.bus = bus;
    command.volume = volume;
    command.seconds = seconds;
    (void)send(*mixer, command);
}

void busEffects(uint32_t bus, BusEffects effects, float seconds)
{
    Command command{.kind = CommandKind::BusEffects};
    command.bus = bus;
    command.effects = effects;
    command.seconds = seconds;
    (void)send(*mixer, command);
}

void mixerBusEffects(uint32_t bus, BusEffects effects)
{
    busEffects(bus, effects, 0);
}

void mixerDefineSnapshot(std::string_view name, uint32_t bus, float volume, BusEffects effects)
{
    const uint32_t length = static_cast<uint32_t>(std::min<size_t>(name.size(), 64));
    for (Snapshot& snapshot : snapshots)
    {
        if (snapshot.bus == bus && std::string_view(snapshot.name, snapshot.nameLength) == name.substr(0, length))
        {
            snapshot.volume = volume;
            snapshot.effects = effects;
            return;
        }
    }
    Snapshot snapshot{{}, length, bus, volume, effects};
    std::memcpy(snapshot.name, name.data(), length);
    snapshots.pushBack(snapshot);
}

void mixerBlendToSnapshot(std::string_view name, float seconds)
{
    for (const Snapshot& snapshot : snapshots)
    {
        if (std::string_view(snapshot.name, snapshot.nameLength) == name.substr(0, std::min<size_t>(name.size(), 64)))
        {
            mixerBusVolume(snapshot.bus, snapshot.volume, seconds);
            busEffects(snapshot.bus, snapshot.effects, seconds);
        }
    }
}

void mixerPlayMusic(assets::AssetId track, MusicTransition transition)
{
    Command command{.kind = CommandKind::Music, .voice = nextVoice++, .sound = soundOf(track)};
    command.transition = transition;
    (void)send(*mixer, command);
}

void mixerStopAll(float fadeSeconds)
{
    Command command{.kind = CommandKind::StopAll};
    command.seconds = fadeSeconds;
    (void)send(*mixer, command);
    sourceStates.clear();
}

constexpr AudioService mixerService{mixerPlayOneShot, mixerStop,           mixerBusVolume,
                                    mixerBusEffects,  mixerDefineSnapshot, mixerBlendToSnapshot,
                                    mixerPlayMusic,   mixerStopAll};

// --- The device -------------------------------------------------------------

// SDL's audio thread asks for frames; they're mixed there, in pieces that
// fit a buffer made beforehand.
void SDLCALL deviceCallback(void* userData, SDL_AudioStream* audio, int additional, int)
{
    static uint64_t lastNs = 0;
    Mixer& target = *static_cast<Mixer*>(userData);
    static float buffer[1024 * 2];
    uint32_t frames = static_cast<uint32_t>(additional) / (2 * sizeof(float));
    const uint64_t now = platform::timestampNow();
    // A callback much later than the last one's frames last: the device
    // likely ran dry. An estimate; SDL doesn't report dropouts.
    if (lastNs != 0 && frames > 0 && static_cast<double>(now - lastNs) > 3e9 * frames / mixRate + 5e6)
    {
        noteUnderrun(target);
    }
    lastNs = now;
    uint64_t at = now;
    while (frames > 0)
    {
        const uint32_t piece = std::min<uint32_t>(frames, 1024);
        mix(target, buffer, piece, at);
        (void)SDL_PutAudioStreamData(audio, buffer, static_cast<int>(piece * 2 * sizeof(float)));
        at += static_cast<uint64_t>(piece * 1e9 / mixRate);
        frames -= piece;
    }
}

void start(Mode next)
{
    mixer = createMixer();
    setDelay(*mixer, pendingDelay);
    mode = next;
    sourceStates.clear();
    sourceWorld = nullptr;
    installAudio(mixerService);
}

} // namespace

bool updateMixerSources(const loop::SystemContext& context)
{
    if (mode == Mode::Off || context.world == nullptr)
    {
        return false;
    }
    pump();
    ecs::World& world = *context.world;
    if (sourceWorld != &world)
    {
        sourceStates.clear();
        sourceWorld = &world;
    }
    // The listener: the first entity with one, by its world transform.
    {
        const ecs::ComponentId required[] = {ecs::componentIdOf<AudioListener>,
                                             ecs::componentIdOf<scenes::WorldTransform>};
        ecs::Query* query = ecs::createQuery(world, {.required = required});
        Command listener{.kind = CommandKind::Listener};
        bool found = false;
        struct Found
        {
            Command* listener;
            bool* found;
        } into{&listener, &found};
        ecs::forEachChunk(
            *query,
            [](const ecs::ChunkView& view, void* data) {
                Found& out = *static_cast<Found*>(data);
                const std::span<const scenes::WorldTransform> placed = ecs::readColumn<scenes::WorldTransform>(view);
                if (*out.found || placed.empty())
                {
                    return;
                }
                const Matrix4x4& m = placed[0].matrix;
                out.listener->position = {m.rows[3].x, m.rows[3].y, m.rows[3].z};
                out.listener->forward = normalize(Vector3{m.rows[2].x, m.rows[2].y, m.rows[2].z});
                out.listener->up = normalize(Vector3{m.rows[1].x, m.rows[1].y, m.rows[1].z});
                *out.found = true;
            },
            &into);
        ecs::destroyQuery(query);
        if (found)
        {
            (void)send(*mixer, listener);
        }
    }
    // The sources: started once if they play on start, then kept where
    // their entity is.
    ++frameMark;
    const ecs::ComponentId required[] = {ecs::componentIdOf<AudioSource>};
    ecs::Query* query = ecs::createQuery(world, {.required = required});
    ecs::forEachChunk(
        *query,
        [](const ecs::ChunkView& view, void* data) {
            const ecs::World& in = *static_cast<const ecs::World*>(data);
            const std::span<const AudioSource> sources = ecs::readColumn<AudioSource>(view);
            const ecs::Entity* entities = ecs::entitiesOf(view);
            for (size_t i = 0; i < sources.size(); ++i)
            {
                const AudioSource& source = sources[i];
                SourceState* state = sourceStates.find(entities[i].value);
                if (state == nullptr)
                {
                    sourceStates.insertOrAssign(entities[i].value, SourceState{});
                    state = sourceStates.find(entities[i].value);
                }
                state->seen = frameMark;
                const scenes::WorldTransform* placed = ecs::read<scenes::WorldTransform>(in, entities[i]);
                const Vector3 position = placed != nullptr ? Vector3{placed->matrix.rows[3].x, placed->matrix.rows[3].y,
                                                                     placed->matrix.rows[3].z}
                                                           : Vector3{};
                if (!state->started && source.playOnStart)
                {
                    state->started = true;
                    const Sound* sound = soundOf(source.clip);
                    if (sound == nullptr)
                    {
                        continue;
                    }
                    Command play{.kind = CommandKind::Play, .voice = nextVoice++, .sound = sound};
                    play.volume = source.volume;
                    play.pitch = source.pitch;
                    play.bus = source.bus;
                    play.loop = source.loop;
                    play.spatial = source.spatial;
                    play.position = position;
                    play.minDistance = source.minDistance;
                    play.maxDistance = source.maxDistance;
                    play.rolloff = source.rolloff;
                    state->voice = send(*mixer, play) ? play.voice : 0;
                    continue;
                }
                if (state->voice != 0)
                {
                    Command voice{.kind = CommandKind::Voice, .voice = state->voice};
                    voice.volume = source.volume;
                    voice.pitch = source.pitch;
                    voice.position = position;
                    (void)send(*mixer, voice);
                }
            }
        },
        &world);
    ecs::destroyQuery(query);
    // Sources gone (their entity, or their AudioSource) stop.
    Vector<uint64_t> gone(tag);
    for (const auto& entry : sourceStates)
    {
        if (entry.value.seen != frameMark)
        {
            gone.pushBack(entry.key);
            if (entry.value.voice != 0)
            {
                mixerStop(entry.value.voice, 0.05f);
            }
        }
    }
    for (const uint64_t key : gone)
    {
        sourceStates.erase(key);
    }
    return true;
}

bool startMixing()
{
    if (mode != Mode::Off)
    {
        return mode == Mode::Device;
    }
    if (!SDL_InitSubSystem(SDL_INIT_AUDIO))
    {
        return false;
    }
    // A small device buffer: about 5 ms at 48 kHz, if the device allows.
    SDL_SetHint(SDL_HINT_AUDIO_DEVICE_SAMPLE_FRAMES, "256");
    Mixer* made = createMixer();
    const SDL_AudioSpec spec{SDL_AUDIO_F32, 2, static_cast<int>(mixRate)};
    stream = SDL_OpenAudioDeviceStream(SDL_AUDIO_DEVICE_DEFAULT_PLAYBACK, &spec, deviceCallback, made);
    if (stream == nullptr)
    {
        destroyMixer(made);
        SDL_QuitSubSystem(SDL_INIT_AUDIO);
        return false;
    }
    mixer = made;
    setDelay(*mixer, pendingDelay);
    mode = Mode::Device;
    sourceStates.clear();
    sourceWorld = nullptr;
    installAudio(mixerService);
    SDL_AudioSpec device{};
    int deviceFrames = 0;
    if (SDL_GetAudioDeviceFormat(SDL_GetAudioStreamDevice(stream), &device, &deviceFrames) && device.freq > 0)
    {
        // The device's buffer, and the one SDL's stream keeps ahead of it.
        setLatencyEstimate(*mixer, 2.0f * static_cast<float>(deviceFrames) / static_cast<float>(device.freq));
    }
    SDL_ResumeAudioStreamDevice(stream);
    return true;
}

void startOfflineMixing(uint64_t startNs)
{
    stopMixing();
    start(Mode::Offline);
    offlineStart = startNs;
    offlineFrames = 0;
}

void mixOffline(std::span<float> into)
{
    if (mode != Mode::Offline)
    {
        std::fill(into.begin(), into.end(), 0.0f);
        return;
    }
    const uint32_t frames = static_cast<uint32_t>(into.size() / 2);
    const uint64_t now = offlineStart + static_cast<uint64_t>(std::llround(offlineFrames * 1e9 / mixRate));
    mix(*mixer, into.data(), frames, now);
    offlineFrames += frames;
    pump();
}

void stopMixing()
{
    if (mode == Mode::Off)
    {
        return;
    }
    if (mode == Mode::Device)
    {
        SDL_DestroyAudioStream(stream); // stops the callback
        stream = nullptr;
        SDL_QuitSubSystem(SDL_INIT_AUDIO);
    }
    destroyMixer(mixer);
    mixer = nullptr;
    // Nothing plays any sound now.
    for (Sound* sound : forgetting)
    {
        destroy(tag, sound);
    }
    forgetting.clear();
    sourceStates.clear();
    sourceWorld = nullptr;
    mode = Mode::Off;
    installAudio(nullAudioService());
}

void setScheduleDelay(float seconds)
{
    pendingDelay = std::max(seconds, 0.0f);
    if (mixer != nullptr)
    {
        setDelay(*mixer, pendingDelay);
    }
}

float scheduleDelay()
{
    return pendingDelay;
}

MixerStats mixerStats()
{
    return mixer != nullptr ? stats(*mixer) : MixerStats{};
}

assets::AssetId createSound(assets::SoundInfo info, std::span<const int16_t> samples)
{
    if (!valid(info, samples.size()))
    {
        return {};
    }
    Sound* sound = create<Sound>(tag);
    sound->info = info;
    sound->samples.assign(samples.subspan(0, size_t{info.frameCount} * info.channels));
    // An ID no imported asset has: "crsound" and a count.
    assets::AssetId id{};
    std::memcpy(id.bytes, "crsound", 7);
    const uint32_t count = ++createdSounds;
    std::memcpy(id.bytes + 8, &count, sizeof(count));
    sounds.insertOrAssign(id, sound);
    return id;
}

void destroySound(assets::AssetId id)
{
    Sound** found = sounds.find(id);
    if (found == nullptr)
    {
        return;
    }
    Sound* sound = *found;
    if (sound != nullptr && mixer != nullptr)
    {
        Command forget{.kind = CommandKind::Forget, .sound = sound};
        if (!send(*mixer, forget))
        {
            return; // the queue is full: try again later
        }
        forgetting.pushBack(sound);
    }
    else if (sound != nullptr)
    {
        destroy(tag, sound);
    }
    sounds.erase(id);
}

} // namespace crucible::audio
