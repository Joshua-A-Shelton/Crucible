using System.Runtime.InteropServices;

namespace Crucible.Core.Math;

[StructLayout(LayoutKind.Sequential)]
public struct Vector2
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
    
    //Magnitude of this vector
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
    
    public static float DotProduct(Vector2 a, Vector2 b)
    {
        return a.X*b.X + a.Y*b.Y;
    }
    
    public static Vector2 operator +(Vector2 v1,Vector2 v2)
    {
        return new Vector2(v1.X + v2.X, v1.Y + v2.Y);
    }
    
    public static Vector2 operator -(Vector2 v1,Vector2 v2)
    {
        return new Vector2(v1.X - v2.X, v1.Y - v2.Y);
    }

    public static Vector2 operator *(Vector2 v1, float multiplier)
    {
        return new Vector2(v1.X*multiplier, v1.Y*multiplier);
    }

    public static Vector2 operator /(Vector2 v1, float divisor)
    {
        return new Vector2(v1.X/divisor, v1.Y/divisor);
    }

    public static bool operator ==(Vector2 v1, Vector2 v2)
    {
        return v1.X == v2.X && v1.Y == v2.Y;
    }

    public static bool operator !=(Vector2 v1, Vector2 v2)
    {
        return !(v1 == v2);
    }

    public static bool Approximately(Vector2 v1, Vector2 v2)
    {
        return Math.Common.Approximately(v1.X,v2.X) && Math.Common.Approximately(v1.Y,v2.Y);
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
}