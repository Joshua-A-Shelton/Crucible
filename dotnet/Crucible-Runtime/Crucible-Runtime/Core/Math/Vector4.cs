using System.Runtime.InteropServices;

namespace Crucible.Core.Math;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct Vector4
{
    public float X;
    public float Y;
    public float Z;
    public float W;

    public Vector4()
    {
        X = 0;
        Y = 0;
        Z = 0;
        W = 1;
    }

    public Vector4(float x, float y, float z, float w)
    {
        X = x;
        Y = y;
        Z = z;
        W = w;
    }
    
    [DllImport("Crucible")]
    private static extern void CRUCIBLE_NATIVE_Vector4MatrixMultipy(ref Vector4 vector, ref Matrix4x4 matrix, out Vector4 outValue);
    [DllImport("Crucible")]
    private static extern float CRUCIBLE_NATIVE_Vector4DotProduct(ref Vector4 v1, ref Vector4 v2);
    [DllImport("Crucible")]
    private static extern void CRUCIBLE_NATIVE_Vector4Normalized(ref Vector4 vector, out Vector4 outResult);
    
    //Magnitude of this vector
    public float Magnitude()
    {
        return MathF.Sqrt(X*X+Y*Y+Z*Z+W*W);
    }
    
    //Equivalent vector with magnitude of 1
    public Vector4 Normalized()
    {
        CRUCIBLE_NATIVE_Vector4Normalized(ref this, out Vector4 result);
        return result;
    }
    
    //Keep direction, but set magnitude to 1
    public void Normalize()
    {
        this = Normalized();
    }
    
    public static float DotProduct(Vector4 a, Vector4 b)
    {
        return CRUCIBLE_NATIVE_Vector4DotProduct(ref a, ref b);
    }
    
    public static Vector4 operator +(Vector4 v1,Vector4 v2)
    {
        return new Vector4(v1.X + v2.X, v1.Y + v2.Y, v1.Z + v2.Z, v1.W + v2.W);
    }
    
    public static Vector4 operator -(Vector4 v1,Vector4 v2)
    {
        return new Vector4(v1.X - v2.X, v1.Y - v2.Y, v1.Z - v2.Z,v1.W - v2.W);
    }

    public static Vector4 operator *(Vector4 v1, float multiplier)
    {
        return new Vector4(v1.X*multiplier, v1.Y*multiplier, v1.Z*multiplier, v1.W*multiplier);
    }
    
    public static Vector4 operator *(Vector4 vector, Matrix4x4 by)
    {
        CRUCIBLE_NATIVE_Vector4MatrixMultipy(ref vector, ref by, out Vector4 result);
        return result;
    }

    public static Vector4 operator /(Vector4 v1, float divisor)
    {
        return new Vector4(v1.X/divisor, v1.Y/divisor, v1.Z/divisor, v1.W/divisor);
    }

    public static bool operator ==(Vector4 v1, Vector4 v2)
    {
        return v1.X == v2.X && v1.Y == v2.Y && v1.Z == v2.Z &&  v1.W == v2.W;
    }

    public static bool operator !=(Vector4 v1, Vector4 v2)
    {
        return !(v1 == v2);
    }

    public static bool Approximately(Vector4 v1, Vector4 v2)
    {
        return Math.Common.Approximately(v1.X,v2.X) && Math.Common.Approximately(v1.Y,v2.Y) && Math.Common.Approximately(v1.Z,v2.Z) && Math.Common.Approximately(v1.W,v2.W);
    }

    public override string ToString()
    {
        return "{"+X+", "+Y+", "+Z+", "+W+"}";
    }

    public override bool Equals(object? obj)
    {
        if (obj is Vector4 vector)
        {
            return this == vector;
        }

        return false;
    }

    public override int GetHashCode()
    {
        return (int)(X*13+Y*27+Z*31+W*71);
    }
}