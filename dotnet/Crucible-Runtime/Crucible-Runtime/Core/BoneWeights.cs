using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Crucible.Core;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct BoneWeight
{
    public UInt32 index;
    public float weight;

    public BoneWeight(UInt32 index, float weight)
    {
        this.index = index;
        this.weight = weight;
    }

    public static bool operator ==(BoneWeight bw1, BoneWeight bw2)
    {
        return bw1.index == bw2.index &&  bw1.weight == bw2.weight;
    }

    public static bool operator !=(BoneWeight bw1, BoneWeight bw2)
    {
        return !(bw1 == bw2);
    }
}
[InlineArray(4)]
public struct BoneWeightArray
{
    private BoneWeight _element0;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct BoneWeights
{
    public BoneWeightArray weights;
    
    public BoneWeights(BoneWeight weight)
    {
        weights[0] =  weight;
    }
    
    public BoneWeights(BoneWeight weight1, BoneWeight weight2)
    {
        weights[0] =  weight1;
        weights[1] =  weight2;
    }
    
    public BoneWeights(BoneWeight weight1, BoneWeight weight2, BoneWeight weight3)
    {
        weights[0] =  weight1;
        weights[1] =  weight2;
        weights[2] = weight3;
    }

    public BoneWeights(BoneWeight weight1, BoneWeight weight2, BoneWeight weight3, BoneWeight weight4)
    {
        weights[0] =  weight1;
        weights[1] =  weight2;
        weights[2] = weight3;
        weights[3] = weight4;
    }

    public static bool operator ==(BoneWeights bw1, BoneWeights bw2)
    {
        return bw1.weights[0] == bw2.weights[0] && bw1.weights[1] == bw2.weights[1] &&  bw1.weights[2] == bw2.weights[2] &&  bw1.weights[3] == bw2.weights[3];
    }

    public static bool operator !=(BoneWeights bw1, BoneWeights bw2)
    {
        return !(bw1 == bw2);
    }
}