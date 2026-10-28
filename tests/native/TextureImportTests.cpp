// Texture import (ADR 0042): decoding, linear-light mipmaps, alpha coverage,
// normal maps, and BC7/BC5/BC4 quality, measured by decoding the result.

#include <crucible/BinaryFile.h>
#include <crucible/Jobs.h>
#include <crucible/Memory.h>
#include <crucible/importers/Texture.h>

#include <doctest/doctest.h>

#define STB_IMAGE_WRITE_IMPLEMENTATION
#if defined(_MSC_VER)
#pragma warning(push, 0)
#else
#pragma GCC diagnostic push
#pragma GCC diagnostic ignored "-Wall"
#pragma GCC diagnostic ignored "-Wextra"
#pragma GCC diagnostic ignored "-Wpedantic"
#pragma GCC diagnostic ignored "-Wmissing-field-initializers"
#endif
#include <stb_image_write.h>
#if defined(_MSC_VER)
#pragma warning(pop)
#else
#pragma GCC diagnostic pop
#endif

#include <cmath>
#include <cstring>
// doctest prints a string_view with operator<<, which MSVC declares only in
// <ostream>.
#include <ostream>
#include <random>
#include <string>
#include <string_view>
#include <vector>

using namespace crucible;
using namespace crucible::importers;

namespace {

struct Rgba
{
    uint32_t width = 0;
    uint32_t height = 0;
    std::vector<uint8_t> texels; // RGBA8

    Rgba(uint32_t w, uint32_t h)
        : width(w)
        , height(h)
        , texels(size_t{w} * h * 4)
    {}

    uint8_t* at(uint32_t x, uint32_t y)
    {
        return texels.data() + (size_t{y} * width + x) * 4;
    }
};

std::vector<uint8_t> png(const Rgba& image)
{
    std::vector<uint8_t> bytes;
    stbi_write_png_to_func(
        [](void* context, void* data, int size) {
            auto* out = static_cast<std::vector<uint8_t>*>(context);
            out->insert(out->end(), static_cast<uint8_t*>(data), static_cast<uint8_t*>(data) + size);
        },
        &bytes, static_cast<int>(image.width), static_cast<int>(image.height), 4, image.texels.data(),
        static_cast<int>(image.width * 4));
    return bytes;
}

TextureSettings settingsFor(TextureUsage usage)
{
    return {.usage = usage,
            .mipmaps = true,
            .wrap = true,
            .preserveAlphaCoverage = false,
            .alphaCutoff = 0.5f,
            .rdoLambda = 0.0f,
            .quality = 2};
}

struct Imported
{
    TextureImport* texture = nullptr;

    explicit Imported(const std::vector<uint8_t>& source, TextureSettings settings)
    {
        Result<TextureImport*> result = importTexture({source.data(), source.size()}, settings);
        INFO(lastImportError());
        REQUIRE(result);
        texture = result.value();
    }

    ~Imported()
    {
        destroyTextureImport(texture);
    }

    std::vector<uint8_t> encoded(uint32_t mip) const
    {
        std::vector<uint8_t> bytes(textureMipSize(*texture, mip));
        copyTextureMip(*texture, mip, bytes);
        return bytes;
    }

