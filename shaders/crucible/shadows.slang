// Crucible's shadow lookups (ADR 0054): the sun's cascades and the shadow
// atlas, filtered with a 5x5 tent (Castaño's optimized PCF: nine bilinear
// comparisons), which gives a soft edge about two texels wide without the
// noise random samples would need a temporal filter to hide.

import crucible.shared;

// The renderer's comparison sampler (ADR 0049's samplers, then this): linear,
// clamped, LessEqual against the stored depth.
public static const uint shadowComparisonSampler = 4;
// Texels the filter reaches from its center: lookups stay this far inside a
// view (the views are widened by more).
static const float filterReach = 3.5;
// Normal offset, in texels: a little always, more as the surface turns away
// from the light.
static const float normalOffsetTexels = 0.5;
static const float normalOffsetGrazing = 2.0;

// The nine taps' positions (in texels from the snapped base) and weights.
struct TentTaps
{
    float2 base;  // UV
    float3 u, v;  // offsets in texels
    float3 uw, vw; // weights, out of 144 together
}

TentTaps tentTaps(float2 uv, float size)
{
    const float2 texel = uv * size;
    float2 base = floor(texel + 0.5);
    const float s = texel.x + 0.5 - base.x;
    const float t = texel.y + 0.5 - base.y;
    TentTaps taps;
    taps.base = (base - 0.5) / size;
    taps.uw = float3(4.0 - 3.0 * s, 7.0, 1.0 + 3.0 * s);
    taps.u = float3((3.0 - 2.0 * s) / taps.uw.x - 2.0, (3.0 + s) / taps.uw.y, s / taps.uw.z + 2.0);
    taps.vw = float3(4.0 - 3.0 * t, 7.0, 1.0 + 3.0 * t);
    taps.v = float3((3.0 - 2.0 * t) / taps.vw.x - 2.0, (3.0 + t) / taps.vw.y, t / taps.vw.z + 2.0);
    return taps;
}

// Where a world position falls in a view: UV in its map (kept inside the
// view by the filter's reach) and depth.
float3 project(ShadowView shadow, float3 position)
{
    const float4 clip = mul(float4(position, 1.0), shadow.viewProjection);
    const float3 ndc = clip.xyz / clip.w;
    float2 uv = ndc.xy * float2(0.5, -0.5) + 0.5;
    uv = shadow.rect.xy + uv * shadow.rect.zw;
    const float2 inset = filterReach * shadow.texelSize;
    uv = clamp(uv, shadow.rect.xy + inset, shadow.rect.xy + shadow.rect.zw - inset);
    return float3(uv, saturate(ndc.z));
}

float filterCascade(Texture2DArray<float> map, float3 lookup, ShadowView shadow)
{
    SamplerComparisonState s = SamplerDescriptorHeap[shadowComparisonSampler];
    const TentTaps taps = tentTaps(lookup.xy, 1.0 / shadow.texelSize);
    float sum = 0.0;
    [unroll] for (int i = 0; i < 3; ++i)
    {
        [unroll] for (int j = 0; j < 3; ++j)
        {
            const float2 uv = taps.base + float2(taps.u[i], taps.v[j]) * shadow.texelSize;
            sum += taps.uw[i] * taps.vw[j] * map.SampleCmpLevelZero(s, float3(uv, float(shadow.layer)), lookup.z);
        }
    }
    return sum / 144.0;
}

float filterAtlas(Texture2D<float> map, float3 lookup, ShadowView shadow)
{
    SamplerComparisonState s = SamplerDescriptorHeap[shadowComparisonSampler];
    const TentTaps taps = tentTaps(lookup.xy, 1.0 / shadow.texelSize);
    float sum = 0.0;
    [unroll] for (int i = 0; i < 3; ++i)
    {
        [unroll] for (int j = 0; j < 3; ++j)
        {
            const float2 uv = taps.base + float2(taps.u[i], taps.v[j]) * shadow.texelSize;
            sum += taps.uw[i] * taps.vw[j] * map.SampleCmpLevelZero(s, uv, lookup.z);
        }
    }
    return sum / 144.0;
}

// The position pushed off the surface by about a texel, so a surface
// doesn't shadow itself.
float3 offsetPosition(float3 position, float3 normal, float3 toLight, float texelWidth)
{
    const float grazing = 1.0 - saturate(dot(normal, toLight));
    return position + normal * texelWidth * (normalOffsetTexels + normalOffsetGrazing * grazing);
}

float cascadeShadow(Texture2DArray<float> map, StructuredBuffer<ShadowView> views, uint cascade, float3 position,
                    float3 normal, float3 toLight)
{
    const ShadowView shadow = views[cascade];
    const float3 lookup = project(shadow, offsetPosition(position, normal, toLight, shadow.normalOffset));
    return filterCascade(map, lookup, shadow);
}

// How much of the sun reaches a position (1 lit, 0 shadowed): its cascade
// by view depth, blended into the next over each cascade's last tenth, and
// faded out at the last one's end. The cascades are views 0 to 3.
public float sunShadow(float3 position, float3 normal, float3 toLight, float viewDepth, float4 splits, uint views,
                       uint map)
{
    if (map == 0)
    {
        return 1.0;
    }
    uint cascade = 0;
    while (cascade < 4 && viewDepth > splits[cascade])
    {
        ++cascade;
    }
    if (cascade >= 4)
    {
        return 1.0;
    }
    Texture2DArray<float> cascades = ResourceDescriptorHeap[map];
    StructuredBuffer<ShadowView> shadowViews = ResourceDescriptorHeap[views];
    float shadow = cascadeShadow(cascades, shadowViews, cascade, position, normal, toLight);
    const float start = cascade == 0 ? 0.0 : splits[cascade - 1];
    const float band = (splits[cascade] - start) * 0.1;
    const float blend = saturate((viewDepth - (splits[cascade] - band)) / band);
    if (blend > 0.0)
    {
        const float next =
            cascade + 1 < 4 ? cascadeShadow(cascades, shadowViews, cascade + 1, position, normal, toLight) : 1.0;
        shadow = lerp(shadow, next, blend);
    }
    return shadow;
}

// How much of a point or spot light reaches a position: its tile in the
// atlas, for a point light the cube face the position is in.
public float localShadow(LightData light, float3 position, float3 normal, uint views, uint atlas)
{
    if (light.shadowView == noShadowView || atlas == 0)
    {
        return 1.0;
    }
    uint index = light.shadowView;
    const float3 fromLight = position - light.position;
    if (light.spotScale == 0.0)
    {
        // Faces +X, -X, +Y, -Y, +Z, -Z.
        const float3 a = abs(fromLight);
        const uint face = a.x >= a.y && a.x >= a.z ? (fromLight.x > 0.0 ? 0 : 1)
                          : a.y >= a.z             ? (fromLight.y > 0.0 ? 2 : 3)
                                                   : (fromLight.z > 0.0 ? 4 : 5);
        index += face;
    }
    StructuredBuffer<ShadowView> shadowViews = ResourceDescriptorHeap[views];
    const ShadowView shadow = shadowViews[index];
    Texture2D<float> map = ResourceDescriptorHeap[atlas];
    const float distance = length(fromLight);
    const float3 toLight = -fromLight / max(distance, 1e-4);
    const float texelWidth = shadow.normalOffset + shadow.normalOffsetPerMeter * distance;
    return filterAtlas(map, project(shadow, offsetPosition(position, normal, toLight, texelWidth)), shadow);
}
