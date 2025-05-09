#include "CrucibleCore.slang"
#include "Mesh.slang"


struct VertexStageOutput
{
    float4 sv_position: SV_Position;
}

struct NullObject
{
    matrix<float,4,4> transform;
}

ParameterBlock<NullObject> NullObjectInstance;

[shader("vertex")]
VertexStageOutput vertexMain(Vertex3D vertex3d)
{
    float3 worldPosition = mul(NullObjectInstance.transform,float4(vertex3d.position,1)).xyz;

    VertexStageOutput output;
    output.sv_position = mul(Camera.projectionView, float4(worldPosition,1));
    return output;
}

[shader("fragment")]
[OutputColorTarget(0,TexelFormatReflection.B8G8R8A8_UNORM_SRGB)]
[OutputDepthTarget(TexelFormatReflection.D32_FLOAT)]
float4 fragmentMain() : SV_Target
{
    return float4(1,0,1,1);
}