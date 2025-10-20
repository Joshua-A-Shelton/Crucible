using System.Runtime.InteropServices;

namespace Crucible.Core.Math;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct Quaternion
{
    public float X;
    public float Y;
    public float Z;
    public float W;

    public Quaternion(float x, float y, float z, float w)
    {
        X = x;
        Y = y;
        Z = z;
        W = w;
    }
    [DllImport("Crucible")]
    private static extern void CRUCIBLE_NATIVE_QuaternionFromAngleAxis(float angle, ref Vector3 axis, ref Quaternion outResult);
    [DllImport("Crucible")]
    private static extern void CRUCIBLE_NATIVE_QuaternionFromEulerAngles(ref Vector3 pitchYawRoll, ref Quaternion outResult);
    [DllImport("Crucible")]
    private static extern void CRUCIBLE_NATIVE_QuaternionInverse(ref Quaternion of, ref Quaternion outResult);
    
    
    public Quaternion(float angle, Vector3 axis)
    {
        axis.Normalize();
        CRUCIBLE_NATIVE_QuaternionFromAngleAxis(angle, ref axis, ref this);
    }
    
    public Quaternion(float pitch, float yaw, float roll)
    {
        Vector3 pitchYawRoll = new Vector3(pitch, yaw, roll);
        CRUCIBLE_NATIVE_QuaternionFromEulerAngles(ref pitchYawRoll, ref this);
    }

    public Quaternion(Vector3 pitchYawRoll)
    {
        CRUCIBLE_NATIVE_QuaternionFromEulerAngles(ref pitchYawRoll, ref this);
    }

    public static Quaternion operator *(Quaternion a, Quaternion b)
    {
        float w = a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z;
        float x = a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y;
        float y = a.W * b.Y + a.Y * b.W + a.Z * b.X - a.X * b.Z;
        float z = a.W * b.Z + a.Z * b.Z + a.X * b.Y - a.Y * b.X;
        return new Quaternion(x, y, z, w);
    }

    public override string ToString()
    {
        return "{"+W+", "+X+"i, "+Y+"j, "+Z+"k}";
    }
    
    public static bool operator ==(Quaternion v1, Quaternion v2)
    {
        return v1.X == v2.X && v1.Y == v2.Y && v1.Z == v2.Z &&  v1.W == v2.W;
    }

    public static bool operator !=(Quaternion v1, Quaternion v2)
    {
        return !(v1 == v2);
    }

    public static bool Approximately(Quaternion v1, Quaternion v2)
    {
        return Math.Common.Approximately(v1.X,v2.X) && Math.Common.Approximately(v1.Y,v2.Y) && Math.Common.Approximately(v1.Z,v2.Z) && Math.Common.Approximately(v1.W,v2.W);
    }
    
    //Magnitude of this Quaternion
    public float Magnitude()
    {
        return MathF.Sqrt(X*X+Y*Y+Z*Z+W*W);
    }
    
    //Equivalent Quaternion with magnitude of 1
    public Quaternion Normalized()
    {
        var mag = Magnitude();
        return new Quaternion(X/mag, Y/mag, Z/mag, W/mag);
    }
    
    //Keep rotation, but set magnitude to 1
    public void Normalize()
    {
        this = Normalized();
    }

    public Quaternion Inverse()
    {
        Quaternion q = new Quaternion();
        CRUCIBLE_NATIVE_QuaternionInverse(ref this, ref q);
        return q;
    }
    
    public override bool Equals(object? obj)
    {
        if (obj is Quaternion vector)
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