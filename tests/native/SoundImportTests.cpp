// Sound import (ADR 0083): WAV, FLAC, and Ogg Vorbis read; Auto's choices;
// mono, trimming, and normalizing; Vorbis kept as it is; tempo; bad files.

#include "SoundTestSupport.h"

using namespace crucible;
using namespace crucible::audio::testing;

namespace {

float peakOf(const std::vector<int16_t>& samples, uint32_t channels = 1, uint32_t channel = 0)
{
    int32_t most = 0;
    for (size_t i = channel; i < samples.size(); i += channels)
    {
        most = std::max(most, std::abs(static_cast<int32_t>(samples[i])));
    }
    return most / 32767.0f;
}

} // namespace

TEST_CASE("WAV FLAC and Ogg Vorbis are read")
{
    const Imported wav(testSound("tone.wav"), defaults(SoundImportEncoding::Pcm));
    CHECK(wav.info().sampleRate == 48000);
    CHECK(wav.info().channels == 1);
    CHECK(wav.info().frameCount == 48000);
    CHECK(wav.info().encoding == assets::SoundEncoding::Pcm16);
    CHECK(peakOf(wav.pcm()) == doctest::Approx(0.5f).epsilon(0.01));

    const Imported flac(testSound("tone.flac"), defaults(SoundImportEncoding::Pcm));
    CHECK(flac.info().sampleRate == 44100);
    CHECK(flac.info().channels == 2);
    CHECK(flac.info().frameCount == 44100);
    CHECK(peakOf(flac.pcm(), 2, 0) == doctest::Approx(0.5f).epsilon(0.01));
    CHECK(peakOf(flac.pcm(), 2, 1) == doctest::Approx(0.25f).epsilon(0.01));

    const Imported ogg(testSound("tone.ogg"), defaults(SoundImportEncoding::Pcm));
    CHECK(ogg.info().sampleRate == 48000);
    CHECK(ogg.info().channels == 1);
    CHECK(ogg.info().frameCount == doctest::Approx(48000).epsilon(0.01));
    CHECK(peakOf(ogg.pcm()) == doctest::Approx(0.5f).epsilon(0.05));
}

TEST_CASE("Auto keeps Ogg as Vorbis and makes long sounds ADPCM")
{
    const std::vector<uint8_t> source = testSound("tone.ogg");
    const Imported ogg(source, defaults());
    CHECK(ogg.info().encoding == assets::SoundEncoding::Vorbis);
    CHECK(importers::soundDataTag(*ogg.sound) == binary::tag("OGGV"));
    CHECK(ogg.data() == source);

    const Imported shortWav(testSound("tone.wav"), defaults());
    CHECK(shortWav.info().encoding == assets::SoundEncoding::Pcm16);
    SoundSettings settings = defaults();
    settings.adpcmAfterSeconds = 0.5f;
    const Imported longWav(testSound("tone.wav"), settings);
    CHECK(longWav.info().encoding == assets::SoundEncoding::ImaAdpcm);
    CHECK(longWav.info().blockFrames == 1024);
    CHECK(importers::soundDataTag(*longWav.sound) == binary::tag("ADPC"));
    // A quarter of PCM's size, near enough (headers and the last block's padding).
    CHECK(longWav.data().size() == 47 * audio::adpcm::blockBytes(1024, 1));
}

TEST_CASE("Mono trimming and normalizing change the samples")
{
    SoundSettings settings = defaults(SoundImportEncoding::Pcm);
    settings.mono = true;
    settings.trimSilence = true;
    settings.silenceThreshold = -40;
    settings.normalize = true;
    settings.normalizePeak = -1;
    const Imported flac(testSound("tone.flac"), settings);
    CHECK(flac.info().channels == 1);
    // Half a second of tone left, the silence either side cut.
    CHECK(flac.info().frameCount == doctest::Approx(22050).epsilon(0.01));
    CHECK(peakOf(flac.pcm()) == doctest::Approx(0.891f).epsilon(0.01)); // -1 dBFS
    CHECK(std::abs(flac.pcm().front()) > 300);
}

TEST_CASE("Settings that can't apply say so")
{
    SoundSettings settings = defaults(SoundImportEncoding::Vorbis);
    settings.mono = true;
    const Imported ogg(testSound("tone.ogg"), settings);
    CHECK(importers::soundWarningCount(*ogg.sound) == 1);
    CHECK(ogg.info().encoding == assets::SoundEncoding::Vorbis);

    const Imported wav(testSound("tone.wav"), defaults(SoundImportEncoding::Vorbis));
    REQUIRE(importers::soundWarningCount(*wav.sound) == 1);
    CHECK(importers::soundWarning(*wav.sound, 0).find("needs an .ogg") != std::string_view::npos);
    CHECK(wav.info().encoding == assets::SoundEncoding::Pcm16);
}

TEST_CASE("Music keeps its tempo and bad files fail with a reason")
{
    SoundSettings settings = defaults();
    settings.beatsPerMinute = 96;
    settings.beatsPerBar = 3;
    const Imported ogg(testSound("tone.ogg"), settings);
    CHECK(ogg.info().beatsPerMinute == 96.0f);
    CHECK(ogg.info().beatsPerBar == 3);

    const std::vector<uint8_t> junk(64, 0x42);
    CHECK_FALSE(importers::importSound(junk, defaults()));
    CHECK(importers::lastImportError().find("isn't a WAV") != std::string_view::npos);
    std::vector<uint8_t> broken = testSound("tone.ogg");
    broken.resize(200);
    CHECK_FALSE(importers::importSound(broken, defaults(SoundImportEncoding::Pcm)));
}
