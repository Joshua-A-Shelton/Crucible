// The Bloom step's shaders (ADR 0055; Jimenez, "Next Generation Post
// Processing in Call of Duty: Advanced Warfare", 2014): SceneColor brought
// to half size, filtered down a chain of halving levels, then back up, each
// level adding the one below it, tent-filtered. What reaches the top is a
// wide, smooth glow, in one frame.

import crucible.shared;

ConstantBuffer<ViewConstants> view : register(b1);

struct BloomConstants
{
    uint source;        // what's read: SceneColor (prefilter) or one level
    uint samples;       // SceneColor's samples (prefilter)
    float2 sourceTexel; // the size of a texel of what's read, in UVs
};
[[vk::push_constant]] ConstantBuffer<BloomConstants> bloom;

static const uint linearClampSampler = 2;

[shader("vertex")]
float4 bloomVertex(uint vertex : SV_VertexID) : SV_Position
{
    const float2 corner = float2((vertex << 1) & 2, vertex & 2);
    return float4(corner * float2(2.0, -2.0) + float2(-1.0, 1.0), 0.0, 1.0);
}

float luminance(float3 color)
{
    return dot(color, float3(0.2126, 0.7152, 0.0722));
}

float3 scenePixel(int2 pixel)
{
    if (bloom.samples <= 1)
    {
        Texture2D<float4> single = ResourceDescriptorHeap[bloom.source];
        return single.Load(int3(pixel, 0)).rgb;
    }
    Texture2DMS<float4> scene = ResourceDescriptorHeap[bloom.source];
    float3 sum = float3(0.0, 0.0, 0.0);
    for (uint i = 0; i < bloom.samples; ++i)
    {
        sum += scene.Load(pixel, i).rgb;
    }
    return sum / bloom.samples;
}

// Level 0: each texel the four scene pixels under it, exposed, weighted by
// 1 / (1 + luminance) (Karis) so a single very bright pixel can't flicker
// into a big blob; then past the threshold only, with a soft knee.
[shader("fragment")]
float4 bloomPrefilterPixel(float4 position : SV_Position) : SV_Target
{
    const int2 base = int2(position.xy) * 2;
    float3 sum = float3(0.0, 0.0, 0.0);
    float weights = 0.0;
    for (int i = 0; i < 4; ++i)
    {
        const float3 color = max(scenePixel(base + int2(i & 1, i >> 1)), 0.0) * view.exposureScale;
        const float weight = 1.0 / (1.0 + luminance(color));
        sum += color * weight;
        weights += weight;
    }
    float3 color = sum / weights;
    if (view.bloomThreshold > 0.0)
    {
        const float brightness = max(color.r, max(color.g, color.b));
        const float knee = view.bloomThreshold * 0.5;
        float soft = clamp(brightness - view.bloomThreshold + knee, 0.0, 2.0 * knee);
        soft = soft * soft / (4.0 * knee + 1e-4);
        color *= max(soft, brightness - view.bloomThreshold) / max(brightness, 1e-4);
    }
    return float4(color, 1.0);
}

float3 sampleSource(float2 uv)
{
    Texture2D<float4> source = ResourceDescriptorHeap[bloom.source];
    SamplerState s = SamplerDescriptorHeap[linearClampSampler];
    return source.SampleLevel(s, uv, 0.0).rgb;
}

// Down a level: 13 bilinear taps in overlapping boxes, which keeps the
// chain from flickering as things move.
[shader("fragment")]
float4 bloomDownsamplePixel(float4 position : SV_Position) : SV_Target
{
    const float2 uv = position.xy * bloom.sourceTexel * 2.0;
    const float2 t = bloom.sourceTexel;
    const float3 a = sampleSource(uv + t * float2(-2, -2)), b = sampleSource(uv + t * float2(0, -2)),
                 c = sampleSource(uv + t * float2(2, -2)), d = sampleSource(uv + t * float2(-2, 0)),
                 e = sampleSource(uv), f = sampleSource(uv + t * float2(2, 0)),
                 g = sampleSource(uv + t * float2(-2, 2)), h = sampleSource(uv + t * float2(0, 2)),
                 i = sampleSource(uv + t * float2(2, 2)), j = sampleSource(uv + t * float2(-1, -1)),
                 k = sampleSource(uv + t * float2(1, -1)), l = sampleSource(uv + t * float2(-1, 1)),
                 m = sampleSource(uv + t * float2(1, 1));
    const float3 down = e * 0.125 + (a + c + g + i) * 0.03125 + (b + d + f + h) * 0.0625 + (j + k + l + m) * 0.125;
    return float4(down, 1.0);
}

// Up a level: the level below, through a 3x3 tent, added (by blending) to
// this level's own downsample.
[shader("fragment")]
float4 bloomUpsamplePixel(float4 position : SV_Position) : SV_Target
{
    const float2 uv = position.xy * bloom.sourceTexel * 0.5;
    const float2 t = bloom.sourceTexel;
    const float3 up = (sampleSource(uv) * 4.0 +
                       (sampleSource(uv + t * float2(0, -1)) + sampleSource(uv + t * float2(-1, 0)) +
                        sampleSource(uv + t * float2(1, 0)) + sampleSource(uv + t * float2(0, 1))) * 2.0 +
                       sampleSource(uv + t * float2(-1, -1)) + sampleSource(uv + t * float2(1, -1)) +
                       sampleSource(uv + t * float2(-1, 1)) + sampleSource(uv + t * float2(1, 1))) / 16.0;
    return float4(up, 1.0);
}
