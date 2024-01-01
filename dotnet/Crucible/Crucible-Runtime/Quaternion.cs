using System.Runtime.InteropServices;

namespace Crucible;

[StructLayout(LayoutKind.Sequential)]
public class Quaternion
{
    public float x;
    public float y;
    public float z;
    public float w;

    public Quaternion()
    {
        x = 0;
        y = 0;
        z = 0;
        w = 1;
    }

}