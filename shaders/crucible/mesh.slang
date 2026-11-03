// The depth-only vertex shader (ADR 0051): DepthPrepass draws opaque
// materials' depth with it, positions only. Alpha-tested materials use
// their own (default.slang's maskPixel).
import crucible.shared;
import crucible.material;

[shader("vertex")]
float4 depthVertex(uint vertex : SV_VertexID, uint instance : SV_InstanceID,
                   uint firstInstance : SV_StartInstanceLocation) : SV_Position
{
    const float3 world = worldPosition(vertex, instanceTransform(firstInstance + instance));
    return mul(float4(world, 1.0), view.viewProjection);
}

// Clears one shadow-atlas tile (ADR 0054): a triangle covering the viewport
// at the far depth (1, conventional depth), drawn with depth always written.
[shader("vertex")]
float4 clearTileVertex(uint vertex : SV_VertexID) : SV_Position
{
    const float2 corner = float2((vertex << 1) & 2, vertex & 2);
    return float4(corner * float2(2.0, -2.0) + float2(-1.0, 1.0), 1.0, 1.0);
}
