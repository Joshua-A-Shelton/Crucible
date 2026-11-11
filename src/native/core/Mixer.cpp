// Crucible's mixer, the audio thread's side (ADR 0082). Everything here runs
// in mix(): no locks, no allocations, nothing that can wait. Each call
// drains the command queue, then mixes in blocks of up to blockFrames:
//
//   voices   resampled (cubic Hermite), panned, attenuated, into their bus
//   buses    low-pass, ducking, volume; into the master, and by their
//            reverb send into the shared reverb
//   reverb   Freeverb's combs and all-passes, into the reverb bus
//   master   its own low-pass and volume, clamped to [-1, 1]
//
// Gains move linearly across a block rather than jumping at its start, so
// changes don't click ("zipper noise"); fades move sample by sample.

#include "Mixer.h"

#include <crucible/Adpcm.h>

#include <algorithm>
#include <cmath>

namespace crucible::audio {

namespace {

constexpr uint32_t blockFrames = 512;
constexpr float pi = 3.14159265358979f;
// A ducking bus at this level or above lowers the ducked bus by its whole
// duckAmount (about -12 dBFS); quieter, proportionally less.
constexpr float duckLevel = 0.25f;
constexpr float duckRelease = 0.3f; // seconds for the ducking bus's level to fall back

// --- Freeverb (Jezar's public-domain design): eight parallel low-passed
// comb filters (the room's echoes) into four series all-passes (diffusion),
// per side, the right side's delays a little longer for width. Its delay
// lengths are for 44.1 kHz; they're scaled to the mix rate.

constexpr uint32_t combLengths[8] = {1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617};
constexpr uint32_t allPassLengths[4] = {556, 441, 341, 225};
constexpr uint32_t stereoSpread = 23;
constexpr uint32_t maxComb = 1800;
constexpr uint32_t maxAllPass = 640;
constexpr float roomFeedback = 0.84f;
constexpr float damping = 0.2f;
constexpr float reverbInputGain = 0.015f;

uint32_t scaled(uint32_t length)
{
    return length * mixRate / 44100;
}

struct Comb
{
    float buffer[maxComb]{};
    uint32_t size = 1;
    uint32_t index = 0;
    float store = 0;

    float process(float in)
    {
        const float out = buffer[index];
        store = out * (1 - damping) + store * damping;
        buffer[index] = in + store * roomFeedback;
        index = index + 1 == size ? 0 : index + 1;
        return out;
    }
};

struct AllPass
{
    float buffer[maxAllPass]{};
    uint32_t size = 1;
    uint32_t index = 0;

    float process(float in)
    {
        const float delayed = buffer[index];
        buffer[index] = in + delayed * 0.5f;
        index = index + 1 == size ? 0 : index + 1;
        return delayed - in;
    }
};

struct Reverb
{
    Comb combs[2][8];
    AllPass allPasses[2][4];

    Reverb()
    {
        for (uint32_t side = 0; side < 2; ++side)
        {
            for (uint32_t c = 0; c < 8; ++c)
            {
                combs[side][c].size = scaled(combLengths[c] + side * stereoSpread);
            }
            for (uint32_t a = 0; a < 4; ++a)
            {
                allPasses[side][a].size = scaled(allPassLengths[a] + side * stereoSpread);
            }
        }
    }

