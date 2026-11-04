// The batched command API's GPU test (ADR 0058): a compute pass copies four
// colors from the frame data into a transient buffer, then a graphics pass
// draws one quad per quarter of the target in those colors, times a tint
// from a constant buffer in the frame data.

struct ProbeConstants
{
    uint buffer;    // the colors' buffer: RW in the compute pass, read in the graphics pass
    uint frameData; // the frame data's slot
    uint offset;    // the colors in the frame data
};
[[vk::push_constant]] ConstantBuffer<ProbeConstants> probe;

struct QuadConstants
{
    float4 tint;
};
ConstantBuffer<QuadConstants> quad : register(b1);

[shader("compute")]
[numthreads(4, 1, 1)]
void fillColors(uint i : SV_DispatchThreadID)
{
    ByteAddressBuffer frame = ResourceDescriptorHeap[probe.frameData];
    RWByteAddressBuffer output = ResourceDescriptorHeap[probe.buffer];
    output.Store<float4>(i * 16, frame.Load<float4>(probe.offset + i * 16));
}

struct QuadOut
{
    float4 position : SV_Position;
    nointerpolation float4 color : COLOR;
};

// A triangle strip of 4 vertices per instance; instance i covers quarter i
// (0 top left, 1 top right, 2 bottom left, 3 bottom right).
[shader("vertex")]
QuadOut quadVertex(uint vertex : SV_VertexID, uint instance : SV_InstanceID)
{
    const float2 corner = float2(vertex & 1, vertex >> 1);
    const float2 cell = float2(instance & 1, instance >> 1);
    ByteAddressBuffer colors = ResourceDescriptorHeap[probe.buffer];
    QuadOut output;
    output.position = float4((cell + corner) * float2(1, -1) + float2(-1, 1), 0, 1);
    output.color = colors.Load<float4>(instance * 16) * quad.tint;
    return output;
}

[shader("pixel")]
float4 quadPixel(QuadOut input) : SV_Target
{
    return input.color;
}
