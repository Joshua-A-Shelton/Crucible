// The UI step's shader (ADR 0059): every Canvas primitive is one instance
// of UiInstance, a quad drawn in a single instanced draw, in order. Shapes
// are signed distance fields in the quad's local space, so edges, corners,
// borders, lines, and shadows are antialiased at any scale or rotation.
// Colors arrive linear and premultiplied; the target's sRGB view encodes
// them, so blending happens in linear light.

import crucible.shared;

// One Canvas primitive. Local positions are logical units before the
// instance's transform, which maps them to physical pixels (row vectors:
// screen = local.x * transform.xy + local.y * transform.zw + translation).
[GpuShared]
public struct UiInstance
{
    public float4 rect;        // local: x, y, width, height
    public float4 transform;   // m11, m12, m21, m22
    public float2 translation; // physical pixels
    public uint kind;          // UiKind
    public uint texture;       // a resource-heap slot (images, glyph pages)
    public float4 radii;       // corners: top left, top right, bottom right, bottom left
    public float4 color;       // fill, tint, glyph, line or shadow color
    public float4 color2;      // a gradient's end color
    public float4 uv;          // images: uv rect; glyphs: page uv rect; lines: the ends; gradients: geometry
    public float4 extra;       // x: border width, line width or blur sigma; y: gradient kind. Nine-slice: insets
    public float4 extra2;      // rects: border color; nine-slice: source insets in uv
    public float4 clip;        // physical pixels: min x, min y, max x, max y
};

public static const uint uiRect = 0;
public static const uint uiImage = 1;
public static const uint uiNineSlice = 2;
public static const uint uiGlyph = 3;      // exact: coverage, texel for pixel
public static const uint uiFieldGlyph = 4; // MSDF
public static const uint uiLine = 5;
public static const uint uiShadow = 6;

public static const uint uiGradientNone = 0;
public static const uint uiGradientLinear = 1;
public static const uint uiGradientRadial = 2;

struct UiConstants
{
    float2 targetSize; // physical pixels
    uint frameData;    // the frame data's ByteAddressBuffer slot
    uint first;        // the first instance's byte offset in it
};
[[vk::push_constant]] ConstantBuffer<UiConstants> ui;

static const uint linearClampSampler = 2;
static const uint pointClampSampler = 3;
static const float glyphPageSize = 1024.0;
static const float distanceFieldRange = 4.0; // texels; GlyphCache's

struct UiVaryings
{
    float4 position : SV_Position;
    float2 local : LOCAL;
    nointerpolation uint instance : INSTANCE;
};

UiInstance load(uint instance)
{
    ByteAddressBuffer frame = ResourceDescriptorHeap[ui.frameData];
    return frame.Load<UiInstance>(ui.first + instance * 160);
}

// A strip of four corners around the rect, widened so antialiasing (and a
// shadow's blur) has room: one physical pixel, in local units.
[shader("vertex")]
UiVaryings uiVertex(uint vertex : SV_VertexID, uint instance : SV_InstanceID)
{
    const UiInstance item = load(instance);
    const float2 corner = float2(vertex & 1, vertex >> 1);
    const float scale = sqrt(max(abs(item.transform.x * item.transform.w - item.transform.y * item.transform.z), 1e-8));
    float margin = 1.0 / scale;
    if (item.kind == uiShadow)
    {
        margin += 3.0 * item.extra.x;
    }
    else if (item.kind == uiGlyph)
    {
        margin = 0.0; // texel for pixel
    }
    const float2 local = item.rect.xy - margin + corner * (item.rect.zw + 2.0 * margin);
    const float2 screen = local.x * item.transform.xy + local.y * item.transform.zw + item.translation;
    UiVaryings output;
    output.position = float4(screen / ui.targetSize * float2(2, -2) + float2(-1, 1), 0, 1);
    output.local = local;
    output.instance = instance;
    return output;
}

