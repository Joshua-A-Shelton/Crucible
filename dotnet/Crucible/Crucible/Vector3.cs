using System.Runtime.InteropServices;

namespace Crucible;



[StructLayout(LayoutKind.Sequential)]
public struct Vector3
{
    public float x;
    public float y;
    public float z;

    public Vector3()
    {
        x = 0;
        y = 0;
        z = 0;
    }

    public Vector3(float x, float y, float z)
    {
        this.x = x;
        this.y = y;
        this.z = z;
    }

    public override string ToString()
    {
        return "{" + x + ", " + y + ", " + z + "}";
    }

    public static unsafe Vector3 Cross(Vector3 v1, Vector3 v2)
    {
        Vector3 returnVector = new Vector3();
        Interop.vector3Cross(v1, v2, out returnVector);
        return returnVector;
    }

}