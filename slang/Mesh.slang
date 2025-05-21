module Mesh;
//Vertex Position 3D attribute
public struct Vertex3D
{
    public float3 position;
}
//Vertex Position 2D attribute
public struct Vertex2D
{
    public float2 position;
}
//Vertex Normal attribute
public struct Normal
{
    public float3 direction;
}
public struct Tangent
{
    public float3 direction;
}
//Vertex UV coordinate attribute
public struct UVCoordinates
{
    public float2 position;
}
//Vertex color attribute
public struct VertexColor
{
    public uint8_t4 values;
}
public struct BoneIndexWeightValue
{
    public uint16_t boneId;
    public float weight;
}
//Vertex Bone Weights attribute
public struct BoneWeights
{
    public BoneIndexWeightValue[4] weights;
}

//Data that is set once per scene per frame
public struct SceneData
{
    public float4 wind;
    public float time;
}
//Data that is set once per view (usually camera) per frame
public struct ViewData
{
    public matrix<float,4,4> projection;
    public matrix<float,4,4> view;
    public matrix<float,4,4> projectionView;
}

public struct ObjectInstanceData
{
    public matrix<float,4,4> transform;
    public StructuredBuffer<matrix<float,4,4>> bones;
}

public ParameterBlock<SceneData> Scene;

public ParameterBlock<ViewData> Camera;