// The distance to a rounded box's edge (Quilez): negative inside.
float roundedBox(float2 p, float2 halfSize, float4 radii)
{
    // radii: top left, top right, bottom right, bottom left; y is down.
    float2 sideRadii = p.x < 0 ? float2(radii.x, radii.w) : float2(radii.y, radii.z);
    const float r = p.y < 0 ? sideRadii.x : sideRadii.y;
    const float2 q = abs(p) - halfSize + r;
    return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - r;
}

float coverageOf(float distance)
{
    const float width = max(fwidth(distance), 1e-5);
    return saturate(0.5 - distance / width);
}

// Text coverage, corrected for blending in linear light: dark text is
// thickened and light text thinned, partway toward how it looks blended in
// gamma space (ADR 0059).
float textCoverage(float coverage, float4 color)
{
    const float3 straight = color.a > 0 ? color.rgb / color.a : color.rgb;
    const float luminance = dot(straight, float3(0.2126, 0.7152, 0.0722));
    return pow(saturate(coverage), lerp(1.0 / 1.45, 1.45, luminance));
}

// Evan Wallace's rounded-box shadow: a Gaussian blur of a rounded box,
// exact along x (erf) and sampled along y.
float2 erf2(float2 x)
{
    const float2 s = sign(x), a = abs(x);
    float2 r = 1.0 + (0.278393 + (0.230389 + 0.078108 * (a * a)) * a) * a;
    r *= r;
    return s - s / (r * r);
}

float shadowX(float x, float y, float sigma, float corner, float2 halfSize)
{
    const float delta = min(halfSize.y - corner - abs(y), 0.0);
    const float curved = halfSize.x - corner + sqrt(max(0.0, corner * corner - delta * delta));
    const float2 integral = 0.5 + 0.5 * erf2((x + float2(-curved, curved)) * (sqrt(0.5) / sigma));
    return integral.y - integral.x;
}

float roundedBoxShadow(float2 p, float2 halfSize, float sigma, float corner)
{
    const float low = p.y - halfSize.y, high = p.y + halfSize.y;
    const float start = clamp(-3.0 * sigma, low, high), end = clamp(3.0 * sigma, low, high);
    const float step = (end - start) / 4.0;
    float y = start + step * 0.5;
    float value = 0.0;
    for (int i = 0; i < 4; ++i)
    {
        const float gaussian = exp(-(y * y) / (2.0 * sigma * sigma)) / (sqrt(2.0 * 3.14159265) * sigma);
        value += shadowX(p.x, p.y - y, sigma, corner, halfSize) * gaussian * step;
        y += step;
    }
    return value;
}

float4 fillColor(UiInstance item, float2 local)
{
    const uint gradient = uint(item.extra.y);
    if (gradient == uiGradientLinear)
    {
        const float2 along = item.uv.zw - item.uv.xy;
        const float t = saturate(dot(local - item.uv.xy, along) / max(dot(along, along), 1e-8));
        return lerp(item.color, item.color2, t);
    }
    if (gradient == uiGradientRadial)
    {
        return lerp(item.color, item.color2, saturate(length(local - item.uv.xy) / max(item.uv.z, 1e-8)));
    }
    return item.color;
}

// One axis of a nine-slice: destination position x in [0, size] to a
// source coordinate, keeping the insets' size.
float nineSlice(float x, float size, float insetLow, float insetHigh, float u0, float u1, float sourceLow,
                float sourceHigh)
{
    if (x < insetLow)
    {
        return u0 + x / max(insetLow, 1e-5) * sourceLow;
    }
    if (x > size - insetHigh)
    {
        return u1 - (size - x) / max(insetHigh, 1e-5) * sourceHigh;
    }
    const float middle = max(size - insetLow - insetHigh, 1e-5);
    return lerp(u0 + sourceLow, u1 - sourceHigh, (x - insetLow) / middle);
}

float median(float3 v)
{
    return max(min(v.r, v.g), min(max(v.r, v.g), v.b));
}

