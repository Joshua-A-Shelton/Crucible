// Crucible's mixer, the main thread's side (ADR 0082): the SDL device whose
// callback mixes, offline mixing, the sounds the voices play, the audio
// service, and "Audio.Update" for AudioSources and the listener. Commands go
// to the audio thread through the mixer's queue; what comes back (voices
// ended, sounds no longer played) is read here.

#include "Mixer.h"

#include <crucible/Adpcm.h>
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

// Whether a sound's info and data agree, so the mixer needn't check.
bool valid(const Sound& sound)
{
    const assets::SoundInfo& info = sound.info;
    if (info.sampleRate == 0 || (info.channels != 1 && info.channels != 2))
    {
        return false;
    }
    switch (info.encoding)
    {
    case assets::SoundEncoding::Pcm16:
        return uint64_t{info.frameCount} * info.channels <= sound.samples.size();
    case assets::SoundEncoding::ImaAdpcm:
    {
        if (info.blockFrames < 2 || info.blockFrames % 2 != 0 || info.blockFrames > assets::maxAdpcmBlockFrames)
        {
            return false;
        }
        const uint64_t blocks = (uint64_t{info.frameCount} + info.blockFrames - 1) / info.blockFrames;
        return blocks * adpcm::blockBytes(info.blockFrames, info.channels) <= sound.data.size();
    }
    case assets::SoundEncoding::Vorbis:
    {
        VorbisDecoder* decoder = openVorbis(sound.data.span());
        const bool agrees = decoder != nullptr && vorbisInfo(*decoder).channels == info.channels &&
                            vorbisInfo(*decoder).sampleRate == info.sampleRate && info.frameCount > 0;
        closeVorbis(decoder);
        return agrees;
    }
    }
    return false;
}

template <class T>
bool readSection(const binary::File& file, uint32_t tagOf, Vector<T>& out)
{
    const int32_t index = binary::findSection(file, tagOf);
    if (index < 0)
    {
        return false;
    }
    out.resize(static_cast<size_t>(binary::sectionInfo(file, static_cast<uint32_t>(index)).size) / sizeof(T));
    return static_cast<bool>(binary::readSection(
        file, static_cast<uint32_t>(index), std::span(reinterpret_cast<uint8_t*>(out.data()), out.size() * sizeof(T))));
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
    Vector<assets::SoundInfo> info(tag);
    bool read = readSection(*file, binary::tag("INFO"), info) && info.size() == 1;
    if (read)
    {
        sound->info = info[0];
        switch (sound->info.encoding)
        {
        case assets::SoundEncoding::Pcm16:
            read = readSection(*file, binary::tag("PCMS"), sound->samples);
            break;
        case assets::SoundEncoding::ImaAdpcm:
            read = readSection(*file, binary::tag("ADPC"), sound->data);
            break;
        case assets::SoundEncoding::Vorbis:
            read = readSection(*file, binary::tag("OGGV"), sound->data);
            break;
        default:
            read = false;
        }
    }
    binary::closeFile(file);
    if (!read || !valid(*sound))
    {
        destroy(tag, sound);
        return nullptr;
    }
    return sound;
}

// --- Streams (ADR 0083) -----------------------------------------------------

Vector<Stream*> streams{tag};

// The job: decode into the ring as far as the voice has let go of it.
void fill(void* data)
{
    Stream& stream = *static_cast<Stream*>(data);
    const uint32_t channels = stream.channels;
    const uint32_t piece = static_cast<uint32_t>(stream.scratch.size() / channels);
    while (!stream.finished.load(std::memory_order_relaxed))
    {
        const uint64_t written = stream.written.load(std::memory_order_relaxed);
        const uint64_t room = stream.capacity - (written - stream.released.load(std::memory_order_acquire));
        if (room == 0)
        {
            return;
        }
        uint32_t frames = decodeVorbis(
            *stream.decoder,
            stream.scratch.span().subspan(0, static_cast<size_t>(std::min<uint64_t>(room, piece)) * channels));
        if (frames == 0)
        {
            if (!stream.loop || !rewindVorbis(*stream.decoder))
            {
                stream.finished.store(true, std::memory_order_release);
                return;
            }
            continue;
        }
        for (uint32_t f = 0; f < frames; ++f)
        {
            const size_t at = static_cast<size_t>((written + f) % stream.capacity) * channels;
            for (uint32_t c = 0; c < channels; ++c)
            {
                stream.ring[at + c] = stream.scratch[size_t{f} * channels + c];
            }
        }
        stream.written.store(written + frames, std::memory_order_release);
    }
}

