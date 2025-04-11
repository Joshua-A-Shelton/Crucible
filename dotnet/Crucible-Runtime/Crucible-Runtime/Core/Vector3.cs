using Crucible.Core.Utils;

namespace Crucible.Core;
using System.Runtime.InteropServices;
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
        return MathUtils.Approximately(v1.X,v2.X) && MathUtils.Approximately(v1.Y,v2.Y) && MathUtils.Approximately(v1.Z,v2.Z);
    }
    
#pragma warning disable 0649
    private static delegate* unmanaged<ref Vector3, ref Vector3, float> _vector3Dot_ptr;
    private static delegate* unmanaged<ref Vector3, ref Vector3, ref Vector3, void> _vector3Cross_ptr;
#pragma warning restore 0649
    /// <summary>
    /// Dot product of two vectors
    /// </summary>
    /// <param name="v1"></param>
    /// <param name="v2"></param>
    /// <returns></returns>
    public static float DotProduct(ref Vector3 v1, ref Vector3 v2)
    {
        return _vector3Dot_ptr(ref v1, ref v2);
    }
    /// <summary>
    /// Cross product of two vectors
    /// </summary>
    /// <param name="v1"></param>
    /// <param name="v2"></param>
    /// <returns></returns>
    public static Vector3 CrossProduct(ref Vector3 v1, ref Vector3 v2)
    {
        Vector3 v3 = new Vector3();
        _vector3Cross_ptr(ref v1, ref v2, ref v3);
        return v3;
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