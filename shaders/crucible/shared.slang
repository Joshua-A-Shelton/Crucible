// Crucible's shader library: what every shader may import (ADR 0047).

// Marks a struct whose layout C++ and C# mirror: shaderfax generates
// both, and refuses a struct laid out differently as a constant buffer member
// and a structured buffer element, or on Vulkan and D3D12.
[__AttributeUsage(_AttributeTargets.Struct)]
public struct GpuSharedAttribute
{
};

// The view a frame is drawn from (ADR 0050), filled by the renderer from the
// camera every frame. Row vectors, as in C++: a world position p lands at
// mul(float4(p, 1), viewProjection). Depth is reverse-Z, with no far plane
// for a perspective camera.
[GpuShared]
public struct ViewConstants
{
    public float4x4 view;
    public float4x4 projection;
    public float4x4 viewProjection;
    public float4x4 inverseView;
    public float4x4 inverseProjection;
    public float4x4 inverseViewProjection;
    public float3 cameraPosition; // world space
    public float time;            // seconds since the renderer started
    public float2 viewportSize;   // pixels
    // Lighting (ADR 0053): the clusters' screen tiles in pixels, the
    // ambient light's two colors (intensity applied), and the view-depth
    // slice of a depth z, floor(log(z) * scale + bias).
    public float2 clusterTileSize;
    public float3 ambientSky;
    public float clusterSliceScale;
    public float3 ambientGround;
    public float clusterSliceBias;
    public uint directionalLightCount; // the first entries of lights
    public uint lightCount;            // point and spot lights after them
    // Resource-heap slots: StructuredBuffer<LightData> lights; each
    // cluster's light count (StructuredBuffer<uint>) and its list of light
    // indices (StructuredBuffer<uint>, maxLightsPerCluster per cluster).
    public uint lights;
    public uint clusterRanges;
    public uint clusterLights;
    // Shadows (ADR 0054): resource-heap slots of the frame's
    // StructuredBuffer<ShadowView> (the cascades first), the cascades'
    // Texture2DArray<float> (0: the sun casts no shadows this frame), and
    // the shadow atlas's Texture2D<float> (0: no light has a tile); and the
    // view depth where each of the sun's cascades ends.
    public uint shadowViews;
    public uint cascadeMap;
    public uint shadowAtlas;
    public float4 cascadeSplits;
    // The sky (ADR 0055), intensity applied: its three colors; the cosine
    // of the sun disk's angular radius, and the disk's brightness over the
    // sun's color; whether there is a sky (0: renderFrame's clear color).
    public float3 skyZenith;
    public float skySunCosine;
    public float3 skyHorizon;
    public float skySunIntensity;
    public float3 skyGround;
    public uint sky;
    // Finishing (ADR 0055), from the camera's PostProcess: white balance as
    // one matrix (row vectors, applied before tonemapping); lift, gamma,
    // gain, contrast, and saturation after it; the exposure as a scale; the
    // bloom's mix and threshold; and the Tonemapper.
    public float4x4 whiteBalance;
    public float3 lift;
    public float contrast;
    public float3 gamma;
    public float saturation;
    public float3 gain;
    public uint tonemapper;
    public float exposureScale;
    public float bloomStrength;
    public float bloomThreshold;
    public uint bloomLevels; // the bloom chain's levels this frame; 0 for none
};

// A world matrix as shaders read it (ADR 0051): its first three columns, so
// a world position is (dot(p, x), dot(p, y), dot(p, z)) with p = float4(position, 1).
[GpuShared]
public struct InstanceTransform
{
    public float4 x;
    public float4 y;
    public float4 z;
};

// Per draw, as push constants (ADR 0051, 0052): one submesh of one batch.
// Stream offsets are bytes into the model's buffer, at the mesh's first
// vertex; a stream the mesh lacks (attributes) isn't read.
[GpuShared]
public struct DrawConstants
{
    public uint model; // the model buffer's slot (MeshData::descriptor)
    public uint positions;
    public uint normals;
    public uint tangents;
    public uint uv0;
    public uint uv1;
    public uint colors;
    public uint attributes; // the mesh's MeshAttribute bits
    public uint floatUvs;   // bit 0: UV0 holds floats, bit 1: UV1; else halves
    public uint instances;  // DrawList::instances
    public uint transforms; // DrawList::transforms
    public uint materials;  // the material buffer's slot (a ByteAddressBuffer)
    public uint material;   // this draw's material: its byte offset there
    public float alphaCutoff; // alpha-tested materials discard below it; 0 otherwise
    // Outlines (ADR 0055): the outline-normal stream's offset (when
    // attributes has it), and the material's outline.
    public uint outlineNormals;
    public float outlineWidth;
    public float3 outlineColor;
    public float outlineTint;
};

// One light as shaders read it (ADR 0053), in world space. Directional
// lights: direction toward the light, range 0. Point lights: spotScale 0 and
// spotOffset 1 (no cone). Spot lights: direction toward the light (against
// the cone's axis), and the cone as saturate(cos * spotScale + spotOffset).
[GpuShared]
public struct LightData
{
    public float3 position;
    public float range;
    public float3 direction;
    public float spotScale;
    public float3 color; // intensity applied
    public float spotOffset;
    // Its first ShadowView (a point light's six cube faces follow it, in
    // the order +X, -X, +Y, -Y, +Z, -Z), or noShadowView.
    public uint shadowView;
};

public static const uint noShadowView = 0xffffffff;

// One shadow map's view (ADR 0054): a cascade, a spot light's tile, or one
// face of a point light's. Depth is conventional (0 near, 1 far), compared
// with the stored depth by a LessEqual comparison sampler.
[GpuShared]
public struct ShadowView
{
    public float4x4 viewProjection; // world position to the map's clip space
    // Where the view sits in its map's UVs: offset in xy, size in zw.
    public float4 rect;
    public float texelSize; // 1 / the map's size in texels
    // A texel's width in meters, which the normal offset scales with:
    // normalOffset everywhere (cascades), plus normalOffsetPerMeter per
    // meter from the light (atlas tiles).
    public float normalOffset;
    public float normalOffsetPerMeter;
    public uint layer; // the cascade's layer; 0 for atlas tiles
};
