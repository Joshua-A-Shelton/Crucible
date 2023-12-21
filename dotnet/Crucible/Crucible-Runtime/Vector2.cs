using System.Runtime.InteropServices;

namespace Crucible;

[StructLayout(LayoutKind.Sequential)]
public struct Vector2
{
    [Expose]
    public float x;
    [Expose]
    public float y;

    public Vector2()
    {
        x = 0;
        y = 0;
    }

    public Vector2(float x, float y)
    {
        this.x = x;
        this.y = y;
    }
}