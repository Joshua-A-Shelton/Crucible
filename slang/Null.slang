//import Mesh;
#include "Mesh.slang"
struct NullObject
{
    matrix<float,4,4> transform;
}

ParameterBlock<NullObject> NullObjectInstance;

[shader("vertex")]
float4 vertexMain(Vertex3D vertex3d) : SV_Position
{
    float4 finalPosition = float4(vertex3d.position,1.0);
    finalPosition = mul(mul(mul(View.perspective,View.view),NullObjectInstance.transform),finalPosition);

    return finalPosition;
}

[shader("fragment")]
float4 fragmentMain() : SV_Target
{
    return float4(1,0,1,1);
}