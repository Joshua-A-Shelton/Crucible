// IMA ADPCM (ADR 0083): close to the source it was encoded from, and played
// by the mixer exactly as it decodes, across its blocks.

#include "SoundTestSupport.h"

using namespace crucible;
using namespace crucible::audio;
using namespace crucible::audio::testing;

TEST_CASE("ADPCM stays close to a tone")
{
    const std::vector<int16_t> tone = sine(440, 1024 / 48000.0 * 8);
    std::vector<uint8_t> block(adpcm::blockBytes(1024, 1));
    std::vector<int16_t> decoded(1024);
    double error = 0, signal = 0;
    for (size_t b = 0; b < 8; ++b)
    {
        adpcm::encodeBlock(std::span(tone).subspan(b * 1024), 1024, 1, 1024, block);
        adpcm::decodeBlock(block.data(), 1, 1024, decoded.data());
        for (size_t i = 0; i < 1024; ++i)
        {
            const double difference = decoded[i] - tone[b * 1024 + i];
            error += difference * difference;
            signal += static_cast<double>(tone[b * 1024 + i]) * tone[b * 1024 + i];
        }
    }
    // Signal to noise, in decibels.
    CHECK(10 * std::log10(signal / error) > 30);
}

TEST_CASE("The mixer plays ADPCM as it decodes")
{
    SoundSettings settings = defaults(SoundImportEncoding::Adpcm);
    const Imported imported(testSound("tone.wav"), settings);
    const assets::AssetId id = SoundAssets::add(1, imported.file());
    // The whole sound decoded, block by block.
    const assets::SoundInfo info = imported.info();
    const std::vector<uint8_t> blocks = imported.data();
    std::vector<int16_t> expected((info.frameCount + 1023) / 1024 * 1024);
    for (size_t b = 0; b < expected.size() / 1024; ++b)
    {
        adpcm::decodeBlock(blocks.data() + b * adpcm::blockBytes(1024, 1), 1, 1024, expected.data() + b * 1024);
    }

    Offline mixer;
    REQUIRE(playOneShot(id, 0, here, 1, masterBus) != 0);
    mixer.mix(48000 + 960);
    float worst = 0;
    for (uint64_t frame = 0; frame < 48000; ++frame)
    {
        worst = std::max(worst, std::fabs(mixer.left(frame) - expected[frame] / 32768.0f * centre));
    }
    CHECK(worst < 1e-5f);
    CHECK(mixer.peak(48001, 48960, 0) == 0.0f);
}
