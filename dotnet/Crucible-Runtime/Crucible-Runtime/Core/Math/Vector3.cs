using System.Runtime.InteropServices;
using Crucible.Initialization;

namespace Crucible.Core.Math;
/// <summary>
/// Direction and magnitude, or point in 3d space
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct Vector3
{
    public float X;
    public float Y;
    public float Z;

    public Vector3()
    {
        X = 0;
        Y = 0;
        Z = 0;
    }

    public Vector3(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public static Vector3 Up()
    {
        return new Vector3(0, 1, 0);
    }

    public static Vector3 Right()
    {
        return new Vector3(1, 0, 0);
    }

    public static Vector3 Forward()
    {
        return new Vector3(0, 0, 1);
    }
    
    //Magnitude of this vector
    public float Magnitude()
    {
        return MathF.Sqrt(X*X+Y*Y+Z*Z);
    }
    //Equivalent vector with magnitude of 1
    public Vector3 Normalized()
    {
        var mag = Magnitude();
        return new Vector3(X/mag, Y/mag, Z/mag);
    }
    //Keep direction, but set magnitude to 1
    public void Normalize()
    {
        this = Normalized();
    }

    private static delegate* unmanaged<ref Vector3, ref Vector3, float> NATIVE_Vector3Dot = (delegate* unmanaged<ref Vector3, ref Vector3, float>)Native.GetExportedSymbol("Crucible",nameof(NATIVE_Vector3Dot));
    public static float DotProduct(Vector3 vector1, Vector3 vector2)
    {
        return NATIVE_Vector3Dot(ref vector1, ref vector2);
    }

    public static Vector3 operator +(Vector3 v1,Vector3 v2)
    {
        return new Vector3(v1.X + v2.X, v1.Y + v2.Y, v1.Z + v2.Z);
    }
    
    public static Vector3 operator -(Vector3 v1,Vector3 v2)
    {
        return new Vector3(v1.X - v2.X, v1.Y - v2.Y, v1.Z - v2.Z);
    }

    public static bool operator ==(Vector3 v1, Vector3 v2)
    {
        return v1.X == v2.X && v1.Y == v2.Y && v1.Z == v2.Z;
    }

    public static bool operator !=(Vector3 v1, Vector3 v2)
    {
        return !(v1 == v2);
    }

    public static bool Approximately(Vector3 v1, Vector3 v2)
    {
        return Math.Common.Approximately(v1.X,v2.X) && Math.Common.Approximately(v1.Y,v2.Y) && Math.Common.Approximately(v1.Z,v2.Z);
    }

    public override string ToString()
    {
        return "{"+X+", "+Y+", "+Z+"}";
    }

    public override bool Equals(object? obj)
    {
        if (obj is Vector3 vector)
        {
            return this == vector;
        }

        return false;
    }

    public override int GetHashCode()
    {
        return (int)(X*13+Y*27+Z*31);
    }
}