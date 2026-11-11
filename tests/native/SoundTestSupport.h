#pragma once

// What the sound import tests share (ADR 0083): the test tones, importing
// them, and serving the result to the mixer as an asset, as the editor would.

#include "AudioTestSupport.h"

#include <crucible/Adpcm.h>
#include <crucible/AssetSource.h>
#include <crucible/BinaryFile.h>
#include <crucible/importers/Sound.h>
#include <crucible/importers/Texture.h> // lastImportError

#include <cstring>
#include <fstream>
#include <iterator>
#include <map>
#include <string>

namespace crucible::audio::testing {

using importers::SoundImportEncoding;
using importers::SoundSettings;

inline std::vector<uint8_t> testSound(const std::string& name)
{
    std::ifstream file(std::string(CRUCIBLE_SOUNDS_DIR) + "/" + name, std::ios::binary);
    REQUIRE(file.good());
    return {std::istreambuf_iterator<char>(file), std::istreambuf_iterator<char>()};
}

inline SoundSettings defaults(SoundImportEncoding encoding = SoundImportEncoding::Auto)
{
    return {encoding, 5.0f, 0, 4, false, false, -1.0f, false, -60.0f};
}

// An import's result, destroyed with it.
struct Imported
{
    importers::SoundImport* sound = nullptr;

    Imported(const std::vector<uint8_t>& source, SoundSettings settings)
    {
        Result<importers::SoundImport*> result = importers::importSound(source, settings);
        CAPTURE(importers::lastImportError());
        REQUIRE(result);
        sound = result.value();
    }

    ~Imported()
    {
        importers::destroySoundImport(sound);
    }

    Imported(const Imported&) = delete;
    Imported& operator=(const Imported&) = delete;

    assets::SoundInfo info() const
    {
        return importers::soundImportInfo(*sound);
    }

    std::vector<uint8_t> data() const
    {
        std::vector<uint8_t> bytes(importers::soundDataSize(*sound));
        importers::copySoundData(*sound, bytes);
        return bytes;
    }

    std::vector<int16_t> pcm() const
    {
        const std::vector<uint8_t> bytes = data();
        std::vector<int16_t> samples(bytes.size() / 2);
        std::memcpy(samples.data(), bytes.data(), samples.size() * 2);
        return samples;
    }

    // The SOND file the C# importer would write.
    std::vector<uint8_t> file() const
    {
        binary::Writer* writer = binary::createWriter(assets::soundFileKind, assets::soundFileVersion);
        const assets::SoundInfo header = info();
        REQUIRE(binary::addSection(*writer, binary::tag("INFO"), 1,
                                   std::span(reinterpret_cast<const uint8_t*>(&header), sizeof(header)), 16));
        const std::vector<uint8_t> bytes = data();
        REQUIRE(binary::addSection(*writer, importers::soundDataTag(*sound), 1, bytes, 16));
        Vector<std::byte> out(MemoryTag::General);
        REQUIRE(binary::writeToMemory(*writer, binary::Compression::None, out));
        binary::destroyWriter(writer);
        const auto* begin = reinterpret_cast<const uint8_t*>(out.data());
        return {begin, begin + out.size()};
    }
};

// Imported sounds served as assets, by the first byte of their ID.
struct SoundAssets
{
    inline static std::map<uint8_t, std::vector<uint8_t>> files;

    static assets::AssetId add(uint8_t n, std::vector<uint8_t> file)
    {
        files[n] = std::move(file);
        assets::installAssetSource({[](const assets::AssetId* id, void* context, assets::AssetDelivery deliver) {
            const auto found = files.find(id->bytes[0]);
            if (found == files.end() || id->bytes[1] != 0x5d)
            {
                return -1;
            }
            deliver(context, found->second.data(), found->second.size());
            return 0;
        }});
        assets::AssetId id{};
        id.bytes[0] = n;
        id.bytes[1] = 0x5d; // apart from other tests' IDs
        return id;
    }
};

// The whole of a sound decoded, as floats: what the mixer should play.
inline std::vector<float> asFloats(const std::vector<int16_t>& samples)
{
    std::vector<float> out(samples.size());
    for (size_t i = 0; i < samples.size(); ++i)
    {
        out[i] = samples[i] / 32768.0f;
    }
    return out;
}

} // namespace crucible::audio::testing