    Rgba decoded(uint32_t mip) const
    {
        const TextureInfo info = textureInfo(*texture);
        Rgba image(std::max(1u, info.width >> mip), std::max(1u, info.height >> mip));
        const std::vector<uint8_t> bytes = encoded(mip);
        REQUIRE(decodeTexels(info.format, image.width, image.height, bytes, image.texels));
        return image;
    }
};

// Peak signal-to-noise ratio over the first `channels` channels, in dB.
double psnr(const Rgba& a, const Rgba& b, int channels)
{
    double error = 0;
    for (size_t i = 0; i < a.texels.size(); i += 4)
    {
        for (int c = 0; c < channels; ++c)
        {
            const double d = double(a.texels[i + c]) - double(b.texels[i + c]);
            error += d * d;
        }
    }
    error /= double(a.texels.size() / 4 * channels);
    return error == 0 ? 99.0 : 10.0 * std::log10(255.0 * 255.0 / error);
}

// Smooth gradients with detail on top: a stand-in for a photo or painting.
Rgba scene(uint32_t size)
{
    Rgba image(size, size);
    for (uint32_t y = 0; y < size; ++y)
    {
        for (uint32_t x = 0; x < size; ++x)
        {
            const double u = double(x) / size, v = double(y) / size;
            uint8_t* t = image.at(x, y);
            t[0] = uint8_t(255 * u);
            t[1] = uint8_t(255 * (0.5 + 0.4 * std::sin(v * 12.0 + u * 3.0)));
            t[2] = uint8_t(255 * (0.5 + 0.5 * std::cos((u - v) * 9.0)));
            t[3] = 255;
        }
    }
    return image;
}

// A field of hemispherical bumps as a tangent-space normal map.
Rgba bumps(uint32_t size, std::vector<float>* directions = nullptr)
{
    Rgba image(size, size);
    for (uint32_t y = 0; y < size; ++y)
    {
        for (uint32_t x = 0; x < size; ++x)
        {
            const float px = (float(x % 16) + 0.5f) / 8.0f - 1.0f;
            const float py = (float(y % 16) + 0.5f) / 8.0f - 1.0f;
            const float r2 = px * px + py * py;
            float nx = 0, ny = 0, nz = 1;
            if (r2 < 0.8f)
            {
                nx = px;
                ny = py;
                nz = std::sqrt(1.0f - r2);
            }
            uint8_t* t = image.at(x, y);
            t[0] = uint8_t(std::lround((nx * 0.5f + 0.5f) * 255));
            t[1] = uint8_t(std::lround((ny * 0.5f + 0.5f) * 255));
            t[2] = uint8_t(std::lround((nz * 0.5f + 0.5f) * 255));
            t[3] = 255;
            if (directions != nullptr)
            {
                directions->insert(directions->end(), {nx, ny, nz});
            }
        }
    }
    return image;
}

double alphaCoverage(const Rgba& image)
{
    size_t covered = 0;
    for (size_t i = 3; i < image.texels.size(); i += 4)
    {
        covered += image.texels[i] >= 128 ? 1 : 0;
    }
    return double(covered) / double(image.texels.size() / 4);
}

// Compressed size of encoded bytes, as the binary format stores them.
size_t compressedSize(const std::vector<uint8_t>& bytes)
{
    binary::Writer* writer = binary::createWriter(binary::tag("TEST"), 1);
    REQUIRE(binary::addSection(*writer, binary::tag("MIP0"), 1, bytes, 16));
    Vector<std::byte> file(MemoryTag::Assets);
    REQUIRE(binary::writeToMemory(*writer, binary::Compression::Fast, file));
    binary::destroyWriter(writer);
    return file.size();
}

} // namespace

TEST_CASE("Checkerboard mips average in linear light")
{
    // Averaging black and white in sRGB values gives 128, which looks too
    // dark; in linear light the average is 0.5, which is sRGB 188.
    Rgba checker(8, 8);
    for (uint32_t y = 0; y < 8; ++y)
    {
        for (uint32_t x = 0; x < 8; ++x)
        {
            const uint8_t value = (x + y) % 2 == 0 ? 255 : 0;
            uint8_t* t = checker.at(x, y);
            t[0] = t[1] = t[2] = value;
            t[3] = 255;
        }
    }
    const Imported texture(png(checker), settingsFor(TextureUsage::Ui));
    const TextureInfo info = textureInfo(*texture.texture);
    CHECK(info.format == TextureFormat::Rgba8);
    CHECK(info.mipCount == 4);
    CHECK(info.srgb == 1);
    const Rgba mip = texture.decoded(1);
    for (size_t i = 0; i < mip.texels.size(); i += 4)
    {
        CHECK(std::abs(int(mip.texels[i]) - 188) <= 1);
    }
}

TEST_CASE("Mips halve down to one texel")
{
    Rgba image(64, 16);
    const Imported texture(png(image), settingsFor(TextureUsage::Color));
    const TextureInfo info = textureInfo(*texture.texture);
    CHECK(info.mipCount == 7); // 64, 32, 16, 8, 4, 2, 1 across
    // BC7 stores 16 bytes per 4x4 block, partial blocks included.
    CHECK(textureMipSize(*texture.texture, 0) == 16u * 4 * 16);
    CHECK(textureMipSize(*texture.texture, 4) == 16u); // 4x1
    CHECK(textureMipSize(*texture.texture, 6) == 16u); // 1x1
    CHECK(textureMipSize(*texture.texture, 7) == 0u);

    TextureSettings single = settingsFor(TextureUsage::Color);
    single.mipmaps = false;
    const Imported top(png(image), single);
    CHECK(textureInfo(*top.texture).mipCount == 1);
}