    // Mono in, stereo out (added to `out`).
    void process(const float* in, float* out, uint32_t frames)
    {
        for (uint32_t i = 0; i < frames; ++i)
        {
            const float input = in[i] * reverbInputGain;
            for (uint32_t side = 0; side < 2; ++side)
            {
                float sum = 0;
                for (Comb& comb : combs[side])
                {
                    sum += comb.process(input);
                }
                for (AllPass& allPass : allPasses[side])
                {
                    sum = allPass.process(sum);
                }
                out[i * 2 + side] += sum;
            }
        }
    }
};

struct Voice
{
    bool active = false;
    bool fresh = true; // no gains yet: the first block starts at its own
    bool music = false;
    uint32_t id = 0;
    const Sound* sound = nullptr;
    Stream* stream = nullptr; // a Vorbis sound's decoded frames
    bool starved = false;     // this block read past what was decoded
    double position = 0;      // in the sound's frames (a stream's: from its start, laps included)
    uint64_t start = 0;       // the mix frame it starts at
    float volume = 1;
    float pitch = 1;
    uint32_t bus = masterBus;
    bool loop = false;
    bool spatial = false;
    Vector3 place{};
    float minDistance = 1;
    float maxDistance = 50;
    Rolloff rolloff = Rolloff::Inverse;
    // A fade (a stop, a music crossfade): toward fadeTarget, by fadePerFrame,
    // from fadeFrom on.
    float fade = 1;
    float fadeTarget = 1;
    float fadePerFrame = 0;
    uint64_t fadeFrom = 0;
    float gainLeft = 0;
    float gainRight = 0;
};

struct Bus
{
    float volume = 1;
    float volumeTarget = 1;
    float volumePerFrame = 0;
    // Effects glide from `from` to `to` (t from 0 to 1).
    BusEffects from{0, 0, noBus, 0};
    BusEffects to{0, 0, noBus, 0};
    float effectsT = 1;
    float effectsPerFrame = 0;
    float lowLeft = 0;
    float lowRight = 0;
    float duck = 1;     // the gain ducking applies now
    float envelope = 0; // the bus's output level, for buses it ducks
    float buffer[blockFrames * 2]{};

