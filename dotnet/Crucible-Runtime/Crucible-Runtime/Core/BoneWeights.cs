using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Crucible.Core;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct BoneWeight
{
    public UInt32 index;
    public float weight;
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
}