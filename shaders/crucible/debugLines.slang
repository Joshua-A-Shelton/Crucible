// The DebugLines step (ADR 0079): the frame's debug lines over the finished
// image, two vertices each, pulled from a StructuredBuffer<DebugVertex>.
import crucible.shared;

ConstantBuffer<ViewConstants> view : register(b1);

struct DebugVertex
{
    float3 position; // world space
    uint color;      // RGBA8, linear
};

struct DebugConstants
{
    uint vertices; // StructuredBuffer<DebugVertex>
    uint pad0;
    uint pad1;
    uint pad2;
};
[[vk::push_constant]] ConstantBuffer<DebugConstants> debug;

struct DebugOutput
{
    float4 position : SV_Position;
    float4 color : COLOR;
};

[shader("vertex")]
DebugOutput debugLineVertex(uint vertex : SV_VertexID)
{
    StructuredBuffer<DebugVertex> vertices = ResourceDescriptorHeap[debug.vertices];
    const DebugVertex v = vertices[vertex];
    DebugOutput output;
    output.position = mul(float4(v.position, 1.0), view.viewProjection);
    output.color = float4(v.color & 0xff, (v.color >> 8) & 0xff, (v.color >> 16) & 0xff, v.color >> 24) / 255.0;
    return output;
}

[shader("fragment")]
float4 debugLinePixel(DebugOutput input) : SV_Target
{
    return input.color;
}
