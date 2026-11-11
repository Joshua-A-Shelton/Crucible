// Vorbis streaming (ADR 0083): a voice plays what the background jobs decode
// ahead of it, the same as decoding the whole file; music loops past the
// file's end; a stream left too far behind starves, and that's counted.

#include "SoundTestSupport.h"

#include <crucible/Vorbis.h>

using namespace crucible;
using namespace crucible::audio;
using namespace crucible::audio::testing;

namespace {

// The tone, imported as Vorbis and served as an asset, and all of it decoded.
struct Tone
{
    std::vector<uint8_t> source = testSound("tone.ogg");
    assets::AssetId id;
    std::vector<float> samples;

    explicit Tone(uint8_t n)
    {
        const Imported imported(source, defaults());
        REQUIRE(imported.info().encoding == assets::SoundEncoding::Vorbis);
        id = SoundAssets::add(n, imported.file());
        VorbisDecoder* decoder = openVorbis(source);
        REQUIRE(decoder != nullptr);
        std::vector<int16_t> all(vorbisInfo(*decoder).frameCount);
        REQUIRE(decodeVorbis(*decoder, all) == all.size());
        closeVorbis(decoder);
        samples = asFloats(all);
    }
};

} // namespace

TEST_CASE("A Vorbis voice plays what's decoded ahead of it")
{
    const Tone tone(2);
    Offline mixer;
    REQUIRE(playOneShot(tone.id, 0, here, 1, masterBus) != 0);
    mixer.mix(tone.samples.size() + 4800);
    float worst = 0;
    for (uint64_t frame = 0; frame < tone.samples.size(); ++frame)
    {
        worst = std::max(worst, std::fabs(mixer.left(frame) - tone.samples[frame] * centre));
    }
    CHECK(worst < 1e-5f);
    CHECK(mixer.peak(tone.samples.size() + 1, mixer.frames, 0) == 0.0f);
    CHECK(mixerStats().starvedBlocks == 0);
    CHECK(mixerStats().voices == 0);
}

TEST_CASE("Vorbis music loops past the file's end")
{
    const Tone tone(3);
    Offline mixer;
    playMusic(tone.id, {0, MusicAlign::Now});
    mixer.mix(tone.samples.size() * 2 + 480);
    float worst = 0;
    for (uint64_t frame = tone.samples.size(); frame < tone.samples.size() * 2; ++frame)
    {
        worst = std::max(worst, std::fabs(mixer.left(frame) - tone.samples[frame - tone.samples.size()] * centre));
    }
    CHECK(worst < 1e-5f);
    CHECK(mixerStats().starvedBlocks == 0);
}

TEST_CASE("A stream asked for more than is decoded starves and is counted")
{
    const Tone tone(4);
    Offline mixer;
    REQUIRE(playOneShot(tone.id, 0, here, 1, masterBus) != 0);
    // One request for the whole second, with nothing to top the stream up
    // in between: only the first half second was decoded.
    std::vector<float> out(tone.samples.size() * 2);
    mixOffline(out);
    CHECK(mixerStats().starvedBlocks > 0);
}
