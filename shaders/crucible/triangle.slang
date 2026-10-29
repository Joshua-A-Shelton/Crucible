// The Triangle render step's shader (ADR 0048): one triangle over the
// cleared backbuffer, from SV_VertexID, no vertex buffer. Temporary, until
// M5 step 5 draws meshes. Corners are clockwise on screen: Crucible's front
// faces (ADR 0024).

struct VertexOutput
{
    float4 position : SV_Position;
    float3 color : COLOR;
};

[shader("vertex")]
VertexOutput vertexMain(uint vertex : SV_VertexID)
{
    const float2 corners[3] = { float2(0.0, 0.6), float2(0.55, -0.45), float2(-0.55, -0.45) };
    const float3 colors[3] = { float3(1.0, 0.25, 0.2), float3(0.2, 0.9, 0.3), float3(0.25, 0.4, 1.0) };
    VertexOutput output;
    output.position = float4(corners[vertex], 0.0, 1.0);
    output.color = colors[vertex];
    return output;
}

[shader("fragment")]
float4 pixelMain(VertexOutput input) : SV_Target
{
    return float4(input.color, 1.0);
}
