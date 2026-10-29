// The mesh shaders (ADR 0051): positions only for DepthPrepass, and unlit
// base color for Opaque. Vertices are pulled from the model's buffer, from
// ADR 0043's streams; each instance finds its world matrix through the draw
// list (the instance list maps it to an entity index, the transform buffer
// holds world matrices by entity index).
import crucible.shared;

ConstantBuffer<ViewConstants> view : register(b1);

// Per draw, as push constants: one submesh of one batch.
[GpuShared]
struct DrawConstants
{
    uint model;     // the model buffer's slot (MeshData::descriptor)
    uint positions; // byte offsets of the mesh's first vertex in each stream
    uint uv0;
    uint colors;
    uint attributes; // the mesh's MeshAttribute bits
    uint floatUvs;   // 1: UV0 holds floats, else halves
    uint instances;  // DrawList::instances
    uint transforms; // DrawList::transforms
    float4 baseColor; // linear
    uint baseColorTexture; // slot, 0 for none
    uint baseColorSampler;
};
[[vk::push_constant]] ConstantBuffer<DrawConstants> draw;

static const uint uv0Attribute = 4;
static const uint colorsAttribute = 16;

float3 worldPosition(uint vertex, uint instance)
{
    ByteAddressBuffer model = ResourceDescriptorHeap[draw.model];
    StructuredBuffer<uint> instances = ResourceDescriptorHeap[draw.instances];
    StructuredBuffer<InstanceTransform> transforms = ResourceDescriptorHeap[draw.transforms];
    const float4 position = float4(asfloat(model.Load3(draw.positions + vertex * 12)), 1.0);
    const InstanceTransform transform = transforms[instances[instance]];
    return float3(dot(position, transform.x), dot(position, transform.y), dot(position, transform.z));
}

float4 clipPosition(uint vertex, uint instance)
{
    return mul(float4(worldPosition(vertex, instance), 1.0), view.viewProjection);
}

[shader("vertex")]
float4 depthVertex(uint vertex : SV_VertexID, uint instance : SV_InstanceID,
                   uint firstInstance : SV_StartInstanceLocation) : SV_Position
{
    return clipPosition(vertex, firstInstance + instance);
}

struct OpaqueVertex
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
    float4 color : COLOR;
};

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

float3 srgbToLinear(float3 c)
{
    return select(c <= 0.04045, c / 12.92, pow((c + 0.055) / 1.055, 2.4));
}

[shader("vertex")]
OpaqueVertex opaqueVertex(uint vertex : SV_VertexID, uint instance : SV_InstanceID,
                          uint firstInstance : SV_StartInstanceLocation)
{
    ByteAddressBuffer model = ResourceDescriptorHeap[draw.model];
    OpaqueVertex output;
    output.position = clipPosition(vertex, firstInstance + instance);
    output.uv = float2(0.0, 0.0);
    if ((draw.attributes & uv0Attribute) != 0)
    {
        if (draw.floatUvs != 0)
        {
            output.uv = asfloat(model.Load2(draw.uv0 + vertex * 8));
        }
        else
        {
            const uint packed = model.Load(draw.uv0 + vertex * 4);
            output.uv = float2(halfToFloat(packed & 0xffff), halfToFloat(packed >> 16));
        }
    }
    output.color = float4(1.0, 1.0, 1.0, 1.0);
    if ((draw.attributes & colorsAttribute) != 0)
    {
        const uint packed = model.Load(draw.colors + vertex * 4);
        const float4 color = float4(packed & 0xff, (packed >> 8) & 0xff, (packed >> 16) & 0xff, packed >> 24) / 255.0;
        output.color = float4(srgbToLinear(color.rgb), color.a);
    }
    return output;
}

[shader("fragment")]
float4 opaquePixel(OpaqueVertex input) : SV_Target
{
    float4 color = draw.baseColor * input.color;
    if (draw.baseColorTexture != 0)
    {
        Texture2D<float4> texture = ResourceDescriptorHeap[draw.baseColorTexture];
        SamplerState sampler = SamplerDescriptorHeap[draw.baseColorSampler];
        color *= texture.Sample(sampler, input.uv);
    }
    return float4(color.rgb, 1.0);
}
