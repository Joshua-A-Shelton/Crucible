using System.Runtime.InteropServices;

namespace Crucible.Core;

[StructLayout(LayoutKind.Sequential)]
public unsafe partial struct Vector3 : IEquatable<Vector3>
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
    //Magnitude or length of this vector
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
    
    [LibraryImport("Crucible", EntryPoint = "CRUCIBLE_API_Vector3DotProduct")]
    public static partial float Dot(in Vector3 v1, in Vector3 v2);
    [LibraryImport("Crucible", EntryPoint = "CRUCIBLE_API_Vector3CrossProduct")]
    private static partial void CRUCIBLE_API_Vector3CrossProduct(in Vector3 v1, in Vector3 v2, out Vector3 result);

    public static Vector3 Cross(in Vector3 v1, in Vector3 v2)
    {
        CRUCIBLE_API_Vector3CrossProduct(v1, v2, out var result);
        return result;
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

    public bool Equals(Vector3 other)
    {
        return X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
    }
}