TEST_CASE("BC7 keeps colour close to the source")
{
    const Rgba source = scene(256);
    const Imported texture(png(source), settingsFor(TextureUsage::Color));
    CHECK(textureInfo(*texture.texture).format == TextureFormat::Bc7);
    const double quality = psnr(source, texture.decoded(0), 3);
    MESSAGE("BC7 PSNR " << quality << " dB");
    CHECK(quality > 40.0);
}

TEST_CASE("RDO trades a little quality for smaller compressed files")
{
    const Rgba source = scene(256);
    TextureSettings settings = settingsFor(TextureUsage::Color);
    const Imported plain(png(source), settings);
    settings.rdoLambda = 1.0f;
    const Imported reduced(png(source), settings);
    const size_t plainSize = compressedSize(plain.encoded(0));
    const size_t reducedSize = compressedSize(reduced.encoded(0));
    const double quality = psnr(source, reduced.decoded(0), 3);
    MESSAGE("BC7 with zstd: " << plainSize << " bytes plain, " << reducedSize << " with RDO (lambda 1), PSNR "
                              << quality << " dB");
    CHECK(reducedSize < plainSize * 9 / 10);
    CHECK(quality > 35.0);
}

TEST_CASE("Normal maps keep their directions through BC5 and the mips")
{
    std::vector<float> directions;
    const Rgba source = bumps(64, &directions);
    const Imported texture(png(source), settingsFor(TextureUsage::NormalMap));
    CHECK(textureInfo(*texture.texture).format == TextureFormat::Bc5);
    CHECK(textureInfo(*texture.texture).srgb == 0);

    const Rgba top = texture.decoded(0);
    double worst = 0, total = 0;
    for (size_t i = 0; i < directions.size() / 3; ++i)
    {
        const float x = top.texels[i * 4] / 255.0f * 2 - 1, y = top.texels[i * 4 + 1] / 255.0f * 2 - 1;
        const float z = std::sqrt(std::max(0.0f, 1 - x * x - y * y)); // as a shader rebuilds it
        const double cosine = x * directions[i * 3] + y * directions[i * 3 + 1] + z * directions[i * 3 + 2];
        const double degrees = std::acos(std::min(1.0, cosine)) * 180.0 / 3.14159265358979;
        worst = std::max(worst, degrees);
        total += degrees;
    }
    MESSAGE("BC5 normals: mean error " << total / double(directions.size() / 3) << " degrees, worst " << worst);
    CHECK(total / double(directions.size() / 3) < 2.0);
    CHECK(worst < 10.0);

    // Every mip stays inside the unit disc, so Z is always real: they were
    // renormalized, not just averaged.
    for (uint32_t mip = 1; mip < textureInfo(*texture.texture).mipCount; ++mip)
    {
        const Rgba level = texture.decoded(mip);
        for (size_t i = 0; i < level.texels.size(); i += 4)
        {
            const float x = level.texels[i] / 255.0f * 2 - 1, y = level.texels[i + 1] / 255.0f * 2 - 1;
            CHECK(x * x + y * y <= 1.02f);
        }
    }
}

TEST_CASE("BC4 keeps a single channel")
{
    Rgba height(128, 128);
    for (uint32_t y = 0; y < 128; ++y)
    {
        for (uint32_t x = 0; x < 128; ++x)
        {
            uint8_t* t = height.at(x, y);
            t[0] = uint8_t(127.5 + 127.5 * std::sin(x * 0.1) * std::cos(y * 0.07));
            t[3] = 255;
        }
    }
    const Imported texture(png(height), settingsFor(TextureUsage::SingleChannel));
    CHECK(textureInfo(*texture.texture).format == TextureFormat::Bc4);
    const double quality = psnr(height, texture.decoded(0), 1);
    MESSAGE("BC4 PSNR " << quality << " dB");
    CHECK(quality > 40.0);
}

