using System.Runtime.InteropServices;

namespace Crucible;

[StructLayout(LayoutKind.Sequential)]
public struct Vector2
{
    [Expose]
    public float x;
    [Expose]
    public float y;
}