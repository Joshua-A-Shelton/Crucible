#include "Mesh.slang"

struct VertexUVOut
{
    float4 position : SV_Position;
    float2 uv;
}

struct FlatTextureObject
{
    matrix<float,4,4> transform;
    Sampler2D sampledTexture;
}

ParameterBlock<FlatTextureObject> FlatObjectInstance;

[shader("vertex")]
VertexUVOut vertexMain(Vertex3D vertex3d, UVCoordinates uvCoordinates) : SV_Position
{

    float4 finalPosition = float4(vertex3d.position,1.0);
    //matrix<float,4,4> matr = FlatObjectInstance.transform * View.view *View.perspective;
    //finalPosition = mul(matr,finalPosition);

    VertexUVOut output;
    output.position = finalPosition;
    output.uv = uvCoordinates.position;
    return output;
}

[shader("fragment")]
float4 fragmentMain(VertexUVOut input) : SV_Target
{
    return float4(1,0,1,1);
    //return FlatObjectInstance.sampledTexture.Sample(input.uv);
}