float4 shade(UiInstance item, float2 local)
{
    const float2 halfSize = item.rect.zw * 0.5;
    const float2 centered = local - item.rect.xy - halfSize;
    const float2 unit = (local - item.rect.xy) / max(item.rect.zw, 1e-5);

    switch (item.kind)
    {
    case uiRect: {
        const float distance = roundedBox(centered, halfSize, item.radii);
        const float outer = coverageOf(distance);
        const float border = item.extra.x;
        if (border <= 0)
        {
            return fillColor(item, local) * outer;
        }
        const float inner = coverageOf(distance + border);
        return fillColor(item, local) * inner + item.extra2 * (outer - inner);
    }
    case uiImage: {
        Texture2D texture = ResourceDescriptorHeap[item.texture];
        SamplerState s = SamplerDescriptorHeap[linearClampSampler];
        const float4 texel = texture.Sample(s, lerp(item.uv.xy, item.uv.zw, unit));
        const float alpha = coverageOf(roundedBox(centered, halfSize, item.radii));
        return float4(texel.rgb * texel.a, texel.a) * item.color * alpha;
    }
    case uiNineSlice: {
        Texture2D texture = ResourceDescriptorHeap[item.texture];
        SamplerState s = SamplerDescriptorHeap[linearClampSampler];
        const float2 p = local - item.rect.xy;
        const float2 uv = float2(nineSlice(p.x, item.rect.z, item.extra.x, item.extra.z, item.uv.x, item.uv.z,
                                           item.extra2.x, item.extra2.z),
                                 nineSlice(p.y, item.rect.w, item.extra.y, item.extra.w, item.uv.y, item.uv.w,
                                           item.extra2.y, item.extra2.w));
        const float4 texel = texture.SampleGrad(s, uv, ddx(uv), ddy(uv));
        const float alpha = coverageOf(roundedBox(centered, halfSize, item.radii));
        return float4(texel.rgb * texel.a, texel.a) * item.color * alpha;
    }
    case uiGlyph: {
        Texture2D texture = ResourceDescriptorHeap[item.texture];
        SamplerState s = SamplerDescriptorHeap[pointClampSampler];
        const float coverage = texture.SampleLevel(s, lerp(item.uv.xy, item.uv.zw, unit), 0).r;
        return item.color * textCoverage(coverage, item.color);
    }
    case uiFieldGlyph: {
        Texture2D texture = ResourceDescriptorHeap[item.texture];
        SamplerState s = SamplerDescriptorHeap[linearClampSampler];
        const float2 uv = lerp(item.uv.xy, item.uv.zw, unit);
        const float distance = median(texture.SampleLevel(s, uv, 0).rgb);
        // How many screen pixels the field's range spans here.
        const float2 texelsPerPixel = fwidth(uv) * glyphPageSize;
        const float range = max(distanceFieldRange / max(length(texelsPerPixel) * 0.7071, 1e-5), 1.0);
        const float coverage = saturate((distance - 0.5) * range + 0.5);
        return item.color * textCoverage(coverage, item.color);
    }
    case uiLine: {
        const float2 a = item.uv.xy, b = item.uv.zw;
        const float2 pa = local - a, ba = b - a;
        const float h = saturate(dot(pa, ba) / max(dot(ba, ba), 1e-8));
        return item.color * coverageOf(length(pa - ba * h) - item.extra.x * 0.5);
    }
    case uiShadow: {
        const float sigma = max(item.extra.x, 1e-3);
        return item.color * roundedBoxShadow(centered, halfSize, sigma, min(item.radii.x, min(halfSize.x, halfSize.y)));
    }
    }
    return 0;
}

// Clipped by multiplying, not discarding, so the derivatives the shapes use
// stay defined at the clip's edge.
[shader("pixel")]
float4 uiPixel(UiVaryings input) : SV_Target
{
    const UiInstance item = load(input.instance);
    const float2 pixel = input.position.xy;
    const float inside = all(pixel >= item.clip.xy) && all(pixel < item.clip.zw) ? 1.0 : 0.0;
    return shade(item, input.local) * inside;
}
