using System.Runtime.InteropServices;

namespace Crucible.Core;
[StructLayout(LayoutKind.Sequential)]
public unsafe struct Vector2 : IEquatable<Vector2>
{
    public float X;
    public float Y;

    public Vector2()
    {
        X = 0;
        Y = 0;
    }
    public Vector2(float x, float y)
    {
        X = x;
        Y = y;
    }
    //Magnitude or length of this vector
    public float Magnitude()
    {
        return MathF.Sqrt(X*X+Y*Y);
    }
    //Equivalent vector with magnitude of 1
    public Vector2 Normalized()
    {
        var mag = Magnitude();
        return new Vector2(X/mag, Y/mag);
    }
    //Keep direction, but set magnitude to 1
    public void Normalize()
    {
        this = Normalized();
    }
    
    public static Vector2 operator +(Vector2 v1,Vector2 v2)
    {
        return new Vector2(v1.X + v2.X, v1.Y + v2.Y);
    }
    
    public static Vector2 operator -(Vector2 v1,Vector2 v2)
    {
        return new Vector2(v1.X - v2.X, v1.Y - v2.Y);
    }

    public static bool operator ==(Vector2 v1, Vector2 v2)
    {
        return v1.X == v2.X && v1.Y == v2.Y;
    }

    public static bool operator !=(Vector2 v1, Vector2 v2)
    {
        return !(v1 == v2);
    }
    
    public override string ToString()
    {
        return "{"+X+", "+Y+"}";
    }

    public override bool Equals(object? obj)
    {
        if (obj is Vector2 vector)
        {
            return this == vector;
        }

        return false;
    }

    public override int GetHashCode()
    {
        return (int)(X*13+Y*27);
    }

    public bool Equals(Vector2 other)
    {
        return X.Equals(other.X) && Y.Equals(other.Y);
    }
}