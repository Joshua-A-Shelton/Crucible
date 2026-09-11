using System.Runtime.InteropServices;

namespace Crucible.Core;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct BoneWeights
{
    public fixed float Weights[4];
    public fixed UInt16 BoneIndices[4];
}