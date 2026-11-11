// stb_vorbis behind crucible/Vorbis.h (ADR 0083). stb_vorbis is built on its
// own (CMakeLists.txt); here it's only declared. It takes its memory from a
// buffer given to it, so every decoder's memory comes from Crucible's
// allocator (ADR 0032) under the Audio tag: too small a buffer and it says
// so, and a bigger one is tried.

#include <crucible/Memory.h>
#include <crucible/Vorbis.h>

#define STB_VORBIS_HEADER_ONLY
#define STB_VORBIS_NO_STDIO
#define STB_VORBIS_NO_PUSHDATA_API
#include <stb_vorbis.c>

namespace crucible::audio {

struct VorbisDecoder
{
    stb_vorbis* vorbis = nullptr;
    void* memory = nullptr;
    size_t memorySize = 0;
    assets::SoundInfo info{};
};

VorbisDecoder* openVorbis(std::span<const uint8_t> ogg)
{
    if (ogg.size() > static_cast<size_t>(INT32_MAX))
    {
        return nullptr;
    }
    for (size_t size = 256 * 1024; size <= 8 * 1024 * 1024; size *= 2)
    {
        void* memory = tryAllocate(size, 16, MemoryTag::Audio);
        if (memory == nullptr)
        {
            return nullptr;
        }
        const stb_vorbis_alloc buffer{static_cast<char*>(memory), static_cast<int>(size)};
        int error = 0;
        stb_vorbis* vorbis = stb_vorbis_open_memory(ogg.data(), static_cast<int>(ogg.size()), &error, &buffer);
        if (vorbis == nullptr)
        {
            free(memory, size, MemoryTag::Audio);
            if (error == VORBIS_outofmem)
            {
                continue;
            }
            return nullptr;
        }
        const stb_vorbis_info info = stb_vorbis_get_info(vorbis);
        if (info.channels < 1 || info.channels > 2 || info.sample_rate == 0)
        {
            stb_vorbis_close(vorbis);
            free(memory, size, MemoryTag::Audio);
            return nullptr;
        }
        VorbisDecoder* decoder = create<VorbisDecoder>(MemoryTag::Audio);
        decoder->vorbis = vorbis;
        decoder->memory = memory;
        decoder->memorySize = size;
        decoder->info = {info.sample_rate,
                         static_cast<uint32_t>(info.channels),
                         stb_vorbis_stream_length_in_samples(vorbis),
                         assets::SoundEncoding::Vorbis,
                         0,
                         0,
                         0};
        return decoder;
    }
    return nullptr;
}

void closeVorbis(VorbisDecoder* decoder)
{
    if (decoder == nullptr)
    {
        return;
    }
    stb_vorbis_close(decoder->vorbis);
    free(decoder->memory, decoder->memorySize, MemoryTag::Audio);
    destroy(MemoryTag::Audio, decoder);
}

assets::SoundInfo vorbisInfo(const VorbisDecoder& decoder)
{
    return decoder.info;
}

uint32_t decodeVorbis(VorbisDecoder& decoder, std::span<int16_t> into)
{
    const int channels = static_cast<int>(decoder.info.channels);
    const int frames =
        stb_vorbis_get_samples_short_interleaved(decoder.vorbis, channels, into.data(), static_cast<int>(into.size()));
    return frames > 0 ? static_cast<uint32_t>(frames) : 0;
}

bool rewindVorbis(VorbisDecoder& decoder)
{
    return stb_vorbis_seek_start(decoder.vorbis) != 0;
}

} // namespace crucible::audio
