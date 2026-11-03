// Crucible's material library (ADR 0025, 0052): what material shaders and
// shading models are written against.
//
// A material is a struct implementing IMaterial: its fields are its
// parameters (read from the material buffer), surface() describes the
// surface at a pixel, and Shading names the model that lights it. A shading
// model implements IShadingModel: the engine's lighting loop calls begin(),
// then light() for each light and ambient() once, then finish() for the
// pixel's color, with the model's own Accumulator carried through. Both are
// specialized at compile time; nothing dispatches at runtime.

import crucible.shared;
import crucible.shadows;

// The frame's view and the draw's constants (ADR 0050, 0051).
public ConstantBuffer<ViewConstants> view : register(b1);
[[vk::push_constant]] public ConstantBuffer<DrawConstants> draw;

// A texture parameter (ADR 0052): its slot in the resource heap, 0 for
// none. The importer tells textures from numbers by this type.
public struct TextureSlot
{
    public uint index;

    // The texture at uv with linear filtering and wrapping, or fallback
    // without a texture.
    public float4 sample(float2 uv, float4 fallback)
    {
        return sampleTexture(index, uv, fallback);
    }
}

// The samplers the renderer makes at start-up (ADR 0049).
public static const uint linearRepeatSampler = 1;
public static const uint linearClampSampler = 2;
public static const uint pointClampSampler = 3;

public float4 sampleTexture(uint texture, float2 uv, float4 fallback)
{
    if (texture == 0)
    {
        return fallback;
    }
    Texture2D<float4> t = ResourceDescriptorHeap[NonUniformResourceIndex(texture)];
    SamplerState s = SamplerDescriptorHeap[linearRepeatSampler];
    return t.Sample(s, uv);
}

// Specular AA (ADR 0004, 0055; Tokuyoshi and Kaplanyan, as in Filament):
// a perceptual roughness widened by how much the normal varies across the
// pixel, so a highlight smaller than a pixel spreads instead of sparkling.
// Call it where derivatives are valid (not inside per-light loops).
public float specularAntialiasing(float3 normal, float roughness)
{
    const float3 du = ddx(normal);
    const float3 dv = ddy(normal);
    const float variance = 0.15 * (dot(du, du) + dot(dv, dv));
    const float kernel = min(2.0 * variance, 0.2);
    const float alpha = roughness * roughness;
    return sqrt(sqrt(saturate(alpha * alpha + kernel)));
}

// ---------------------------------------------------------------------------
// What a material sees, and what it returns
// ---------------------------------------------------------------------------

// The pixel being shaded, in world space.
public struct SurfaceInput
{
    public float3 position;
    public float3 normal;  // the vertex normal, interpolated and normalized
    public float4 tangent; // xyz, and the bitangent's sign in w
    public float2 uv0;
    public float2 uv1;
    public float4 color; // the vertex color, linear
    public float3 view;  // toward the camera, normalized
    public float4 screenPosition; // SV_Position: pixel coordinates and depth
    public float time;            // seconds since the renderer started
    public uint entity;           // the instance's entity index

    // A tangent-space normal (a normal map's, in -1..1) in world space.
    public float3 toWorld(float3 tangentNormal)
    {
        const float3 bitangent = cross(normal, tangent.xyz) * tangent.w;
        return normalize(tangentNormal.x * tangent.xyz + tangentNormal.y * bitangent + tangentNormal.z * normal);
    }
}

// What the surface is at the pixel, for every shading model.
public struct SurfaceData
{
    public float3 albedo;
    public float3 normal; // world space, normalized
    public float roughness;
    public float metallic;
    public float3 emissive;
    public float alpha;
    public float occlusion;

    public static SurfaceData defaults(SurfaceInput input)
    {
        SurfaceData s;
        s.albedo = float3(1.0, 1.0, 1.0);
        s.normal = input.normal;
        s.roughness = 0.5;
        s.metallic = 0.0;
        s.emissive = float3(0.0, 0.0, 0.0);
        s.alpha = 1.0;
        s.occlusion = 1.0;
        return s;
    }
}

// One light reaching the pixel: toward it, its color with distance and cone
// applied, and how much of it shadows let through (ADR 0054: 1 lit, 0
// shadowed). Shadow is kept apart from color so a model can treat a cast
// shadow like a surface turned away (toon bands both alike); a model must
// apply it itself.
public struct LightSample
{
    public float3 direction;
    public float3 color;
    public float shadow;
}