TEST_CASE("Alpha coverage is kept down the chain when asked")
{
    // Leaves: opaque on 30% of texels, transparent elsewhere. Plain
    // averaging makes every mip about 0.3 alpha, under the cutoff: the
    // foliage would vanish with distance.
    Rgba leaves(64, 64);
    std::mt19937 random(5);
    for (size_t i = 0; i < leaves.texels.size(); i += 4)
    {
        leaves.texels[i] = 40;
        leaves.texels[i + 1] = 160;
        leaves.texels[i + 2] = 40;
        leaves.texels[i + 3] = random() % 10 < 3 ? 255 : 0;
    }
    TextureSettings settings = settingsFor(TextureUsage::Ui);
    const Imported plain(png(leaves), settings);
    settings.preserveAlphaCoverage = true;
    const Imported kept(png(leaves), settings);

    const double target = alphaCoverage(leaves);
    for (uint32_t mip = 1; mip <= 3; ++mip)
    {
        CAPTURE(mip);
        CHECK(std::abs(alphaCoverage(kept.decoded(mip)) - target) < 0.04);
    }
    // Unprotected, coverage goes 0.29, 0.16, 0.03, 0: the leaves are gone.
    CHECK(alphaCoverage(plain.decoded(1)) < target * 0.6);
    CHECK(alphaCoverage(plain.decoded(3)) < 0.01);
}

TEST_CASE("Sizes block formats can't store are refused")
{
    Rgba odd(30, 30);
    std::vector<uint8_t> source = png(odd);
    Result<TextureImport*> refused = importTexture(source, settingsFor(TextureUsage::Color));
    CHECK(refused.status() == Status::InvalidData);
    CHECK(std::string(lastImportError()).find("30x30 isn't a multiple of 4") != std::string::npos);
    // Uncompressed UI textures take any size.
    const Imported ui(source, settingsFor(TextureUsage::Ui));
    CHECK(textureInfo(*ui.texture).width == 30);
}

TEST_CASE("Unreadable data is refused with the decoder's reason")
{
    const std::vector<uint8_t> garbage = {1, 2, 3, 4, 5, 6, 7, 8};
    CHECK(importTexture(garbage, settingsFor(TextureUsage::Color)).status() == Status::InvalidData);
    CHECK(std::string(lastImportError()).find("can't read the image") != std::string::npos);
}

TEST_CASE("HDR images import as half floats")
{
    // A 4x4 Radiance file of the value 2.0: RGBE (128, 128, 128, 130) is
    // 128/256 * 2^(130 - 128). Scanlines this short are stored plainly.
    const std::string header = "#?RADIANCE\nFORMAT=32-bit_rle_rgbe\n\n-Y 4 +X 4\n";
    std::vector<uint8_t> hdr(header.begin(), header.end());
    for (int i = 0; i < 16; ++i)
    {
        hdr.insert(hdr.end(), {128, 128, 128, 130});
    }
    CHECK(importTexture(hdr, settingsFor(TextureUsage::Color)).status() == Status::InvalidData);
    CHECK(std::string(lastImportError()).find("usage Hdr") != std::string::npos);

    const Imported texture(hdr, settingsFor(TextureUsage::Hdr));
    CHECK(textureInfo(*texture.texture).format == TextureFormat::Rgba16F);
    const std::vector<uint8_t> top = texture.encoded(0);
    REQUIRE(top.size() == 16u * 8);
    uint16_t half[4];
    std::memcpy(half, top.data(), 8);
    CHECK(half[0] == 0x4000); // 2.0
    CHECK(half[3] == 0x3C00); // 1.0
}

TEST_CASE("The same source gives the same bytes with any number of workers")
{
    const std::vector<uint8_t> source = png(scene(128));
    TextureSettings settings = settingsFor(TextureUsage::Color);
    settings.rdoLambda = 2.0f;
    std::vector<uint8_t> inline_, parallel;
    {
        const Imported texture(source, settings); // no workers: everything inline
        inline_ = texture.encoded(0);
    }
    REQUIRE(jobs::initialize(3));
    {
        const Imported texture(source, settings);
        parallel = texture.encoded(0);
    }
    jobs::shutdown();
    CHECK(inline_ == parallel);
}

TEST_CASE("Importing returns its memory")
{
    const MemoryUsage before = memoryUsage(MemoryTag::Assets);
    {
        const Imported texture(png(scene(64)), settingsFor(TextureUsage::Color));
    }
    const MemoryUsage after = memoryUsage(MemoryTag::Assets);
    CHECK(after.bytes == before.bytes);
    CHECK(after.allocations == before.allocations);
}
