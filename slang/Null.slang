import Mesh;

[shader("vertex")]
float4 vertexMain(Vertex3D vertex3d) : SV_Position
{
    float4 finalPosition = float4(vertex3d.position,1.0);
    finalPosition = mul(mul(View.perspective,View.view),finalPosition);

    return finalPosition;
}

[shader("fragment")]
float4 fragmentMain() : SV_Target
{
    return float4(1,0,1,1);
}