// Light that isn't from a light: a constant ambient until step 7.
public struct AmbientSample
{
    public float3 color;
}

// ---------------------------------------------------------------------------
// The interfaces
// ---------------------------------------------------------------------------

public interface IShadingModel
{
    // The model's own inputs, filled by the material.
    associatedtype Data;
    // Whatever the model carries through the lighting loop.
    associatedtype Accumulator;

    static Accumulator begin(SurfaceData s, Data d, SurfaceInput input);
    static void light(inout Accumulator a, LightSample light);
    static void ambient(inout Accumulator a, AmbientSample ambient);
    // The pixel's color and alpha.
    static float4 finish(Accumulator a, SurfaceData s, Data d, SurfaceInput input);
}

public interface IMaterial
{
    associatedtype Shading : IShadingModel;
    SurfaceData surface(SurfaceInput input);
    Shading.Data shading(SurfaceInput input);
}

// ---------------------------------------------------------------------------
// Vertices: pulled from the model's buffer (ADR 0051)
// ---------------------------------------------------------------------------

static const uint normalsAttribute = 1;
static const uint tangentsAttribute = 2;
static const uint uv0Attribute = 4;
static const uint uv1Attribute = 8;
static const uint colorsAttribute = 16;
static const uint outlineNormalsAttribute = 32;

// The importer's octahedral encoding (ADR 0043), two 16-bit numbers.
float3 octahedral(uint packed)
{
    float2 e = float2(int(packed << 16) >> 16, int(packed) >> 16) / 32767.0;
    float3 v = float3(e, 1.0 - abs(e.x) - abs(e.y));
    if (v.z < 0.0)
    {
        const float2 signs = select(v.xy >= 0.0, float2(1.0, 1.0), float2(-1.0, -1.0));
        v.xy = (1.0 - abs(v.yx)) * signs;
    }
    return normalize(v);
}

// f16tof32 by hand: Slang's SPIR-V for it needs the Int16 and Float16
// capabilities, which Slag doesn't enable.
float halfToFloat(uint h)
{
    const uint sign = (h & 0x8000) << 16;
    const uint exponent = (h >> 10) & 0x1f;
    const uint mantissa = h & 0x3ff;
    if (exponent == 0)
    {
        const float subnormal = float(mantissa) * 5.9604645e-8; // 2^-24
        return sign != 0 ? -subnormal : subnormal;
    }
    if (exponent == 31)
    {
        return asfloat(sign | 0x7f800000 | (mantissa << 13));
    }
    return asfloat(sign | ((exponent + 112) << 23) | (mantissa << 13));
}

float2 loadUv(ByteAddressBuffer model, uint offset, uint vertex, bool floats)
{
    if (floats)
    {
        return asfloat(model.Load2(offset + vertex * 8));
    }
    const uint packed = model.Load(offset + vertex * 4);
    return float2(halfToFloat(packed & 0xffff), halfToFloat(packed >> 16));
}

float3 srgbToLinear(float3 c)
{
    return select(c <= 0.04045, c / 12.92, pow((c + 0.055) / 1.055, 2.4));
}

public InstanceTransform instanceTransform(uint instance)
{
    StructuredBuffer<uint> instances = ResourceDescriptorHeap[draw.instances];
    StructuredBuffer<InstanceTransform> transforms = ResourceDescriptorHeap[draw.transforms];
    return transforms[instances[instance]];
}

public uint instanceEntity(uint instance)
{
    StructuredBuffer<uint> instances = ResourceDescriptorHeap[draw.instances];
    return instances[instance];
}

public float3 worldPosition(uint vertex, InstanceTransform transform)
{
    ByteAddressBuffer model = ResourceDescriptorHeap[draw.model];
    const float4 position = float4(asfloat(model.Load3(draw.positions + vertex * 12)), 1.0);
    return float3(dot(position, transform.x), dot(position, transform.y), dot(position, transform.z));
}

// A normal in world space: through the cofactor of the world matrix's 3x3,
// which keeps it perpendicular under non-uniform scale.
public float3 worldNormal(float3 normal, InstanceTransform transform)
{
    const float3 row0 = float3(transform.x.x, transform.y.x, transform.z.x);
    const float3 row1 = float3(transform.x.y, transform.y.y, transform.z.y);
    const float3 row2 = float3(transform.x.z, transform.y.z, transform.z.z);
    return normalize(normal.x * cross(row1, row2) + normal.y * cross(row2, row0) + normal.z * cross(row0, row1));
}

