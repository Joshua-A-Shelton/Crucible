// The editor's Pick step (ADR 0067): every opaque instance drawn again with
// its entity's index plus one into an R32Uint target (0 is nothing), for
// picking the entity under the pointer and outlining the selection. Alpha
// is ignored: a leafy card picks as its whole quad.
import crucible.shared;
import crucible.material;

struct PickVaryings
{
    float4 position : SV_Position;
    nointerpolation uint entity : ENTITY;
};

[shader("vertex")]
PickVaryings pickVertex(uint vertex : SV_VertexID, uint instance : SV_InstanceID,
                        uint firstInstance : SV_StartInstanceLocation)
{
    PickVaryings output;
    const InstanceTransform transform = instanceTransform(firstInstance + instance);
    output.position = mul(float4(worldPosition(vertex, transform), 1.0), view.viewProjection);
    output.entity = instanceEntity(firstInstance + instance) + 1;
    return output;
}

[shader("fragment")]
uint pickPixel(PickVaryings input) : SV_Target
{
    return input.entity;
}
