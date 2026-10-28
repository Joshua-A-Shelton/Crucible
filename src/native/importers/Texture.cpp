#include <crucible/importers/Texture.h>

#include <crucible/Jobs.h>
#include <crucible/Memory.h>
#include <crucible/Vector.h>

#include <bc7decomp.h>
#include <bc7enc.h>
#include <ert.h>
#include <rgbcx.h>
#include <stb_image.h>

#include <algorithm>
#include <cmath>
#include <cstdarg>
#include <cstdio>
#include <cstring>
#include <mutex>
#include <numbers>

namespace crucible::importers {

struct TextureImport
{
    TextureInfo info{};
    // Every mip's encoded bytes, back to back, and where each starts.
    Vector<uint8_t> bytes{MemoryTag::Assets};
    Vector<uint64_t> offsets{MemoryTag::Assets};
};

namespace {

thread_local char lastError[512];

Status fail(const char* format, ...)
{
    va_list arguments;
    va_start(arguments, format);
    std::vsnprintf(lastError, sizeof(lastError), format, arguments);
    va_end(arguments);
    return Status::InvalidData;
}

struct Float4
{
    float r, g, b, a;
};

// A working image: linear floats, in the form mipmaps are filtered in.
struct Image
{
    uint32_t width = 0;
    uint32_t height = 0;
    Vector<Float4> texels{MemoryTag::Assets};

    void resize(uint32_t newWidth, uint32_t newHeight)
    {
        width = newWidth;
        height = newHeight;
        texels.resize(size_t{newWidth} * newHeight);
    }

    Float4& at(uint32_t x, uint32_t y)
    {
        return texels[size_t{y} * width + x];
    }

