#pragma once

// Crucible's mixer (ADR 0082), the part that runs on the audio thread: voices
// resampled and panned into buses, buses filtered, ducked, and sent to the
// shared reverb, all summed into the master. The main thread talks to it only
// through two lock-free queues, commands in and feedback out, so neither ever
// waits for the other. MixerService.cpp is the main thread's side: the
// device, sounds, the service, and "Audio.Update".

#include <crucible/Audio.h>
#include <crucible/Systems.h>
#include <crucible/Vector.h>

#include <atomic>
#include <cstdint>

namespace crucible::audio {

// A sound in memory: its samples, interleaved, ready to play.
struct Sound
{
    assets::SoundInfo info{};
    Vector<int16_t> samples{MemoryTag::Audio};
};

// One producer, one consumer: a ring of slots, the producer moving `tail`
// and the consumer `head`, each only reading the other's. No locks, no
// allocations; a full ring turns pushes away.
template <class T, uint32_t capacity>
class RingQueue
{
public:
    bool push(const T& item)
    {
        const uint32_t tail = tailIndex.load(std::memory_order_relaxed);
        const uint32_t next = (tail + 1) % capacity;
        if (next == headIndex.load(std::memory_order_acquire))
        {
            return false;
        }
        items[tail] = item;
        tailIndex.store(next, std::memory_order_release);
        return true;
    }

    bool pop(T& item)
    {
        const uint32_t head = headIndex.load(std::memory_order_relaxed);
        if (head == tailIndex.load(std::memory_order_acquire))
        {
            return false;
        }
        item = items[head];
        headIndex.store((head + 1) % capacity, std::memory_order_release);
        return true;
    }

private:
    std::atomic<uint32_t> headIndex{0};
    std::atomic<uint32_t> tailIndex{0};
    T items[capacity];
};

enum class CommandKind : uint32_t
{
    Play,      // a voice: one-shot, source, or music
    Stop,      // a voice, fading
    Voice,     // a voice's volume, pitch, and position (sources, each frame)
    Listener,  // where it's heard from
    BusVolume, // over seconds
    BusEffects,
    Music, // the next track, crossfading, aligned
    StopAll,
    Forget, // a sound about to be freed: stop what plays it, then say so
};

struct Command
{
    CommandKind kind = CommandKind::Play;
    uint32_t voice = 0;
    const Sound* sound = nullptr;
    uint64_t atNs = 0; // 0: now
    float volume = 1;
    float pitch = 1;
    uint32_t bus = masterBus;
    bool loop = false;
    bool spatial = false;
    Vector3 position{};
    Vector3 forward{0, 0, 1}; // the listener's
    Vector3 up{0, 1, 0};
    float minDistance = 1;
    float maxDistance = 50;
    Rolloff rolloff = Rolloff::Inverse;
    float seconds = 0;
    BusEffects effects{};
    MusicTransition transition{};
};

enum class FeedbackKind : uint32_t
{
    VoiceEnded,
    Forgotten, // nothing plays the sound any more
};

struct Feedback
{
    FeedbackKind kind;
    uint32_t voice;
    const Sound* sound;
};

class Mixer;

// The mixer: made on the main thread (everything it needs is allocated
// then), mixed on the audio thread.
Mixer* createMixer();
void destroyMixer(Mixer* mixer);
// Main thread.
bool send(Mixer& mixer, const Command& command);
bool receive(Mixer& mixer, Feedback& feedback);
void setDelay(Mixer& mixer, float seconds);
float delay(const Mixer& mixer);
MixerStats stats(const Mixer& mixer);
void noteUnderrun(Mixer& mixer);
void setLatencyEstimate(Mixer& mixer, float seconds);
// Audio thread: the next frames, interleaved stereo, the first of them made
// at nowNs on the input clock (the device's callback time, smoothed inside;
// exact offline).
void mix(Mixer& mixer, float* out, uint32_t frames, uint64_t nowNs);

// The mixer's "Audio.Update": the listener and the AudioSources, if the
// mixer is running; false if it isn't (the null audio's update runs).
bool updateMixerSources(const loop::SystemContext& context);
// The null audio's service, which stopMixing puts back.
const AudioService& nullAudioService();

} // namespace crucible::audio