// What the material vertex shader hands the pixel shader.
public struct MaterialVertex
{
    public float4 position : SV_Position;
    public float3 world : WORLD;
    public float3 normal : NORMAL;
    public float4 tangent : TANGENT;
    public float2 uv0 : TEXCOORD0;
    public float2 uv1 : TEXCOORD1;
    public float4 color : COLOR;
    public nointerpolation uint entity : ENTITY;
}

// Every stream of a vertex, in world space.
public MaterialVertex materialVertexMain(uint vertex, uint instance)
{
    ByteAddressBuffer model = ResourceDescriptorHeap[draw.model];
    const InstanceTransform transform = instanceTransform(instance);
    MaterialVertex output;
    output.world = worldPosition(vertex, transform);
    output.position = mul(float4(output.world, 1.0), view.viewProjection);
    output.entity = instanceEntity(instance);

    const float3 row0 = float3(transform.x.x, transform.y.x, transform.z.x);
    const float3 row1 = float3(transform.x.y, transform.y.y, transform.z.y);
    const float3 row2 = float3(transform.x.z, transform.y.z, transform.z.z);
    float3 normal = float3(0.0, 1.0, 0.0);
    if ((draw.attributes & normalsAttribute) != 0)
    {
        normal = octahedral(model.Load(draw.normals + vertex * 4));
    }
    output.normal = worldNormal(normal, transform);
    output.tangent = float4(1.0, 0.0, 0.0, 1.0);
    if ((draw.attributes & tangentsAttribute) != 0)
    {
        const uint2 packed = model.Load2(draw.tangents + vertex * 8);
        const float3 tangent = octahedral(packed.x);
        const float sign = (int(packed.y << 16) >> 16) < 0 ? -1.0 : 1.0;
        output.tangent = float4(normalize(tangent.x * row0 + tangent.y * row1 + tangent.z * row2), sign);
    }
    output.uv0 = (draw.attributes & uv0Attribute) != 0 ? loadUv(model, draw.uv0, vertex, (draw.floatUvs & 1) != 0)
                                                       : float2(0.0, 0.0);
    output.uv1 = (draw.attributes & uv1Attribute) != 0 ? loadUv(model, draw.uv1, vertex, (draw.floatUvs & 2) != 0)
                                                       : float2(0.0, 0.0);
    output.color = float4(1.0, 1.0, 1.0, 1.0);
    if ((draw.attributes & colorsAttribute) != 0)
    {
        const uint packed = model.Load(draw.colors + vertex * 4);
        const float4 color = float4(packed & 0xff, (packed >> 8) & 0xff, (packed >> 16) & 0xff, packed >> 24) / 255.0;
        output.color = float4(srgbToLinear(color.rgb), color.a);
    }
    return output;
}

// Outlines (ADR 0055): this far from the camera, an outline is its full
// width in pixels; past it, it thins in proportion, so distant crowds and
// scenery don't turn into dark blobs.
public static const float outlineFadeDistance = 10.0;

// A vertex of an inverted hull: pushed out along its outline normal (its
// normal without that stream) by the material's width, measured on screen.
public MaterialVertex outlineVertexMain(uint vertex, uint instance)
{
    MaterialVertex output = materialVertexMain(vertex, instance);
    float3 direction = output.normal;
    if ((draw.attributes & outlineNormalsAttribute) != 0)
    {
        ByteAddressBuffer model = ResourceDescriptorHeap[draw.model];
        direction = worldNormal(octahedral(model.Load(draw.outlineNormals + vertex * 4)), instanceTransform(instance));
    }
    const float depth = max(mul(float4(output.world, 1.0), view.view).z, 1e-3);
    const float width = draw.outlineWidth * min(1.0, outlineFadeDistance / depth);
    const float2 screen = mul(float4(direction, 0.0), view.viewProjection).xy;
    output.position.xy += screen / max(length(screen), 1e-6) * width * 2.0 / view.viewportSize * output.position.w;
    return output;
}

// A hull's pixel: the outline color, times the surface's color as far as
// the material's tint says; alpha-tested materials clip their hull too.
public float4 materialOutline<M : IMaterial>(MaterialVertex v, bool frontFace)
{
    float3 color = draw.outlineColor;
    if (draw.outlineTint > 0.0 || draw.alphaCutoff > 0.0)
    {
        const M material = loadMaterial<M>();
        const SurfaceData surface = material.surface(surfaceInput(v, frontFace));
        if (surface.alpha < draw.alphaCutoff)
        {
            discard;
        }
        color *= lerp(float3(1.0, 1.0, 1.0), surface.albedo, draw.outlineTint);
    }
    return float4(color, 1.0);
}

