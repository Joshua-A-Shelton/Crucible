// Sound import (ADR 0083). Every source is decoded to float frames first
// (dr_wav, dr_flac, or stb_vorbis through the core), processed there (mono,
// trim, normalize), then encoded: 16-bit PCM, or IMA ADPCM in blocks. An
// .ogg imported as Vorbis skips all that and is kept byte for byte, so the
// settings that change samples don't apply to it (and say so).

#include "Common.h"
#include "MeshBuilder.h"

#include <crucible/Adpcm.h>
#include <crucible/Memory.h>
#include <crucible/Vorbis.h>
#include <crucible/importers/Sound.h>

#define DR_WAV_NO_STDIO
#define DR_FLAC_NO_STDIO
#include <dr_flac.h>
#include <dr_wav.h>

#include <cstring>

namespace crucible::importers {

struct SoundImport
{
    SoundInfo info{};
    uint32_t tag = 0;
    Vector<uint8_t> data{MemoryTag::Assets};
    StringList warnings;
};

namespace {

constexpr MemoryTag tag = MemoryTag::Assets;
constexpr uint32_t blockFrames = 1024;

// dr_libs free without a size and Crucible's allocator needs one, so each
// block keeps it in front.
constexpr size_t prefix = 16;

void* allocate(size_t size, void*)
{
    auto* block = static_cast<std::byte*>(tryAllocate(size + prefix, 16, tag));
    if (block == nullptr)
    {
        return nullptr;
    }
    std::memcpy(block, &size, sizeof(size));
    return block + prefix;
}

void release(void* pointer, void*)
{
    if (pointer == nullptr)
    {
        return;
    }
    std::byte* block = static_cast<std::byte*>(pointer) - prefix;
    size_t size;
    std::memcpy(&size, block, sizeof(size));
    free(block, size + prefix, tag);
}

void* reallocate(void* pointer, size_t size, void* user)
{
    void* moved = allocate(size, user);
    if (moved != nullptr && pointer != nullptr)
    {
        size_t old;
        std::memcpy(&old, static_cast<std::byte*>(pointer) - prefix, sizeof(old));
        std::memcpy(moved, pointer, old < size ? old : size);
        release(pointer, user);
    }
    return moved;
}

const drwav_allocation_callbacks wavMemory{nullptr, allocate, reallocate, release};
const drflac_allocation_callbacks flacMemory{nullptr, allocate, reallocate, release};

enum class Format
{
    Unknown,
    Wav,
    Flac,
    Ogg,
};

Format formatOf(std::span<const uint8_t> source)
{
    auto starts = [&](const char* magic, size_t at = 0) {
        const size_t length = std::strlen(magic);
        return source.size() >= at + length && std::memcmp(source.data() + at, magic, length) == 0;
    };
    if (starts("RIFF") && starts("WAVE", 8))
    {
        return Format::Wav;
    }
    if (starts("fLaC"))
    {
        return Format::Flac;
    }
    return starts("OggS") ? Format::Ogg : Format::Unknown;
}

struct Decoded
{
    uint32_t rate = 0;
    uint32_t channels = 0;
    Vector<float> samples{tag}; // interleaved
    uint64_t frames() const
    {
        return channels == 0 ? 0 : samples.size() / channels;
    }
};

Status decode(std::span<const uint8_t> source, Format format, Decoded& out)
{
    unsigned int channels = 0, rate = 0;
    uint64_t frames = 0;
    float* samples = nullptr;
    if (format == Format::Wav)
    {
        drwav_uint64 count = 0;
        samples = drwav_open_memory_and_read_pcm_frames_f32(source.data(), source.size(), &channels, &rate, &count,
                                                            &wavMemory);
        frames = count;
    }
    else if (format == Format::Flac)
    {
        drflac_uint64 count = 0;
        samples = drflac_open_memory_and_read_pcm_frames_f32(source.data(), source.size(), &channels, &rate, &count,
                                                             &flacMemory);
        frames = count;
    }
    else
    {
        audio::VorbisDecoder* decoder = audio::openVorbis(source);
        if (decoder == nullptr)
        {
            return fail("it isn't an Ogg Vorbis file stb_vorbis can read (mono or stereo)");
        }
        const SoundInfo info = audio::vorbisInfo(*decoder);
        out.rate = info.sampleRate;
        out.channels = info.channels;
        Vector<int16_t> piece(tag);
        piece.resize(size_t{4096} * info.channels);
        for (uint32_t got; (got = audio::decodeVorbis(*decoder, piece.span())) > 0;)
        {
            for (size_t i = 0; i < size_t{got} * info.channels; ++i)
            {
                out.samples.pushBack(piece[i] / 32768.0f);
            }
        }
        audio::closeVorbis(decoder);
        return out.samples.empty() ? fail("its Ogg Vorbis stream has no samples") : Status::Ok;
    }
    if (samples == nullptr)
    {
        return fail(format == Format::Wav
                        ? "dr_wav can't read it (compressed WAVs other than ADPCM and A/u-law aren't supported)"
                        : "dr_flac can't read it");
    }
    out.rate = rate;
    out.channels = channels;
    out.samples.assign(std::span<const float>(samples, static_cast<size_t>(frames) * channels));
    if (format == Format::Wav)
    {
        drwav_free(samples, &wavMemory);
    }
    else
    {
        drflac_free(samples, &flacMemory);
    }
    if (channels < 1 || channels > 2)
    {
        return fail("it has %u channels: Crucible plays mono and stereo sounds", channels);
    }
    if (rate == 0 || frames == 0)
    {
        return fail("it has no samples");
    }
    return Status::Ok;
}

float fromDecibels(float decibels)
{
    return std::pow(10.0f, decibels / 20.0f);
}

void toMono(Decoded& sound)
{
    if (sound.channels != 2)
    {
        return;
    }
    const uint64_t frames = sound.frames();
    for (uint64_t f = 0; f < frames; ++f)
    {
        sound.samples[f] = (sound.samples[f * 2] + sound.samples[f * 2 + 1]) * 0.5f;
    }
    sound.samples.resize(static_cast<size_t>(frames));
    sound.channels = 1;
}

// Cuts frames quieter than the threshold (on every channel) from both ends.
void trim(Decoded& sound, float threshold)
{
    const uint64_t frames = sound.frames();
    auto loud = [&](uint64_t frame) {
        for (uint32_t c = 0; c < sound.channels; ++c)
        {
            if (std::fabs(sound.samples[frame * sound.channels + c]) > threshold)
            {
                return true;
            }
        }
        return false;
    };
    uint64_t first = 0, last = frames;
    while (first < frames && !loud(first))
    {
        ++first;
    }
    while (last > first && !loud(last - 1))
    {
        --last;
    }
    if (first == last)
    {
        last = first + 1 <= frames ? first + 1 : first; // all silence: keep one frame
    }
    Vector<float> kept(tag);
    kept.assign(sound.samples.span().subspan(first * sound.channels, (last - first) * sound.channels));
    sound.samples = std::move(kept);
}

void normalize(Decoded& sound, float peak)
{
    float loudest = 0;
    for (const float sample : sound.samples)
    {
        loudest = std::max(loudest, std::fabs(sample));
    }
    if (loudest <= 0)
    {
        return;
    }
    const float gain = peak / loudest;
    for (float& sample : sound.samples)
    {
        sample *= gain;
    }
}

Vector<int16_t> toPcm(const Decoded& sound)
{
    Vector<int16_t> pcm(tag);
    pcm.resize(sound.samples.size());
    for (size_t i = 0; i < pcm.size(); ++i)
    {
        pcm[i] = static_cast<int16_t>(std::lround(std::clamp(sound.samples[i], -1.0f, 1.0f) * 32767.0f));
    }
    return pcm;
}

} // namespace

Result<SoundImport*> importSound(std::span<const uint8_t> source, SoundSettings settings)
{
    clearError();
    const Format format = formatOf(source);
    if (format == Format::Unknown)
    {
        return fail("it isn't a WAV, FLAC, or Ogg Vorbis file");
    }
    SoundImport* sound = create<SoundImport>(tag);
    auto failed = [&](Status status) -> Result<SoundImport*> {
        destroy(tag, sound);
        return status;
    };
    SoundImportEncoding encoding = settings.encoding;
    if (encoding == SoundImportEncoding::Vorbis && format != Format::Ogg)
    {
        sound->warnings.add("Vorbis needs an .ogg source (there's no Vorbis encoder): imported as Auto");
        encoding = SoundImportEncoding::Auto;
    }
    if (encoding == SoundImportEncoding::Auto && format == Format::Ogg)
    {
        encoding = SoundImportEncoding::Vorbis;
    }

    if (encoding == SoundImportEncoding::Vorbis)
    {
        audio::VorbisDecoder* decoder = audio::openVorbis(source);
        if (decoder == nullptr)
        {
            return failed(fail("it isn't an Ogg Vorbis file stb_vorbis can read (mono or stereo)"));
        }
        sound->info = audio::vorbisInfo(*decoder);
        audio::closeVorbis(decoder);
        if (settings.mono || settings.trimSilence || settings.normalize)
        {
            sound->warnings.add("Mono, TrimSilence, and Normalize don't apply to Vorbis, which is kept as it is: "
                                "choose Pcm or Adpcm to use them");
        }
        sound->tag = binary::tag("OGGV");
        sound->data.assign(source);
    }
    else
    {
        Decoded decoded;
        if (const Status status = decode(source, format, decoded); status != Status::Ok)
        {
            return failed(status);
        }
        if (settings.mono)
        {
            toMono(decoded);
        }
        if (settings.trimSilence)
        {
            trim(decoded, fromDecibels(settings.silenceThreshold));
        }
        if (settings.normalize)
        {
            normalize(decoded, fromDecibels(std::min(settings.normalizePeak, 0.0f)));
        }
        if (encoding == SoundImportEncoding::Auto)
        {
            const double seconds = static_cast<double>(decoded.frames()) / decoded.rate;
            encoding = seconds > settings.adpcmAfterSeconds ? SoundImportEncoding::Adpcm : SoundImportEncoding::Pcm;
        }
        const Vector<int16_t> pcm = toPcm(decoded);
        const uint32_t frames = static_cast<uint32_t>(decoded.frames());
        sound->info = {decoded.rate, decoded.channels, frames, assets::SoundEncoding::Pcm16, 0, 0, 0};
        if (encoding == SoundImportEncoding::Adpcm)
        {
            sound->info.encoding = assets::SoundEncoding::ImaAdpcm;
            sound->info.blockFrames = blockFrames;
            sound->tag = binary::tag("ADPC");
            const uint32_t bytes = audio::adpcm::blockBytes(blockFrames, decoded.channels);
            const uint32_t blocks = (frames + blockFrames - 1) / blockFrames;
            sound->data.resize(size_t{blocks} * bytes);
            for (uint32_t b = 0; b < blocks; ++b)
            {
                const uint32_t first = b * blockFrames;
                audio::adpcm::encodeBlock(pcm.span().subspan(size_t{first} * decoded.channels),
                                          std::min(blockFrames, frames - first), decoded.channels, blockFrames,
                                          sound->data.span().subspan(size_t{b} * bytes, bytes));
            }
        }
        else
        {
            sound->tag = binary::tag("PCMS");
            sound->data.assign(std::span(reinterpret_cast<const uint8_t*>(pcm.data()), pcm.size() * 2));
        }
    }
    sound->info.beatsPerMinute = std::max(settings.beatsPerMinute, 0.0f);
    sound->info.beatsPerBar = std::max(settings.beatsPerBar, 1u);
    return sound;
}

void destroySoundImport(SoundImport* sound)
{
    destroy(tag, sound);
}

SoundInfo soundImportInfo(const SoundImport& sound)
{
    return sound.info;
}

uint32_t soundDataTag(const SoundImport& sound)
{
    return sound.tag;
}

uint64_t soundDataSize(const SoundImport& sound)
{
    return sound.data.size();
}

void copySoundData(const SoundImport& sound, std::span<uint8_t> destination)
{
    std::memcpy(destination.data(), sound.data.data(), std::min<size_t>(destination.size(), sound.data.size()));
}

uint32_t soundWarningCount(const SoundImport& sound)
{
    return sound.warnings.count();
}

std::string_view soundWarning(const SoundImport& sound, uint32_t index)
{
    return sound.warnings.at(index);
}

} // namespace crucible::importers
