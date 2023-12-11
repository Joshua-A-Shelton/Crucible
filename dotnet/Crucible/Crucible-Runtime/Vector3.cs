using System.Runtime.InteropServices;

namespace Crucible;



[StructLayout(LayoutKind.Sequential)]
public unsafe struct Vector3
{
    private static delegate* unmanaged<Vector3, Vector3, out Vector3, void> _cross_ptr;
    private static delegate* unmanaged<Vector3, Vector3, out float, void> _dot_ptr;
    [Expose]
    public float x;
    [Expose]
    public float y;
    [Expose]
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

    public void Translate(float x, float y, float z)
    {
        this.x += x;
        this.y += y;
        this.z += z;
    }
    
    public void Translate(Vector3 translation)
    {
        this.x += translation.x;
        this.y += translation.y;
        this.z += translation.z;
    }

    public override string ToString()
    {
        return "{" + x + ", " + y + ", " + z + "}";
    }

    public static Vector3 Cross(Vector3 v1, Vector3 v2)
    {
        Vector3 returnVector = new Vector3();
        _cross_ptr(v1, v2, out returnVector);
        return returnVector;
    }

    public static unsafe float Dot(Vector3 v1, Vector3 v2)
    {
        float returnFloat = 0;
        _dot_ptr(v1, v2, out returnFloat);
        return returnFloat;
    }
    

}