public SurfaceInput surfaceInput(MaterialVertex v, bool frontFace)
{
    SurfaceInput input;
    input.position = v.world;
    // Back faces of double-sided materials face the other way.
    input.normal = normalize(v.normal) * (frontFace ? 1.0 : -1.0);
    input.tangent = float4(normalize(v.tangent.xyz), v.tangent.w);
    input.uv0 = v.uv0;
    input.uv1 = v.uv1;
    input.color = v.color;
    input.view = normalize(view.cameraPosition - v.world);
    input.screenPosition = v.position;
    input.time = view.time;
    input.entity = v.entity;
    return input;
}

// ---------------------------------------------------------------------------
// The lighting loop
// ---------------------------------------------------------------------------

public M loadMaterial<M : IMaterial>()
{
    ByteAddressBuffer materials = ResourceDescriptorHeap[draw.materials];
    return materials.Load<M>(draw.material);
}

// The cluster grid (ADR 0053): LightClustering's 16 x 9 x 24.
static const uint clusterCountX = 16;
static const uint clusterCountY = 9;
static const uint clusterCountZ = 24;
static const uint maxLightsPerCluster = 64;

// The cluster a pixel falls in: its screen tile, and the slice of its view
// depth.
public uint clusterOf(float4 screenPosition, float3 world)
{
    const uint2 tile = min(uint2(screenPosition.xy / view.clusterTileSize), uint2(clusterCountX - 1, clusterCountY - 1));
    const float depth = max(mul(float4(world, 1.0), view.view).z, 1e-4);
    const int slice = clamp(int(floor(log(depth) * view.clusterSliceScale + view.clusterSliceBias)), 0,
                            int(clusterCountZ) - 1);
    return tile.x + tile.y * clusterCountX + uint(slice) * clusterCountX * clusterCountY;
}

// A point or spot light at a position: inverse square, smoothly windowed to
// zero at its range; for a spot, its cone.
public LightSample pointSample(LightData light, float3 position)
{
    const float3 toLight = light.position - position;
    const float distanceSquared = max(dot(toLight, toLight), 1e-4);
    const float distance = sqrt(distanceSquared);
    const float ratio = distance / max(light.range, 1e-4);
    const float window = saturate(1.0 - ratio * ratio * ratio * ratio);
    LightSample sample;
    sample.direction = toLight / distance;
    const float cone = saturate(dot(light.direction, sample.direction) * light.spotScale + light.spotOffset);
    sample.color = light.color * (window * window / distanceSquared) * (cone * cone);
    sample.shadow = 1.0;
    return sample;
}

// The lighting loop: every directional light, then the point and spot
// lights in the pixel's cluster, then the ambient light for its normal.
public float4 shadeMaterial<M : IMaterial>(MaterialVertex v, bool frontFace)
{
    const M material = loadMaterial<M>();
    const SurfaceInput input = surfaceInput(v, frontFace);
    const SurfaceData surface = material.surface(input);
    const M.Shading.Data data = material.shading(input);
    M.Shading.Accumulator accumulator = M.Shading.begin(surface, data, input);

    StructuredBuffer<LightData> lights = ResourceDescriptorHeap[view.lights];
    const float viewDepth = mul(float4(input.position, 1.0), view.view).z;
    for (uint i = 0; i < view.directionalLightCount; ++i)
    {
        LightSample sun;
        sun.direction = lights[i].direction;
        sun.color = lights[i].color;
        // The cascades are the first directional light's.
        sun.shadow = i == 0 ? sunShadow(input.position, input.normal, sun.direction, viewDepth, view.cascadeSplits,
                                        view.shadowViews, view.cascadeMap)
                            : 1.0;
        M.Shading.light(accumulator, sun);
    }
    if (view.lightCount > 0)
    {
        StructuredBuffer<uint> counts = ResourceDescriptorHeap[view.clusterRanges];
        StructuredBuffer<uint> lists = ResourceDescriptorHeap[view.clusterLights];
        const uint cluster = clusterOf(input.screenPosition, input.position);
        const uint count = counts[cluster];
        for (uint j = 0; j < count; ++j)
        {
            const LightData light = lights[lists[cluster * maxLightsPerCluster + j]];
            LightSample sample = pointSample(light, input.position);
            sample.shadow = localShadow(light, input.position, input.normal, view.shadowViews, view.shadowAtlas);
            M.Shading.light(accumulator, sample);
        }
    }

    // Hemisphere ambient (ADR 0053): the sky's color above, the ground's
    // below.
    AmbientSample ambient;
    ambient.color = lerp(view.ambientGround, view.ambientSky, surface.normal.y * 0.5 + 0.5);
    M.Shading.ambient(accumulator, ambient);
    return M.Shading.finish(accumulator, surface, data, input);
}

