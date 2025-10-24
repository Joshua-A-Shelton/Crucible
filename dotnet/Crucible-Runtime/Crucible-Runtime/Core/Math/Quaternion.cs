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
    private static extern void CRUCIBLE_NATIVE_QuaternionFromAngleAxis(float angle, ref Vector3 axis, out Quaternion outResult);
    [DllImport("Crucible")]
    private static extern void CRUCIBLE_NATIVE_QuaternionFromEulerAngles(ref Vector3 pitchYawRoll, out Quaternion outResult);
    [DllImport("Crucible")]
    private static extern void CRUCIBLE_NATIVE_QuaternionInverse(ref Quaternion of, out Quaternion outResult);
    [DllImport("Crucible")]
    private static extern void CRUCIBLE_NATIVE_QuaternionMultiply(ref Quaternion q1, ref Quaternion q2, out Quaternion outResult);
    [DllImport("Crucible")]
    private static extern void CRUCIBLE_NATIVE_QuaternionNormalized(ref Quaternion of, out Quaternion outResult);
    
    
    public Quaternion(float angle, Vector3 axis)
    {
        axis.Normalize();
        CRUCIBLE_NATIVE_QuaternionFromAngleAxis(angle, ref axis, out this);
    }
    
    public Quaternion(float pitch, float yaw, float roll)
    {
        Vector3 pitchYawRoll = new Vector3(pitch, yaw, roll);
        CRUCIBLE_NATIVE_QuaternionFromEulerAngles(ref pitchYawRoll, out this);
    }

    public Quaternion(Vector3 pitchYawRoll)
    {
        CRUCIBLE_NATIVE_QuaternionFromEulerAngles(ref pitchYawRoll, out this);
    }

    public static Quaternion operator *(Quaternion a, Quaternion b)
    {
        CRUCIBLE_NATIVE_QuaternionMultiply(ref a, ref b, out Quaternion outResult);
        return outResult;
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
        //this is actually faster than calling c++
        return MathF.Sqrt(X*X+Y*Y+Z*Z+W*W);
    }
    
    //Equivalent Quaternion with magnitude of 1
    public Quaternion Normalized()
    {
        CRUCIBLE_NATIVE_QuaternionNormalized(ref this, out Quaternion outResult);
        return outResult;
    }
    
    //Keep rotation, but set magnitude to 1
    public void Normalize()
    {
        this = Normalized();
    }

    public Quaternion Inverse()
    {
        CRUCIBLE_NATIVE_QuaternionInverse(ref this, out Quaternion q);
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