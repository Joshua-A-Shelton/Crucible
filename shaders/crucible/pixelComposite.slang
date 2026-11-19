// The pixel camera's composite (ADR 0106), in a file of its own: each
// shader file has one push-constant block, which D3D12 maps to the root
// constants every pipeline shares.

static const uint linearClampSampler = 2;

// A low-resolution image scaled up over the finished one, premultiplied,
// at `scale` window pixels a virtual pixel from `offset`. Sharp bilinear:
// each window pixel samples the center of its virtual pixel, except within
// a window pixel of an edge between two, which blends them; at a
// whole-number scale that never happens, so the art comes out exactly. With
// `depth`, only where the 3D drew nothing (reverse-Z: depth 0). With
// `bars`, black around the image.
struct CompositeConstants
{
    uint source;       // Texture2D<float4>: the low-resolution image
    uint depth;        // the scene's depth, Texture2D<float> or Texture2DMS<float>; 0: none
    uint depthSamples; // 1, or its MSAA samples
    uint bars;         // 1: black outside the image
    float2 sourceSize; // virtual pixels
    float2 offset;     // window pixels
    float scale;       // window pixels a virtual pixel
    float pad0;
    float pad1;
    float pad2;
};

struct CompositeVaryings
{
    float4 position : SV_Position;
};

[shader("vertex")]
CompositeVaryings compositeVertex(uint vertex : SV_VertexID)
{
    CompositeVaryings output;
    const float2 corner = float2((vertex << 1) & 2, vertex & 2);
    output.position = float4(corner * float2(2, -2) + float2(-1, 1), 0, 1);
    return output;
}

[[vk::push_constant]] ConstantBuffer<CompositeConstants> composite;

[shader("fragment")]
float4 compositePixel(CompositeVaryings input) : SV_Target
{
    const float2 local = (input.position.xy - composite.offset) / composite.scale;
    if (any(local < 0) || any(local >= composite.sourceSize))
    {
        if (composite.bars == 0)
        {
            discard;
        }
        return float4(0, 0, 0, 1);
    }
    if (composite.depth != 0)
    {
        const int2 at = int2(input.position.xy);
        float depth;
        if (composite.depthSamples > 1)
        {
            Texture2DMS<float> scene = ResourceDescriptorHeap[composite.depth];
            depth = scene.Load(at, 0);
        }
        else
        {
            Texture2D<float> scene = ResourceDescriptorHeap[composite.depth];
            depth = scene.Load(int3(at, 0));
        }
        if (depth != 0)
        {
            discard;
        }
    }
    const float2 floored = floor(local);
    const float2 centered = local - floored - 0.5;
    const float region = max(0.5 - 0.5 / composite.scale, 0.0);
    const float2 f = (centered - clamp(centered, -region, region)) * max(composite.scale, 1.0) + 0.5;
    Texture2D<float4> source = ResourceDescriptorHeap[composite.source];
    SamplerState s = SamplerDescriptorHeap[linearClampSampler];
    return source.SampleLevel(s, (floored + f) / composite.sourceSize, 0);
}

