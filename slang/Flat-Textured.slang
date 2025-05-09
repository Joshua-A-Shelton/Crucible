#include "CrucibleCore.slang"
#include "Mesh.slang"

struct FragmentParameters
{
    float2 uv;
}
struct VertexStageOutput
{
    FragmentParameters fragmentParameters;
    float4 sv_position: SV_Position;
}

struct FlatTextureObject
{
    matrix<float,4,4> transform;
    Sampler2D sampledTexture;
}

ParameterBlock<FlatTextureObject> FlatObjectInstance;

[shader("vertex")]
VertexStageOutput vertexMain(Vertex3D vertex3d, UVCoordinates uvCoordinates)
{
    float3 worldPosition = mul(FlatObjectInstance.transform,float4(vertex3d.position,1)).xyz;

    VertexStageOutput output;
    output.fragmentParameters.uv = uvCoordinates.position;
    output.sv_position = mul(Camera.projectionView, float4(worldPosition,1));
    return output;
}

[shader("fragment")]
[OutputColorTarget(0,TexelFormatReflection.B8G8R8A8_UNORM_SRGB)]
[OutputDepthTarget(TexelFormatReflection.D32_FLOAT)]
float4 fragmentMain(FragmentParameters fragmentParameters : FragmentParameters) : SV_Target
{
    return FlatObjectInstance.sampledTexture.Sample(fragmentParameters.uv);
}