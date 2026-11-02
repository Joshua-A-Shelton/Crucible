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