// A Vorbis sound's stream for a new voice, its first half second decoded
// here so the voice can start at once. Null for other encodings.
Stream* streamFor(const Sound* sound, bool loop)
{
    if (sound == nullptr || sound->info.encoding != assets::SoundEncoding::Vorbis)
    {
        return nullptr;
    }
    Stream* stream = create<Stream>(tag);
    stream->sound = sound;
    stream->decoder = openVorbis(sound->data.span());
    stream->loop = loop;
    stream->channels = sound->info.channels;
    stream->capacity = sound->info.sampleRate * streamSeconds;
    stream->ring.resize(size_t{stream->capacity} * stream->channels);
    stream->scratch.resize(size_t{4096} * stream->channels);
    if (stream->decoder == nullptr)
    {
        stream->finished = true;
    }
    else
    {
        // Prime half the ring: the job's work, run here once.
        stream->released = 0;
        const uint32_t capacity = stream->capacity;
        stream->capacity = capacity / 2;
        fill(stream);
        stream->capacity = capacity;
    }
    streams.pushBack(stream);
    return stream;
}

void freeStream(Stream* stream)
{
    jobs::wait(stream->job);
    closeVorbis(stream->decoder);
    for (size_t i = 0; i < streams.size(); ++i)
    {
        if (streams[i] == stream)
        {
            streams.removeSwap(i);
            break;
        }
    }
    destroy(tag, stream);
}

// Streams less than half full get a job, on the background priority.
void topUpStreams()
{
    for (Stream* stream : streams)
    {
        if (stream->finished.load(std::memory_order_acquire) || !jobs::isDone(stream->job))
        {
            continue;
        }
        const uint64_t ahead = stream->written.load() - stream->released.load();
        if (ahead < stream->capacity / 2)
        {
            const jobs::Job job{fill, stream};
            jobs::run(std::span(&job, 1), jobs::JobPriority::Background, stream->job);
        }
    }
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

// What the audio thread said: voices that ended (their streams freed),
// sounds it let go of; then the streams topped up.
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
            if (feedback.stream != nullptr)
            {
                freeStream(feedback.stream);
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
    topUpStreams();
}

// Sends a play; a voice that can't be queued gives its stream back.
bool sendPlay(Command& command)
{
    if (send(*mixer, command))
    {
        return true;
    }
    if (command.stream != nullptr)
    {
        freeStream(command.stream);
    }
    return false;
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
    Command command{.kind = CommandKind::Play,
                    .voice = nextVoice++,
                    .sound = sound,
                    .stream = streamFor(sound, false),
                    .atNs = atNs};
    command.volume = volume;
    command.bus = bus;
    command.spatial = true;
    command.position = position;
    return sendPlay(command) ? command.voice : 0;
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
    pump();
    const Sound* sound = soundOf(track);
    Command command{.kind = CommandKind::Music, .voice = nextVoice++, .sound = sound, .stream = streamFor(sound, true)};
    command.transition = transition;
    (void)sendPlay(command);
}

void mixerStopAll(float fadeSeconds)
{
    Command command{.kind = CommandKind::StopAll};
    command.seconds = fadeSeconds;
    (void)send(*mixer, command);
    sourceStates.clear();
}

void mixerPauseAll(bool paused)
{
    Command command{.kind = CommandKind::Pause};
    command.paused = paused;
    (void)send(*mixer, command);
}

void mixerAdvancePaused(float seconds)
{
    Command command{.kind = CommandKind::Advance};
    command.seconds = seconds;
    (void)send(*mixer, command);
}

constexpr AudioService mixerService{
    mixerPlayOneShot,     mixerStop,      mixerBusVolume, mixerBusEffects, mixerDefineSnapshot,
    mixerBlendToSnapshot, mixerPlayMusic, mixerStopAll,   mixerPauseAll,   mixerAdvancePaused};

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
                    Command play{.kind = CommandKind::Play,
                                 .voice = nextVoice++,
                                 .sound = sound,
                                 .stream = streamFor(sound, source.loop)};
                    play.volume = source.volume;
                    play.pitch = source.pitch;
                    play.bus = source.bus;
                    play.loop = source.loop;
                    play.spatial = source.spatial;
                    play.position = position;
                    play.minDistance = source.minDistance;
                    play.maxDistance = source.maxDistance;
                    play.rolloff = source.rolloff;
                    state->voice = sendPlay(play) ? play.voice : 0;
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
    while (!streams.empty())
    {
        freeStream(streams.back());
    }
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
    if (info.encoding != assets::SoundEncoding::Pcm16 || info.channels < 1 || info.channels > 2 ||
        uint64_t{info.frameCount} * info.channels > samples.size())
    {
        return {};
    }
    Sound* sound = create<Sound>(tag);
    sound->info = info;
    sound->info.blockFrames = 0;
    sound->samples.assign(samples.subspan(0, size_t{info.frameCount} * info.channels));
    if (!valid(*sound))
    {
        destroy(tag, sound);
        return {};
    }
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