// A material's pixel in Opaque. For alpha-tested materials, the prepass's
// coverage already decided which samples it colors (Opaque tests Equal).
public float4 materialForward<M : IMaterial>(MaterialVertex v, bool frontFace)
{
    const float4 color = shadeMaterial<M>(v, frontFace);
    return float4(color.rgb, 1.0);
}

// A material's pixel in Shadows, for alpha-tested materials: clipped at the
// cutoff.
public void materialMask<M : IMaterial>(MaterialVertex v, bool frontFace)
{
    clipMaterial<M>(v, frontFace, draw.alphaCutoff);
}

// A material's pixel in DepthPrepass, for alpha-tested materials, drawn
// with alpha-to-coverage (ADR 0004, 0055): alpha sharpened around the
// cutoff to about a pixel's width, so each MSAA sample is in or out and
// edges are antialiased instead of stair-stepped (Golus's "anti-aliased
// alpha test"). With one sample it's the plain alpha test.
public float4 materialCoverage<M : IMaterial>(MaterialVertex v, bool frontFace)
{
    const M material = loadMaterial<M>();
    const float alpha = material.surface(surfaceInput(v, frontFace)).alpha;
    const float coverage = (alpha - draw.alphaCutoff) / max(fwidth(alpha), 1e-4) + 0.5;
    return float4(0.0, 0.0, 0.0, saturate(coverage));
}

// A blended material's pixel in Transparent (ADR 0055): premultiplied, so
// the same blend also covers additive light (color with alpha 0).
public float4 materialTransparent<M : IMaterial>(MaterialVertex v, bool frontFace)
{
    const float4 color = shadeMaterial<M>(v, frontFace);
    return float4(color.rgb * color.a, color.a);
}

// Alpha-tested depth: the surface's alpha against the cutoff.
public void clipMaterial<M : IMaterial>(MaterialVertex v, bool frontFace, float cutoff)
{
    const M material = loadMaterial<M>();
    const SurfaceData surface = material.surface(surfaceInput(v, frontFace));
    if (surface.alpha < cutoff)
    {
        discard;
    }
}

// ---------------------------------------------------------------------------
// Shading models
// ---------------------------------------------------------------------------

// Soft-banded toon (ADR 0010): N·L in `bands` steps whose edges are
// smoothed over `softness`, shadowed toward shadowTint, plus a rim light on
// lit edges.
public struct ToonData
{
    public uint bands;
    public float softness; // 0 hard edges, up to 0.5
    public float3 shadowTint;
    public float rimWidth; // 0 for no rim
    public float3 rimColor;

    public static ToonData defaults()
    {
        ToonData d;
        d.bands = 2;
        d.softness = 0.08;
        d.shadowTint = float3(0.55, 0.5, 0.65);
        d.rimWidth = 0.25;
        d.rimColor = float3(0.35, 0.35, 0.35);
        return d;
    }
}

public struct ToonShading : IShadingModel
{
    public typealias Data = ToonData;

    public struct Accumulator
    {
        public float3 albedo;
        public float3 normal;
        public float3 view;
        public float occlusion;
        public float3 lit;     // banded diffuse from lights
        public float3 ambient;
        public float rimLight; // how lit the pixel is, for the rim
        public float edgeWidth; // how far N·L can change across the pixel
        public ToonData data;
    }

    public static Accumulator begin(SurfaceData s, Data d, SurfaceInput input)
    {
        Accumulator a;
        a.albedo = s.albedo;
        a.normal = s.normal;
        a.view = input.view;
        a.occlusion = s.occlusion;
        a.lit = float3(0.0, 0.0, 0.0);
        a.ambient = float3(0.0, 0.0, 0.0);
        a.rimLight = 0.0;
        // N·L changes across the pixel by at most the normal's change
        // (derivatives here, where they're valid, not per light).
        a.edgeWidth = length(ddx(s.normal)) + length(ddy(s.normal));
        a.data = d;
        return a;
    }

