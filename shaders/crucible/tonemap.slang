// The Tonemap step's shader (ADR 0015, 0055): each sample of SceneColor
// exposed, mixed with the bloom, white-balanced, tonemapped, and graded,
// then the samples averaged into the backbuffer, whose sRGB view encodes
// them. Tonemapping each sample before averaging keeps MSAA's edges: an
// average of HDR samples would let the brightest sample win.

import crucible.shared;

ConstantBuffer<ViewConstants> view : register(b1);

struct TonemapConstants
{
    uint source;  // SceneColor's slot: Texture2DMS, or Texture2D at one sample
    uint samples;
    uint bloom;   // the bloom's slot (Texture2D, half size), when bloomLevels > 0
};
[[vk::push_constant]] ConstantBuffer<TonemapConstants> tonemap;

static const uint linearClampSampler = 2;

// One triangle covering the screen.
[shader("vertex")]
float4 fullscreenVertex(uint vertex : SV_VertexID) : SV_Position
{
    const float2 corner = float2((vertex << 1) & 2, vertex & 2);
    return float4(corner * float2(2.0, -2.0) + float2(-1.0, 1.0), 0.0, 1.0);
}

// Khronos PBR Neutral: colors pass through unchanged until near white, then
// compress, desaturating a little.
float3 pbrNeutral(float3 color)
{
    const float startCompression = 0.8 - 0.04;
    const float desaturation = 0.15;
    const float x = min(color.r, min(color.g, color.b));
    const float offset = x < 0.08 ? x - 6.25 * x * x : 0.04;
    color -= offset;
    const float peak = max(color.r, max(color.g, color.b));
    if (peak < startCompression)
    {
        return color;
    }
    const float d = 1.0 - startCompression;
    const float newPeak = 1.0 - d * d / (peak + d - startCompression);
    color *= newPeak / peak;
    const float g = 1.0 - 1.0 / (desaturation * (peak - newPeak) + 1.0);
    return lerp(color, float3(newPeak, newPeak, newPeak), g);
}

// AgX (Sobotka), its default look, in Wrensch's polynomial fit.
float3 agx(float3 color)
{
    const float3x3 inset = float3x3(0.842479062253094, 0.0423282422610123, 0.0423756549057051,
                                    0.0784335999999992, 0.878468636469772, 0.0784336,
                                    0.0792237451477643, 0.0791661274605434, 0.879142973793104);
    const float3x3 outset = float3x3(1.19687900512017, -0.0528968517574562, -0.0529716355144438,
                                     -0.0980208811401368, 1.15190312990417, -0.0980434501171241,
                                     -0.0990297440797205, -0.0989611768448433, 1.15107367264116);
    const float minEv = -12.47393;
    const float maxEv = 4.026069;
    float3 x = mul(max(color, 1e-10), inset);
    x = (clamp(log2(x), minEv, maxEv) - minEv) / (maxEv - minEv);
    const float3 x2 = x * x;
    const float3 x4 = x2 * x2;
    x = 15.5 * x4 * x2 - 40.14 * x4 * x + 31.96 * x4 - 6.868 * x2 * x + 0.4298 * x2 + 0.1191 * x - 0.00232;
    return pow(max(mul(x, outset), 0.0), 2.2); // the curve's display encoding, back to linear
}

// ACES (the reference rendering and output transforms), Hill's fit.
float3 aces(float3 color)
{
    const float3x3 input = float3x3(0.59719, 0.35458, 0.04823, 0.07600, 0.90834, 0.01566, 0.02840, 0.13383, 0.83777);
    const float3x3 output =
        float3x3(1.60475, -0.53108, -0.07367, -0.10208, 1.10813, -0.00605, -0.00327, -0.07276, 1.07602);
    const float3 v = mul(input, color);
    const float3 a = v * (v + 0.0245786) - 0.000090537;
    const float3 b = v * (0.983729 * v + 0.4329510) + 0.238081;
    return mul(output, a / b);
}

float3 finish(float3 color, float3 bloom)
{
    color = max(color, 0.0) * view.exposureScale;
    color = lerp(color, bloom, view.bloomStrength);
    color = max(mul(float4(color, 0.0), view.whiteBalance).rgb, 0.0);
    color = view.tonemapper == 1 ? agx(color) : view.tonemapper == 2 ? aces(color) : pbrNeutral(color);
    color = saturate(color);
    // Grading, display-referred: lift and gain, gamma, contrast around
    // mid-gray, saturation around luminance.
    color = saturate(color * (1.0 + view.gain) + view.lift * (1.0 - color));
    color = pow(color, 1.0 / (1.0 + view.gamma));
    color = 0.18 * pow(color / 0.18, 1.0 + view.contrast);
    const float luminance = dot(color, float3(0.2126, 0.7152, 0.0722));
    color = lerp(float3(luminance, luminance, luminance), color, 1.0 + view.saturation);
    return saturate(color);
}

[shader("fragment")]
float4 tonemapPixel(float4 position : SV_Position) : SV_Target
{
    const int2 pixel = int2(position.xy);
    float3 bloom = float3(0.0, 0.0, 0.0);
    if (view.bloomLevels > 0)
    {
        Texture2D<float4> chain = ResourceDescriptorHeap[tonemap.bloom];
        SamplerState s = SamplerDescriptorHeap[linearClampSampler];
        bloom = chain.SampleLevel(s, position.xy / view.viewportSize, 0.0).rgb / view.bloomLevels;
    }
    if (tonemap.samples <= 1)
    {
        Texture2D<float4> single = ResourceDescriptorHeap[tonemap.source];
        return float4(finish(single.Load(int3(pixel, 0)).rgb, bloom), 1.0);
    }
    Texture2DMS<float4> scene = ResourceDescriptorHeap[tonemap.source];
    float3 sum = float3(0.0, 0.0, 0.0);
    for (uint i = 0; i < tonemap.samples; ++i)
    {
        sum += finish(scene.Load(pixel, i).rgb, bloom);
    }
    return float4(sum / tonemap.samples, 1.0);
}