    const Float4& at(uint32_t x, uint32_t y) const
    {
        return texels[size_t{y} * width + x];
    }
};

// ---------------------------------------------------------------------------
// Colour spaces and numbers
// ---------------------------------------------------------------------------

float srgbToLinear(float c)
{
    return c <= 0.04045f ? c / 12.92f : std::pow((c + 0.055f) / 1.055f, 2.4f);
}

float linearToSrgb(float c)
{
    return c <= 0.0031308f ? c * 12.92f : 1.055f * std::pow(c, 1.0f / 2.4f) - 0.055f;
}

uint8_t toByte(float value)
{
    return static_cast<uint8_t>(std::lround(std::clamp(value, 0.0f, 1.0f) * 255.0f));
}

// IEEE half precision, rounded to nearest even; values past its range
// become infinity, as the GPU's own conversion does.
uint16_t toHalf(float value)
{
    uint32_t bits;
    std::memcpy(&bits, &value, 4);
    const uint32_t sign = (bits >> 16) & 0x8000u;
    const int32_t exponent = static_cast<int32_t>((bits >> 23) & 0xFF) - 127 + 15;
    uint32_t mantissa = bits & 0x7FFFFFu;
    if (((bits >> 23) & 0xFF) == 0xFF)
    {
        return static_cast<uint16_t>(sign | 0x7C00u | (mantissa != 0 ? 0x200u : 0u)); // inf or NaN
    }
    if (exponent >= 31)
    {
        return static_cast<uint16_t>(sign | 0x7C00u);
    }
    if (exponent <= 0)
    {
        if (exponent < -10)
        {
            return static_cast<uint16_t>(sign);
        }
        mantissa |= 0x800000u;
        const uint32_t shift = static_cast<uint32_t>(14 - exponent);
        uint32_t half = mantissa >> shift;
        const uint32_t remainder = mantissa & ((1u << shift) - 1);
        const uint32_t halfway = 1u << (shift - 1);
        if (remainder > halfway || (remainder == halfway && (half & 1u) != 0))
        {
            ++half;
        }
        return static_cast<uint16_t>(sign | half);
    }
    uint32_t half = (static_cast<uint32_t>(exponent) << 10) | (mantissa >> 13);
    const uint32_t remainder = mantissa & 0x1FFFu;
    if (remainder > 0x1000u || (remainder == 0x1000u && (half & 1u) != 0))
    {
        ++half; // may carry into the exponent, which is right
    }
    return static_cast<uint16_t>(sign | half);
}

// ---------------------------------------------------------------------------
// The mipmap filter: a Kaiser-windowed sinc (ADR 0004, 0042)
// ---------------------------------------------------------------------------

// Its reach, in destination texels, and the window's shape: NVIDIA's
// texture tools' defaults. Larger beta is softer, with less ringing.
constexpr float kernelRadius = 3.0f;
constexpr float kaiserBeta = 4.0f;

// The modified Bessel function of the first kind, order 0, by its series.
float besselI0(float x)
{
    float sum = 1.0f;
    float term = 1.0f;
    const float quarterSquare = x * x / 4.0f;
    for (int k = 1; k < 32; ++k)
    {
        term *= quarterSquare / static_cast<float>(k * k);
        sum += term;
        if (term < sum * 1e-8f)
        {
            break;
        }
    }
    return sum;
}

float kernel(float t)
{
    if (std::fabs(t) >= kernelRadius)
    {
        return 0.0f;
    }
    const float x = std::numbers::pi_v<float> * t;
    const float sinc = t == 0.0f ? 1.0f : std::sin(x) / x;
    const float ratio = t / kernelRadius;
    return sinc * besselI0(kaiserBeta * std::sqrt(1.0f - ratio * ratio)) / besselI0(kaiserBeta);
}

// One destination texel's taps along an axis.
struct Taps
{
    uint32_t first; // index into the weights and sources
    uint32_t count;
};

struct AxisKernel
{
    Vector<Taps> taps{MemoryTag::Assets};
    Vector<uint32_t> sources{MemoryTag::Assets};
    Vector<float> weights{MemoryTag::Assets};
};

AxisKernel makeKernel(uint32_t from, uint32_t to, bool wrap)
{
    AxisKernel axis;
    const float scale = static_cast<float>(from) / static_cast<float>(to);
    for (uint32_t i = 0; i < to; ++i)
    {
        const float center = (static_cast<float>(i) + 0.5f) * scale;
        const int32_t begin = static_cast<int32_t>(std::floor(center - kernelRadius * scale - 0.5f));
        const int32_t end = static_cast<int32_t>(std::ceil(center + kernelRadius * scale - 0.5f));
        const uint32_t first = static_cast<uint32_t>(axis.weights.size());
        float total = 0.0f;
        for (int32_t j = begin; j <= end; ++j)
        {
            const float weight = kernel((static_cast<float>(j) + 0.5f - center) / scale);
            if (weight == 0.0f)
            {
                continue;
            }
            const int32_t size = static_cast<int32_t>(from);
            const int32_t index = wrap ? ((j % size) + size) % size : std::clamp(j, 0, size - 1);
            axis.sources.pushBack(static_cast<uint32_t>(index));
            axis.weights.pushBack(weight);
            total += weight;
        }
        for (uint32_t k = first; k < axis.weights.size(); ++k)
        {
            axis.weights[k] /= total; // flat areas stay flat
        }
        axis.taps.pushBack({first, static_cast<uint32_t>(axis.weights.size()) - first});
    }
    return axis;
}

struct FilterPass
{
    const Image* source;
    Image* destination;
    const AxisKernel* axis;
    bool horizontal;
};

void filterRows(void* data, uint32_t begin, uint32_t end)
{
    const FilterPass& pass = *static_cast<const FilterPass*>(data);
    for (uint32_t y = begin; y < end; ++y)
    {
        for (uint32_t x = 0; x < pass.destination->width; ++x)
        {
            const Taps taps = pass.axis->taps[pass.horizontal ? x : y];
            Float4 sum{0, 0, 0, 0};
            for (uint32_t k = 0; k < taps.count; ++k)
            {
                const uint32_t source = pass.axis->sources[taps.first + k];
                const float weight = pass.axis->weights[taps.first + k];
                const Float4& texel = pass.horizontal ? pass.source->at(source, y) : pass.source->at(x, source);
                sum.r += texel.r * weight;
                sum.g += texel.g * weight;
                sum.b += texel.b * weight;
                sum.a += texel.a * weight;
            }
            pass.destination->at(x, y) = sum;
        }
    }
}

// The next mip: half the size (at least 1), filtered across then down.
Image downsample(const Image& source, bool wrap)
{
    const uint32_t width = std::max(1u, source.width / 2);
    const uint32_t height = std::max(1u, source.height / 2);
    const AxisKernel across = makeKernel(source.width, width, wrap);
    const AxisKernel down = makeKernel(source.height, height, wrap);
    Image middle;
    middle.resize(width, source.height);
    FilterPass first{&source, &middle, &across, true};
    jobs::parallelFor(middle.height, 0, filterRows, &first);
    Image result;
    result.resize(width, height);
    FilterPass second{&middle, &result, &down, false};
    jobs::parallelFor(result.height, 0, filterRows, &second);
    return result;
}

// ---------------------------------------------------------------------------
// From filter space to output texels
// ---------------------------------------------------------------------------

bool isColor(TextureUsage usage)
{
    return usage == TextureUsage::Color || usage == TextureUsage::Ui;
}

// The share of texels whose alpha, scaled, reaches the cutoff.
float coverage(const Image& image, float cutoff, float scale)
{
    size_t covered = 0;
    for (const Float4& texel : image.texels)
    {
        covered += std::clamp(texel.a * scale, 0.0f, 1.0f) >= cutoff ? 1 : 0;
    }
    return static_cast<float>(covered) / static_cast<float>(image.texels.size());
}

// The alpha scale that brings the mip's coverage closest to `target`: a
// mip's averaged alpha otherwise falls below the cutoff ever more often, and
// foliage thins out with distance.
float coverageScale(const Image& image, float cutoff, float target)
{
    float low = 0.0f;
    float high = 16.0f;
    for (int step = 0; step < 24; ++step)
    {
        const float middle = (low + high) / 2.0f;
        if (coverage(image, cutoff, middle) < target)
        {
            low = middle;
        }
        else
        {
            high = middle;
        }
    }
    return high;
}

// Filter space back to the values the format stores: straight alpha for
// colour, normals unit length and in [0, 1], ringing clamped.
void toOutput(const Image& filtered, const TextureSettings& settings, float alphaScale, Image& out)
{
    out.resize(filtered.width, filtered.height);
    for (size_t i = 0; i < filtered.texels.size(); ++i)
    {
        Float4 texel = filtered.texels[i];
        switch (settings.usage)
        {
        case TextureUsage::Color:
        case TextureUsage::Ui:
        {
            const float alpha = std::clamp(texel.a, 0.0f, 1.0f);
            const float divide = alpha > 1e-6f ? 1.0f / alpha : 0.0f;
            texel = {linearToSrgb(std::clamp(texel.r * divide, 0.0f, 1.0f)),
                     linearToSrgb(std::clamp(texel.g * divide, 0.0f, 1.0f)),
                     linearToSrgb(std::clamp(texel.b * divide, 0.0f, 1.0f)),
                     std::clamp(texel.a * alphaScale, 0.0f, 1.0f)};
            break;
        }
        case TextureUsage::NormalMap:
        {
            const float length = std::sqrt(texel.r * texel.r + texel.g * texel.g + texel.b * texel.b);
            const float divide = length > 1e-6f ? 1.0f / length : 0.0f;
            texel = {texel.r * divide * 0.5f + 0.5f, texel.g * divide * 0.5f + 0.5f, texel.b * divide * 0.5f + 0.5f,
                     1.0f};
            break;
        }
        case TextureUsage::Hdr:
            texel = {std::max(texel.r, 0.0f), std::max(texel.g, 0.0f), std::max(texel.b, 0.0f), 1.0f};
            break;
        default:
            texel = {std::clamp(texel.r, 0.0f, 1.0f), std::clamp(texel.g, 0.0f, 1.0f), std::clamp(texel.b, 0.0f, 1.0f),
                     std::clamp(texel.a, 0.0f, 1.0f)};
            break;
        }
        out.texels[i] = texel;
    }
}

// Normals stay unit length down the chain, so each mip filters directions.
void normalize(Image& image)
{
    for (Float4& texel : image.texels)
    {
        const float length = std::sqrt(texel.r * texel.r + texel.g * texel.g + texel.b * texel.b);
        if (length > 1e-6f)
        {
            texel.r /= length;
            texel.g /= length;
            texel.b /= length;
        }
    }
}

// ---------------------------------------------------------------------------
// Encoding
// ---------------------------------------------------------------------------

TextureFormat formatFor(TextureUsage usage)
{
    switch (usage)
    {
    case TextureUsage::NormalMap:
        return TextureFormat::Bc5;
    case TextureUsage::SingleChannel:
        return TextureFormat::Bc4;
    case TextureUsage::Hdr:
        return TextureFormat::Rgba16F;
    case TextureUsage::Ui:
        return TextureFormat::Rgba8;
    default:
        return TextureFormat::Bc7;
    }
}

bool isBlockCompressed(TextureFormat format)
{
    return format == TextureFormat::Bc7 || format == TextureFormat::Bc5 || format == TextureFormat::Bc4;
}

uint32_t blockBytes(TextureFormat format)
{
    return format == TextureFormat::Bc4 ? 8 : 16;
}

uint64_t encodedSize(TextureFormat format, uint32_t width, uint32_t height)
{
    if (isBlockCompressed(format))
    {
        return uint64_t{(width + 3) / 4} * ((height + 3) / 4) * blockBytes(format);
    }
    return uint64_t{width} * height * (format == TextureFormat::Rgba16F ? 8 : 4);
}

void initializeEncoders()
{
    static std::once_flag once;
    std::call_once(once, [] {
        bc7enc_compress_block_init();
        rgbcx::init();
    });
}

struct BlockJob
{
    const Vector<uint8_t>* texels; // RGBA8, width x height
    uint32_t width;
    uint32_t height;
    uint32_t blocksAcross;
    TextureFormat format;
    const bc7enc_compress_block_params* bc7;
    uint8_t* output;
    ert::color_rgba* blockPixels; // each block's 16 source texels, for RDO
};

void encodeBlockRows(void* data, uint32_t begin, uint32_t end)
{
    const BlockJob& job = *static_cast<const BlockJob*>(data);
    const uint32_t size = blockBytes(job.format);
    for (uint32_t by = begin; by < end; ++by)
    {
        for (uint32_t bx = 0; bx < job.blocksAcross; ++bx)
        {
            // Partial blocks at the edges of small mips repeat the last texels.
            uint8_t pixels[64];
            for (uint32_t y = 0; y < 4; ++y)
            {
                for (uint32_t x = 0; x < 4; ++x)
                {
                    const uint32_t sx = std::min(bx * 4 + x, job.width - 1);
                    const uint32_t sy = std::min(by * 4 + y, job.height - 1);
                    std::memcpy(pixels + (y * 4 + x) * 4, job.texels->data() + (size_t{sy} * job.width + sx) * 4, 4);
                }
            }
            const size_t block = size_t{by} * job.blocksAcross + bx;
            uint8_t* out = job.output + block * size;
            switch (job.format)
            {
            case TextureFormat::Bc7:
                bc7enc_compress_block(out, pixels, job.bc7);
                std::memcpy(job.blockPixels + block * 16, pixels, 64);
                break;
            case TextureFormat::Bc5:
                rgbcx::encode_bc5(out, pixels, 0, 1, 4);
                break;
            default:
                rgbcx::encode_bc4(out, pixels, 4);
                break;
            }
        }
    }
}

bool unpackBc7(const void* block, ert::color_rgba* pixels, uint32_t, void*)
{
    return bc7decomp::unpack_bc7(block, reinterpret_cast<bc7decomp::color_rgba*>(pixels));
}

// RDO runs over fixed ranges of blocks, never one per thread: ERT's choices
// depend on where a range starts, and the same source must give the same
// output on any machine with any number of workers (ADR 0040's cache).
constexpr uint32_t rdoRangeBlocks = 1024;

struct RdoJob
{
    uint8_t* blocks;
    const ert::color_rgba* blockPixels;
    uint32_t blockCount;
    ert::reduce_entropy_params params;
};

void reduceEntropyRanges(void* data, uint32_t begin, uint32_t end)
{
    const RdoJob& job = *static_cast<const RdoJob*>(data);
    for (uint32_t range = begin; range < end; ++range)
    {
        const uint32_t first = range * rdoRangeBlocks;
        const uint32_t count = std::min(rdoRangeBlocks, job.blockCount - first);
        uint32_t modified = 0;
        ert::reduce_entropy(job.blocks + size_t{first} * 16, count, 16, 16, 4, 4, 4,
                            job.blockPixels + size_t{first} * 16, job.params, modified, unpackBc7, nullptr);
    }
}

void encodeMip(const Image& mip, const TextureSettings& settings, TextureFormat format, TextureImport& texture)
{
    const uint64_t size = encodedSize(format, mip.width, mip.height);
    const size_t at = texture.bytes.size();
    texture.offsets.pushBack(at);
    texture.bytes.resize(at + size);
    uint8_t* output = texture.bytes.data() + at;

    if (format == TextureFormat::Rgba16F)
    {
        for (size_t i = 0; i < mip.texels.size(); ++i)
        {
            const uint16_t half[4] = {toHalf(mip.texels[i].r), toHalf(mip.texels[i].g), toHalf(mip.texels[i].b),
                                      toHalf(mip.texels[i].a)};
            std::memcpy(output + i * 8, half, 8);
        }
        return;
    }

    Vector<uint8_t> texels(MemoryTag::Assets);
    texels.resize(mip.texels.size() * 4);
    for (size_t i = 0; i < mip.texels.size(); ++i)
    {
        const Float4& texel = mip.texels[i];
        texels[i * 4 + 0] = toByte(texel.r);
        texels[i * 4 + 1] = toByte(texel.g);
        texels[i * 4 + 2] = toByte(texel.b);
        texels[i * 4 + 3] = toByte(texel.a);
    }
    if (format == TextureFormat::Rgba8)
    {
        std::memcpy(output, texels.data(), texels.size());
        return;
    }

    bc7enc_compress_block_params bc7;
    bc7enc_compress_block_params_init(&bc7);
    if (isColor(settings.usage))
    {
        bc7enc_compress_block_params_init_perceptual_weights(&bc7);
    }
    else
    {
        bc7enc_compress_block_params_init_linear_weights(&bc7);
    }
    bc7.m_uber_level = std::min(settings.quality, static_cast<uint32_t>(BC7ENC_MAX_UBER_LEVEL));

    const uint32_t blocksAcross = (mip.width + 3) / 4;
    const uint32_t blocksDown = (mip.height + 3) / 4;
    const uint32_t blockCount = blocksAcross * blocksDown;
    Vector<ert::color_rgba> blockPixels(MemoryTag::Assets);
    if (format == TextureFormat::Bc7)
    {
        blockPixels.resize(size_t{blockCount} * 16);
    }
    BlockJob job{&texels, mip.width, mip.height, blocksAcross, format, &bc7, output, blockPixels.data()};
    jobs::parallelFor(blocksDown, 0, encodeBlockRows, &job);

    if (format == TextureFormat::Bc7 && settings.rdoLambda > 0.0f)
    {
        RdoJob rdo{output, blockPixels.data(), blockCount, {}};
        rdo.params.m_lambda = settings.rdoLambda;
        rdo.params.m_lookback_window_size = 128;
        rdo.params.m_try_two_matches = true;
        rdo.params.m_allow_relative_movement = false;
        rdo.params.m_skip_zero_mse_blocks = false;
        for (uint32_t& weight : rdo.params.m_color_weights)
        {
            weight = 1;
        }
        jobs::parallelFor((blockCount + rdoRangeBlocks - 1) / rdoRangeBlocks, 1, reduceEntropyRanges, &rdo);
    }
}

// ---------------------------------------------------------------------------
// Decoding the source
// ---------------------------------------------------------------------------

constexpr uint32_t maxSize = 16384;

Status decode(std::span<const uint8_t> source, const TextureSettings& settings, Image& image)
{
    if (source.size() > 0x7FFF'FFFF)
    {
        return fail("the image is larger than 2 GB");
    }
    const auto* bytes = source.data();
    const int length = static_cast<int>(source.size());
    int width = 0, height = 0, channels = 0;
    if (stbi_is_hdr_from_memory(bytes, length))
    {
        if (settings.usage != TextureUsage::Hdr)
        {
            return fail("this is a high-dynamic-range image: import it with usage Hdr");
        }
        float* texels = stbi_loadf_from_memory(bytes, length, &width, &height, &channels, 4);
        if (texels == nullptr)
        {
            return fail("can't read the image: %s", stbi_failure_reason());
        }
        image.resize(static_cast<uint32_t>(width), static_cast<uint32_t>(height));
        std::memcpy(image.texels.data(), texels, image.texels.size() * sizeof(Float4));
        stbi_image_free(texels);
        return Status::Ok;
    }

    // 8 or 16 bits per channel, as values in [0, 1].
    const bool sixteen = stbi_is_16_bit_from_memory(bytes, length) != 0;
    void* texels = sixteen ? static_cast<void*>(stbi_load_16_from_memory(bytes, length, &width, &height, &channels, 4))
                           : static_cast<void*>(stbi_load_from_memory(bytes, length, &width, &height, &channels, 4));
    if (texels == nullptr)
    {
        return fail("can't read the image: %s", stbi_failure_reason());
    }
    image.resize(static_cast<uint32_t>(width), static_cast<uint32_t>(height));
    const float scale = sixteen ? 1.0f / 65535.0f : 1.0f / 255.0f;
    for (size_t i = 0; i < image.texels.size(); ++i)
    {
        float channel[4];
        for (int c = 0; c < 4; ++c)
        {
            channel[c] = sixteen ? static_cast<const uint16_t*>(texels)[i * 4 + c] * scale
                                 : static_cast<const uint8_t*>(texels)[i * 4 + c] * scale;
        }
        image.texels[i] = {channel[0], channel[1], channel[2], channel[3]};
    }
    stbi_image_free(texels);
    return Status::Ok;
}

// Into the space mipmaps are filtered in: linear light with premultiplied
// alpha for colour, directions in [-1, 1] for normals, and as stored for
// data.
void toFilterSpace(Image& image, TextureUsage usage, bool sourceIsHdr)
{
    for (Float4& texel : image.texels)
    {
        switch (usage)
        {
        case TextureUsage::Color:
        case TextureUsage::Ui:
            texel = {srgbToLinear(texel.r) * texel.a, srgbToLinear(texel.g) * texel.a, srgbToLinear(texel.b) * texel.a,
                     texel.a};
            break;
        case TextureUsage::NormalMap:
            texel = {texel.r * 2.0f - 1.0f, texel.g * 2.0f - 1.0f, texel.b * 2.0f - 1.0f, 1.0f};
            break;
        case TextureUsage::Hdr:
            if (!sourceIsHdr)
            {
                texel = {srgbToLinear(texel.r), srgbToLinear(texel.g), srgbToLinear(texel.b), 1.0f};
            }
            texel.a = 1.0f;
            break;
        default:
            break;
        }
    }
}

} // namespace

Result<TextureImport*> importTexture(std::span<const uint8_t> source, TextureSettings settings)
{
    lastError[0] = '\0';
    if (static_cast<uint32_t>(settings.usage) > static_cast<uint32_t>(TextureUsage::Ui))
    {
        return fail("unknown usage %u", static_cast<uint32_t>(settings.usage));
    }
    Image image;
    if (const Status status = decode(source, settings, image); status != Status::Ok)
    {
        return status;
    }
    const TextureFormat format = formatFor(settings.usage);
    if (image.width > maxSize || image.height > maxSize)
    {
        return fail("%ux%u is larger than %u texels on a side", image.width, image.height, maxSize);
    }
    if (isBlockCompressed(format) && (image.width % 4 != 0 || image.height % 4 != 0))
    {
        return fail("%ux%u isn't a multiple of 4: block-compressed formats store whole 4x4 blocks; resize it, or "
                    "import it with usage Ui",
                    image.width, image.height);
    }
    toFilterSpace(image, settings.usage, stbi_is_hdr_from_memory(source.data(), static_cast<int>(source.size())));
    initializeEncoders();

    TextureImport* texture = create<TextureImport>(MemoryTag::Assets);
    uint32_t mipCount = 1;
    if (settings.mipmaps)
    {
        for (uint32_t size = std::max(image.width, image.height); size > 1; size /= 2)
        {
            ++mipCount;
        }
    }
    texture->info = {format, image.width, image.height, mipCount, isColor(settings.usage) ? 1u : 0u};

    // Coverage is measured on the top mip's straight alpha, which filter
    // space keeps in the alpha channel.
    const bool keepCoverage = settings.preserveAlphaCoverage && isColor(settings.usage);
    const float targetCoverage = keepCoverage ? coverage(image, settings.alphaCutoff, 1.0f) : 0.0f;

    Image mip = static_cast<Image&&>(image);
    Image output;
    for (uint32_t level = 0; level < mipCount; ++level)
    {
        if (level > 0)
        {
            mip = downsample(mip, settings.wrap);
            if (settings.usage == TextureUsage::NormalMap)
            {
                normalize(mip);
            }
        }
        const float scale = keepCoverage && level > 0 ? coverageScale(mip, settings.alphaCutoff, targetCoverage) : 1.0f;
        toOutput(mip, settings, scale, output);
        encodeMip(output, settings, format, *texture);
    }
    return texture;
}

void destroyTextureImport(TextureImport* texture)
{
    if (texture != nullptr)
    {
        destroy(MemoryTag::Assets, texture);
    }
}

TextureInfo textureInfo(const TextureImport& texture)
{
    return texture.info;
}

uint64_t textureMipSize(const TextureImport& texture, uint32_t mip)
{
    if (mip >= texture.offsets.size())
    {
        return 0;
    }
    const uint64_t end = mip + 1 < texture.offsets.size() ? texture.offsets[mip + 1] : texture.bytes.size();
    return end - texture.offsets[mip];
}

void copyTextureMip(const TextureImport& texture, uint32_t mip, std::span<uint8_t> destination)
{
    const uint64_t size = textureMipSize(texture, mip);
    if (size == 0)
    {
        return;
    }
    std::memcpy(destination.data(), texture.bytes.data() + texture.offsets[mip],
                std::min<size_t>(size, destination.size()));
}

std::string_view lastImportError()
{
    return lastError;
}

Result<void> decodeTexels(TextureFormat format, uint32_t width, uint32_t height, std::span<const uint8_t> encoded,
                          std::span<uint8_t> rgba)
{
    if (rgba.size() != size_t{width} * height * 4 || encoded.size() != encodedSize(format, width, height) ||
        format == TextureFormat::Rgba16F)
    {
        return Status::InvalidData;
    }
    if (format == TextureFormat::Rgba8)
    {
        std::memcpy(rgba.data(), encoded.data(), rgba.size());
        return {};
    }
    const uint32_t blocksAcross = (width + 3) / 4;
    const uint32_t size = blockBytes(format);
    for (uint32_t by = 0; by < (height + 3) / 4; ++by)
    {
        for (uint32_t bx = 0; bx < blocksAcross; ++bx)
        {
            const uint8_t* block = encoded.data() + (size_t{by} * blocksAcross + bx) * size;
            uint8_t pixels[64] = {};
            switch (format)
            {
            case TextureFormat::Bc7:
                bc7decomp::unpack_bc7(block, reinterpret_cast<bc7decomp::color_rgba*>(pixels));
                break;
            case TextureFormat::Bc5:
                rgbcx::unpack_bc5(block, pixels, 0, 1, 4);
                break;
            default:
                rgbcx::unpack_bc4(block, pixels, 4);
                break;
            }
            if (format != TextureFormat::Bc7)
            {
                for (int i = 0; i < 16; ++i)
                {
                    pixels[i * 4 + 3] = 255;
                }
            }
            for (uint32_t y = 0; y < 4 && by * 4 + y < height; ++y)
            {
                for (uint32_t x = 0; x < 4 && bx * 4 + x < width; ++x)
                {
                    std::memcpy(rgba.data() + (size_t{by * 4 + y} * width + bx * 4 + x) * 4, pixels + (y * 4 + x) * 4,
                                4);
                }
            }
        }
    }
    return {};
}

} // namespace crucible::importers