    // The banded response to one light: 0 to 1, in bands steps with soft
    // edges, each at least as wide as N·L's change across the pixel, so
    // distant band edges don't stair-step (ADR 0055).
    static float band(float x, uint bands, float softness, float edgeWidth)
    {
        const float scaled = saturate(x) * bands;
        const float step = floor(scaled);
        const float width = max(softness, edgeWidth * bands * 0.5);
        const float edge = smoothstep(0.5 - width, 0.5 + width, scaled - step);
        return min((step + edge) / bands, 1.0);
    }

    public static void light(inout Accumulator a, LightSample light)
    {
        // A cast shadow darkens like turning away: into the same bands, and
        // so the same shadow tint.
        const float response =
            band(saturate(dot(a.normal, light.direction)) * light.shadow, max(a.data.bands, 1u), a.data.softness,
                 a.edgeWidth);
        a.lit += light.color * response;
        a.rimLight = max(a.rimLight, response);
    }

    public static void ambient(inout Accumulator a, AmbientSample ambient)
    {
        a.ambient += ambient.color;
    }

    public static float4 finish(Accumulator a, SurfaceData s, Data d, SurfaceInput input)
    {
        // Shadowed areas are tinted, not black.
        const float3 diffuse = a.albedo * (a.lit + a.ambient * a.occlusion + d.shadowTint * a.ambient);
        const float rim = d.rimWidth > 0.0
                              ? smoothstep(1.0 - d.rimWidth, 1.0, 1.0 - saturate(dot(a.normal, a.view))) * a.rimLight
                              : 0.0;
        return float4(diffuse + d.rimColor * rim + s.emissive, s.alpha);
    }
}

// Physically based: GGX distribution, height-correlated Smith visibility,
// Schlick Fresnel, and Lambert diffuse, metallic-roughness as in glTF.
public struct PbrData
{
    public static PbrData defaults()
    {
        PbrData d;
        d.unused = 0;
        return d;
    }
    public uint unused; // PBR has no inputs of its own yet
}

public struct PbrShading : IShadingModel
{
    public typealias Data = PbrData;

    public struct Accumulator
    {
        public float3 diffuseColor;
        public float3 f0;
        public float alpha; // roughness squared
        public float3 normal;
        public float3 view;
        public float occlusion;
        public float3 color;
    }

    public static Accumulator begin(SurfaceData s, Data d, SurfaceInput input)
    {
        Accumulator a;
        a.diffuseColor = s.albedo * (1.0 - s.metallic);
        a.f0 = lerp(float3(0.04, 0.04, 0.04), s.albedo, s.metallic);
        const float roughness = clamp(specularAntialiasing(s.normal, s.roughness), 0.045, 1.0);
        a.alpha = roughness * roughness;
        a.normal = s.normal;
        a.view = input.view;
        a.occlusion = s.occlusion;
        a.color = float3(0.0, 0.0, 0.0);
        return a;
    }

    public static void light(inout Accumulator a, LightSample light)
    {
        const float3 h = normalize(light.direction + a.view);
        const float nl = saturate(dot(a.normal, light.direction));
        const float nv = max(dot(a.normal, a.view), 1e-4);
        const float nh = saturate(dot(a.normal, h));
        const float vh = saturate(dot(a.view, h));
        const float a2 = a.alpha * a.alpha;
        const float denominator = nh * nh * (a2 - 1.0) + 1.0;
        const float distribution = a2 / (3.14159265 * denominator * denominator);
        const float visibility =
            0.5 / (nl * sqrt(nv * nv * (1.0 - a2) + a2) + nv * sqrt(nl * nl * (1.0 - a2) + a2) + 1e-5);
        const float3 fresnel = a.f0 + (1.0 - a.f0) * pow(1.0 - vh, 5.0);
        const float3 specular = distribution * visibility * fresnel;
        const float3 diffuse = (1.0 - fresnel) * a.diffuseColor / 3.14159265;
        // Lights are in "pi-normalized" units: a white light at normal
        // incidence lights a white Lambert surface to 1.
        a.color += (diffuse + specular) * light.color * (nl * light.shadow * 3.14159265);
    }

    public static void ambient(inout Accumulator a, AmbientSample ambient)
    {
        // Until image-based lighting: diffuse ambient, and F0 standing in
        // for the specular part.
        a.color += (a.diffuseColor + a.f0 * 0.5) * ambient.color * a.occlusion;
    }

    public static float4 finish(Accumulator a, SurfaceData s, Data d, SurfaceInput input)
    {
        return float4(a.color + s.emissive, s.alpha);
    }
}
