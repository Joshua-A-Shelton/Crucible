using System.Runtime.InteropServices;
namespace Crucible;

[StructLayout(LayoutKind.Sequential)]
public struct Vector4
{
    [Expose]
    public float x;
    [Expose]
    public float y;
    [Expose]
    public float z;
    [Expose]
    public float w;

    public Vector4(float x, float y, float z, float w)
    {
        this.x = x;
        this.y = y;
        this.z = z;
        this.w = w;
    }
}