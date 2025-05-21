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

struct AnimatedFlatMaterial
{
    Sampler2D sampledTexture;
    StructuredBuffer<matrix<float,4,4>> boneTransforms;
}

ParameterBlock<AnimatedFlatMaterial> AnimatedFlatMaterialInstance;
public ParameterBlock<ObjectInstanceData> ObjectInstance;

[shader("vertex")]
VertexStageOutput vertexMain(Vertex3D vertex3d, UVCoordinates uvCoordinates, BoneWeights boneWeight, uint vertexIndex : SV_VertexID)
{
    float4 temp = float4(0,0,0,0);
    float4 v = float4(vertex3d.position,1);

    temp += mul((ObjectInstance.bones[boneWeight.weights[0].boneId]) , boneWeight.weights[0].weight);
    temp += mul((ObjectInstance.bones[boneWeight.weights[1].boneId]) , boneWeight.weights[1].weight);
    temp += mul((ObjectInstance.bones[boneWeight.weights[2].boneId]) , boneWeight.weights[2].weight);
    temp += mul((ObjectInstance.bones[boneWeight.weights[3].boneId]) , boneWeight.weights[3].weight);


    float3 worldPosition = mul(ObjectInstance.transform,temp).xyz;

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
    return AnimatedFlatMaterialInstance.sampledTexture.Sample(fragmentParameters.uv);
}