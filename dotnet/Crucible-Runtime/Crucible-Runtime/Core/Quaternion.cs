using System.Runtime.InteropServices;

namespace Crucible.Core;
[StructLayout(LayoutKind.Sequential)]
public unsafe struct Quaternion
{
    public float W;
    public float X;
    public float Y;
    public float Z;
}