    BusEffects effects() const
    {
        BusEffects now = to;
        now.lowPassHz = from.lowPassHz + (to.lowPassHz - from.lowPassHz) * effectsT;
        // Gliding from or to "off" (0) glides from or to the top of hearing.
        if ((from.lowPassHz == 0) != (to.lowPassHz == 0))
        {
            const float a = from.lowPassHz == 0 ? 20000.0f : from.lowPassHz;
            const float b = to.lowPassHz == 0 ? 20000.0f : to.lowPassHz;
            now.lowPassHz = effectsT >= 1 ? to.lowPassHz : a + (b - a) * effectsT;
        }
        now.reverbSend = from.reverbSend + (to.reverbSend - from.reverbSend) * effectsT;
        now.duckAmount = from.duckAmount + (to.duckAmount - from.duckAmount) * effectsT;
        return now;
    }
};

} // namespace

class Mixer
{
public:
    RingQueue<Command, 4096> commands;
    RingQueue<Feedback, 1024> feedback;
    Voice voices[maxVoices];
    Bus buses[maxBuses];
    Reverb reverb;
    float reverbInput[blockFrames]{};
    Vector3 listener{};
    Vector3 forward{0, 0, 1};
    Vector3 up{0, 1, 0};
    uint64_t cursor = 0; // the next frame to mix
    double cursorNs = 0; // when it's mixed, on the input clock (smoothed)
    uint32_t lastFrames = 0;
    bool anchored = false;
    int32_t music = -1; // the voice playing the current track
    // Each voice's last two decoded ADPCM blocks (the four samples cubic
    // Hermite reads span at most two), by block number.
    int64_t cachedBlock[maxVoices][2];
    int16_t cache[maxVoices][2][assets::maxAdpcmBlockFrames * 2];
    std::atomic<float> delaySeconds{defaultScheduleDelay};
    std::atomic<uint32_t> playing{0};
    std::atomic<uint32_t> lateStarts{0};
    std::atomic<uint32_t> underruns{0};
    std::atomic<uint32_t> dropped{0};
    std::atomic<uint32_t> starved{0};
    std::atomic<float> latency{0};
};

namespace {

// --- Commands ---------------------------------------------------------------

void end(Mixer& mixer, Voice& voice)
{
    voice.active = false;
    (void)mixer.feedback.push({FeedbackKind::VoiceEnded, voice.id, voice.sound, voice.stream});
    if (mixer.music >= 0 && &mixer.voices[mixer.music] == &voice)
    {
        mixer.music = -1;
    }
}

Voice* find(Mixer& mixer, uint32_t id)
{
    for (Voice& voice : mixer.voices)
    {
        if (voice.active && voice.id == id)
        {
            return &voice;
        }
    }
    return nullptr;
}

Voice* freeVoice(Mixer& mixer)
{
    for (Voice& voice : mixer.voices)
    {
        if (!voice.active)
        {
            const size_t index = static_cast<size_t>(&voice - mixer.voices);
            mixer.cachedBlock[index][0] = mixer.cachedBlock[index][1] = -1;
            return &voice;
        }
    }
    return nullptr;
}

// A voice that couldn't start: its stream goes back to be freed.
void refuse(Mixer& mixer, const Command& command)
{
    if (command.stream != nullptr)
    {
        (void)mixer.feedback.push({FeedbackKind::VoiceEnded, command.voice, command.sound, command.stream});
    }
}

// The mix frame a moment on the input clock falls on.
int64_t frameAt(const Mixer& mixer, uint64_t ns)
{
    return static_cast<int64_t>(mixer.cursor) +
           std::llround((static_cast<double>(ns) - mixer.cursorNs) * mixRate / 1e9);
}

// When a scheduled sound starts: its time plus the delay, or now if that's
// passed (counted, so a check can catch it).
uint64_t scheduled(Mixer& mixer, uint64_t atNs)
{
    if (atNs == 0)
    {
        return mixer.cursor;
    }
    const uint64_t delayNs = static_cast<uint64_t>(mixer.delaySeconds.load() * 1e9);
    const int64_t frame = frameAt(mixer, atNs + delayNs);
    if (frame < static_cast<int64_t>(mixer.cursor))
    {
        ++mixer.lateStarts;
        return mixer.cursor;
    }
    return static_cast<uint64_t>(frame);
}

void fadeOut(Voice& voice, float seconds, uint64_t from)
{
    voice.fadeTarget = 0;
    voice.fadeFrom = from;
    voice.fadePerFrame = seconds > 0 ? std::max(voice.fade, 1e-6f) / (seconds * mixRate) : 1.0f;
}

uint32_t busIndex(uint32_t bus)
{
    return bus < maxBuses ? bus : masterBus;
}

void playMusic(Mixer& mixer, const Command& command)
{
    uint64_t at = mixer.cursor;
    const float crossfade = std::max(command.transition.crossfadeSeconds, 0.0f);
    if (mixer.music >= 0)
    {
        Voice& current = mixer.voices[mixer.music];
        const assets::SoundInfo& info = current.sound->info;
        if (command.transition.align != MusicAlign::Now && info.beatsPerMinute > 0)
        {
            // The playing track's next beat (or bar) from its start.
            double unit = 60.0 / info.beatsPerMinute * mixRate;
            if (command.transition.align == MusicAlign::Bar)
            {
                unit *= std::max(info.beatsPerBar, 1u);
            }
            const double elapsed = mixer.cursor > current.start ? static_cast<double>(mixer.cursor - current.start) : 0;
            at = current.start + static_cast<uint64_t>(std::llround(std::ceil(elapsed / unit - 1e-9) * unit));
        }
        fadeOut(current, crossfade, at);
        mixer.music = -1;
    }
    Voice* voice = command.sound != nullptr ? freeVoice(mixer) : nullptr;
    if (voice == nullptr)
    {
        refuse(mixer, command);
        return;
    }
    *voice = Voice{};
    voice->active = true;
    voice->music = true;
    voice->id = command.voice;
    voice->sound = command.sound;
    voice->stream = command.stream;
    voice->start = at;
    voice->volume = command.volume;
    voice->bus = musicBus;
    voice->loop = true;
    voice->fade = crossfade > 0 ? 0.0f : 1.0f;
    voice->fadeTarget = 1;
    voice->fadePerFrame = crossfade > 0 ? 1.0f / (crossfade * mixRate) : 0.0f;
    voice->fadeFrom = at;
    mixer.music = static_cast<int32_t>(voice - mixer.voices);
}

void apply(Mixer& mixer, const Command& command)
{
    switch (command.kind)
    {
    case CommandKind::Play:
    {
        Voice* voice = command.sound != nullptr ? freeVoice(mixer) : nullptr;
        if (voice == nullptr)
        {
            refuse(mixer, command);
            return;
        }
        *voice = Voice{};
        voice->active = true;
        voice->id = command.voice;
        voice->sound = command.sound;
        voice->stream = command.stream;
        voice->start = scheduled(mixer, command.atNs);
        voice->volume = command.volume;
        voice->pitch = command.pitch;
        voice->bus = busIndex(command.bus);
        voice->loop = command.loop;
        voice->spatial = command.spatial;
        voice->place = command.position;
        voice->minDistance = command.minDistance;
        voice->maxDistance = command.maxDistance;
        voice->rolloff = command.rolloff;
        return;
    }
    case CommandKind::Stop:
        if (Voice* voice = find(mixer, command.voice))
        {
            if (command.seconds <= 0)
            {
                end(mixer, *voice);
            }
            else
            {
                fadeOut(*voice, command.seconds, mixer.cursor);
            }
        }
        return;
    case CommandKind::Voice:
        if (Voice* voice = find(mixer, command.voice))
        {
            voice->volume = command.volume;
            voice->pitch = command.pitch;
            voice->place = command.position;
        }
        return;
    case CommandKind::Listener:
        mixer.listener = command.position;
        mixer.forward = command.forward;
        mixer.up = command.up;
        return;
    case CommandKind::BusVolume:
    {
        if (command.bus >= maxBuses)
        {
            return;
        }
        Bus& bus = mixer.buses[command.bus];
        bus.volumeTarget = std::max(command.volume, 0.0f);
        bus.volumePerFrame =
            command.seconds > 0 ? std::fabs(bus.volumeTarget - bus.volume) / (command.seconds * mixRate) : 0.0f;
        if (bus.volumePerFrame <= 0)
        {
            bus.volume = bus.volumeTarget;
        }
        return;
    }
    case CommandKind::BusEffects:
    {
        if (command.bus >= maxBuses)
        {
            return;
        }
        Bus& bus = mixer.buses[command.bus];
        bus.from = bus.effects();
        bus.to = command.effects;
        bus.from.duckedBy = command.effects.duckedBy;
        bus.effectsT = command.seconds > 0 ? 0.0f : 1.0f;
        bus.effectsPerFrame = command.seconds > 0 ? 1.0f / (command.seconds * mixRate) : 0.0f;
        return;
    }
    case CommandKind::Music:
        playMusic(mixer, command);
        return;
    case CommandKind::StopAll:
        for (Voice& voice : mixer.voices)
        {
            if (voice.active)
            {
                if (command.seconds <= 0)
                {
                    end(mixer, voice);
                }
                else
                {
                    fadeOut(voice, command.seconds, mixer.cursor);
                }
            }
        }
        mixer.music = -1;
        return;
    case CommandKind::Forget:
        for (Voice& voice : mixer.voices)
        {
            if (voice.active && voice.sound == command.sound)
            {
                end(mixer, voice);
            }
        }
        (void)mixer.feedback.push({FeedbackKind::Forgotten, 0, command.sound, nullptr});
        return;
    }
}

// --- Voices -----------------------------------------------------------------

// A voice's sample at a frame of its sound (silence outside it, unless it
// loops), whatever its encoding.
float sampleOf(Mixer& mixer, Voice& voice, uint32_t channel, int64_t frame)
{
    const Sound& sound = *voice.sound;
    const uint32_t channels = sound.info.channels;
    if (voice.stream != nullptr)
    {
        // A stream counts frames from the voice's start, laps included.
        const Stream& stream = *voice.stream;
        if (frame < 0)
        {
            return 0;
        }
        if (static_cast<uint64_t>(frame) >= stream.written.load(std::memory_order_acquire))
        {
            voice.starved = !stream.finished.load(std::memory_order_acquire);
            return 0;
        }
        const size_t at = static_cast<size_t>(static_cast<uint64_t>(frame) % stream.capacity) * channels + channel;
        return stream.ring[at] * (1.0f / 32768.0f);
    }
    const int64_t count = sound.info.frameCount;
    if (frame < 0 || frame >= count)
    {
        if (!voice.loop || count == 0)
        {
            return 0;
        }
        frame %= count;
        frame = frame < 0 ? frame + count : frame;
    }
    if (sound.info.encoding == assets::SoundEncoding::ImaAdpcm)
    {
        const uint32_t blockFrames = sound.info.blockFrames;
        const int64_t block = frame / blockFrames;
        const size_t index = static_cast<size_t>(&voice - mixer.voices);
        int16_t* decoded = mixer.cache[index][block & 1];
        if (mixer.cachedBlock[index][block & 1] != block)
        {
            const size_t bytes = adpcm::blockBytes(blockFrames, channels);
            adpcm::decodeBlock(sound.data.data() + static_cast<size_t>(block) * bytes, channels, blockFrames, decoded);
            mixer.cachedBlock[index][block & 1] = block;
        }
        return decoded[static_cast<size_t>(frame % blockFrames) * channels + channel] * (1.0f / 32768.0f);
    }
    return sound.samples[static_cast<size_t>(frame) * channels + channel] * (1.0f / 32768.0f);
}

// Cubic Hermite (Catmull-Rom) through four neighbouring samples: smooth, and
// much cleaner than a straight line when the pitch moves far from 1.
float hermite(float before, float at, float next, float after, float t)
{
    const float c = (next - before) * 0.5f;
    const float v = at - next;
    const float w = c + v;
    const float a = w + v + (after - at) * 0.5f;
    const float b = w + a;
    return ((a * t - b) * t + c) * t + at;
}

float sampleAt(Mixer& mixer, Voice& voice, uint32_t channel)
{
    const double whole = std::floor(voice.position);
    const int64_t frame = static_cast<int64_t>(whole);
    return hermite(sampleOf(mixer, voice, channel, frame - 1), sampleOf(mixer, voice, channel, frame),
                   sampleOf(mixer, voice, channel, frame + 1), sampleOf(mixer, voice, channel, frame + 2),
                   static_cast<float>(voice.position - whole));
}

// Distance attenuation by the voice's rolloff.
float attenuation(const Voice& voice, float distance)
{
    const float near = std::max(voice.minDistance, 1e-4f);
    const float far = std::max(voice.maxDistance, near);
    switch (voice.rolloff)
    {
    case Rolloff::Linear:
        return distance <= near ? 1.0f : distance >= far ? 0.0f : 1.0f - (distance - near) / (far - near);
    case Rolloff::Inverse:
        return distance <= near ? 1.0f : distance >= far ? 0.0f : near / distance;
    case Rolloff::None:
        return 1.0f;
    }
    return 1.0f;
}

// The voice's gains for each side, before fading: spatial ones by distance
// and constant-power panning (cos and sin of the same angle, so the power
// stays the same as a sound passes the middle).
void gains(const Mixer& mixer, const Voice& voice, float& left, float& right)
{
    if (!voice.spatial)
    {
        // A mono sound in the middle at constant power; stereo as it is.
        const float side = voice.sound->info.channels == 1 ? 0.70710678f : 1.0f;
        left = right = voice.volume * side;
        return;
    }
    const Vector3 offset = voice.place - mixer.listener;
    const float distance = length(offset);
    float pan = 0;
    if (distance > 1e-4f)
    {
        const Vector3 rightward = normalize(cross(mixer.up, mixer.forward));
        pan = std::clamp(dot(offset * (1.0f / distance), rightward), -1.0f, 1.0f);
    }
    const float angle = (pan + 1) * pi * 0.25f;
    const float level = voice.volume * attenuation(voice, distance);
    left = level * std::cos(angle);
    right = level * std::sin(angle);
}

// One block of a voice into its bus. Its gains (volume, distance, pan) ramp
// across the block; its fade (a stop, a music crossfade) moves sample by
// sample from the frame it starts, so a cut on a bar lands on the bar.
void render(Mixer& mixer, Voice& voice, uint32_t frames)
{
    const uint64_t blockEnd = mixer.cursor + frames;
    if (voice.start >= blockEnd)
    {
        return;
    }
    const uint32_t first = voice.start > mixer.cursor ? static_cast<uint32_t>(voice.start - mixer.cursor) : 0;
    const Sound& sound = *voice.sound;
    float left, right;
    gains(mixer, voice, left, right);
    if (voice.fresh)
    {
        // A new voice starts at its own gain, not ramping up from silence,
        // so its onset is as sharp as its sound's.
        voice.gainLeft = left;
        voice.gainRight = right;
        voice.fresh = false;
    }
    const float startLeft = voice.gainLeft, startRight = voice.gainRight;
    const float span = static_cast<float>(frames - first);
    const double step = voice.pitch * static_cast<double>(sound.info.sampleRate) / mixRate;
    float* out = mixer.buses[voice.bus].buffer;
    const bool stereo = sound.info.channels == 2;
    bool ended = false;
    for (uint32_t i = first; i < frames; ++i)
    {
        if (!voice.loop && voice.position >= sound.info.frameCount)
        {
            ended = true;
            break;
        }
        if (voice.fade != voice.fadeTarget && mixer.cursor + i >= voice.fadeFrom)
        {
            voice.fade = voice.fade < voice.fadeTarget ? std::min(voice.fade + voice.fadePerFrame, voice.fadeTarget)
                                                       : std::max(voice.fade - voice.fadePerFrame, voice.fadeTarget);
            if (voice.fade <= 0 && voice.fadeTarget <= 0)
            {
                ended = true;
                break;
            }
        }
        const float t = static_cast<float>(i - first + 1) / span;
        const float gl = (startLeft + (left - startLeft) * t) * voice.fade;
        const float gr = (startRight + (right - startRight) * t) * voice.fade;
        const float a = sampleAt(mixer, voice, 0);
        const float b = stereo ? sampleAt(mixer, voice, 1) : a;
        if (voice.spatial && stereo)
        {
            const float middle = (a + b) * 0.5f;
            out[i * 2] += middle * gl;
            out[i * 2 + 1] += middle * gr;
        }
        else
        {
            out[i * 2] += a * gl;
            out[i * 2 + 1] += b * gr;
        }
        voice.position += step;
        if (voice.loop && voice.stream == nullptr && voice.position >= sound.info.frameCount)
        {
            voice.position = std::fmod(voice.position, static_cast<double>(sound.info.frameCount));
        }
    }
    voice.gainLeft = left;
    voice.gainRight = right;
    if (voice.stream != nullptr)
    {
        // Frames behind the voice (but the one Hermite reads before it) are free.
        const double behind = std::floor(voice.position) - 2;
        if (behind > static_cast<double>(voice.stream->released.load(std::memory_order_relaxed)))
        {
            voice.stream->released.store(static_cast<uint64_t>(behind), std::memory_order_release);
        }
        if (voice.starved)
        {
            ++mixer.starved;
            voice.starved = false;
        }
    }
    if (ended)
    {
        end(mixer, voice);
    }
}

// --- Buses ------------------------------------------------------------------

float duckingFor(const Mixer& mixer, const BusEffects& effects)
{
    if (effects.duckedBy >= maxBuses || effects.duckAmount <= 0)
    {
        return 1.0f;
    }
    const float level = std::min(mixer.buses[effects.duckedBy].envelope / duckLevel, 1.0f);
    return 1.0f - std::clamp(effects.duckAmount, 0.0f, 1.0f) * level;
}

void process(Mixer& mixer, uint32_t index, uint32_t frames, float* out)
{
    Bus& bus = mixer.buses[index];
    // Effects and volume at the block's end; gains ramp to them across it.
    if (bus.effectsT < 1)
    {
        bus.effectsT = std::min(bus.effectsT + bus.effectsPerFrame * static_cast<float>(frames), 1.0f);
    }
    const BusEffects effects = bus.effects();
    const float startVolume = bus.volume;
    if (bus.volume != bus.volumeTarget)
    {
        const float step = bus.volumePerFrame * static_cast<float>(frames);
        bus.volume = bus.volume < bus.volumeTarget ? std::min(bus.volume + step, bus.volumeTarget)
                                                   : std::max(bus.volume - step, bus.volumeTarget);
    }
    const float startDuck = bus.duck;
    bus.duck = duckingFor(mixer, effects);
    const bool filtered = effects.lowPassHz > 0 && effects.lowPassHz < 20000;
    // A one-pole low-pass: each sample moves a share of the way toward the input.
    const float share = filtered ? 1.0f - std::exp(-2.0f * pi * effects.lowPassHz / mixRate) : 1.0f;
    const float send = index == reverbBus || index == masterBus ? 0.0f : std::clamp(effects.reverbSend, 0.0f, 1.0f);
    float peak = 0;
    for (uint32_t i = 0; i < frames; ++i)
    {
        float l = bus.buffer[i * 2], r = bus.buffer[i * 2 + 1];
        if (filtered)
        {
            bus.lowLeft += share * (l - bus.lowLeft);
            bus.lowRight += share * (r - bus.lowRight);
            l = bus.lowLeft;
            r = bus.lowRight;
        }
        const float t = static_cast<float>(i + 1) / static_cast<float>(frames);
        const float gain = (startVolume + (bus.volume - startVolume) * t) * (startDuck + (bus.duck - startDuck) * t);
        l *= gain;
        r *= gain;
        peak = std::max(peak, std::max(std::fabs(l), std::fabs(r)));
        if (index == masterBus)
        {
            out[i * 2] = std::clamp(l, -1.0f, 1.0f);
            out[i * 2 + 1] = std::clamp(r, -1.0f, 1.0f);
            continue;
        }
        mixer.buses[masterBus].buffer[i * 2] += l;
        mixer.buses[masterBus].buffer[i * 2 + 1] += r;
        if (send > 0)
        {
            mixer.reverbInput[i] += (l + r) * 0.5f * send;
        }
    }
    const float release = std::exp(-static_cast<float>(frames) / (duckRelease * mixRate));
    bus.envelope = peak > bus.envelope ? peak : std::max(peak, bus.envelope * release);
}

void mixBlock(Mixer& mixer, float* out, uint32_t frames)
{
    for (Bus& bus : mixer.buses)
    {
        std::fill_n(bus.buffer, frames * 2, 0.0f);
    }
    std::fill_n(mixer.reverbInput, frames, 0.0f);
    uint32_t playing = 0;
    for (Voice& voice : mixer.voices)
    {
        if (voice.active)
        {
            render(mixer, voice, frames);
            playing += voice.active ? 1 : 0;
        }
    }
    for (uint32_t b = 1; b < reverbBus; ++b)
    {
        process(mixer, b, frames, out);
    }
    mixer.reverb.process(mixer.reverbInput, mixer.buses[reverbBus].buffer, frames);
    process(mixer, reverbBus, frames, out);
    process(mixer, masterBus, frames, out);
    mixer.playing = playing;
}

} // namespace

Mixer* createMixer()
{
    return create<Mixer>(MemoryTag::Audio);
}

void destroyMixer(Mixer* mixer)
{
    destroy(MemoryTag::Audio, mixer);
}

bool send(Mixer& mixer, const Command& command)
{
    if (mixer.commands.push(command))
    {
        return true;
    }
    ++mixer.dropped;
    return false;
}

bool receive(Mixer& mixer, Feedback& feedback)
{
    return mixer.feedback.pop(feedback);
}

void setDelay(Mixer& mixer, float seconds)
{
    mixer.delaySeconds = std::max(seconds, 0.0f);
}

float delay(const Mixer& mixer)
{
    return mixer.delaySeconds.load();
}

MixerStats stats(const Mixer& mixer)
{
    return {mixer.playing.load(), mixer.lateStarts.load(), mixer.underruns.load(),
            mixer.latency.load(), mixer.dropped.load(),    mixer.starved.load()};
}

void noteUnderrun(Mixer& mixer)
{
    ++mixer.underruns;
}

void setLatencyEstimate(Mixer& mixer, float seconds)
{
    mixer.latency = seconds;
}

void mix(Mixer& mixer, float* out, uint32_t frames, uint64_t nowNs)
{
    // The clock: where the cursor falls on the input clock. Callbacks come
    // in bursts, so their times are smoothed against the frames mixed; a
    // jump (the device restarting) sets it again.
    const double now = static_cast<double>(nowNs);
    if (!mixer.anchored)
    {
        mixer.cursorNs = now;
        mixer.anchored = true;
    }
    else
    {
        const double predicted = mixer.cursorNs + mixer.lastFrames * 1e9 / mixRate;
        const double error = now - predicted;
        mixer.cursorNs = std::fabs(error) > 2e8 ? now : predicted + error * 0.02;
    }
    mixer.lastFrames = frames;

    Command command;
    while (mixer.commands.pop(command))
    {
        apply(mixer, command);
    }
    while (frames > 0)
    {
        const uint32_t block = std::min(frames, blockFrames);
        mixBlock(mixer, out, block);
        out += block * 2;
        frames -= block;
        mixer.cursor += block;
    }
}

} // namespace crucible::audio
