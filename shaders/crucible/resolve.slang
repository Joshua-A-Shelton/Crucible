// The Resolve step's shader (ADR 0051): SceneColor's samples, each clamped to
// [0, 1], averaged into the backbuffer, whose sRGB view encodes them. Clamping
// each sample before averaging is the post-tonemap resolve of ADR 0015, with
// a clamp standing in for M5 step 9's tonemapping.

struct ResolveConstants
{
    uint source;  // SceneColor's slot: Texture2DMS, or Texture2D at one sample
    uint samples;
};
[[vk::push_constant]] ConstantBuffer<ResolveConstants> resolve;

// One triangle covering the screen.
[shader("vertex")]
float4 fullscreenVertex(uint vertex : SV_VertexID) : SV_Position
{
    const float2 corner = float2((vertex << 1) & 2, vertex & 2);
    return float4(corner * float2(2.0, -2.0) + float2(-1.0, 1.0), 0.0, 1.0);
}

[shader("fragment")]
float4 resolvePixel(float4 position : SV_Position) : SV_Target
{
    const int2 pixel = int2(position.xy);
    if (resolve.samples <= 1)
    {
        Texture2D<float4> single = ResourceDescriptorHeap[resolve.source];
        return float4(saturate(single.Load(int3(pixel, 0)).rgb), 1.0);
    }
    Texture2DMS<float4> scene = ResourceDescriptorHeap[resolve.source];
    float3 sum = float3(0.0, 0.0, 0.0);
    for (uint i = 0; i < resolve.samples; ++i)
    {
        sum += saturate(scene.Load(pixel, i).rgb);
    }
    return float4(sum / resolve.samples, 1.0);
}
