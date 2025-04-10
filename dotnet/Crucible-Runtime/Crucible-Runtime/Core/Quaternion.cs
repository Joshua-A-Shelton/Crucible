using System.Drawing;
using System.Runtime.InteropServices;
using Crucible.Core.Utils;

namespace Crucible.Core;
[StructLayout(LayoutKind.Sequential)]
public unsafe struct Quaternion
{
    public float X;
    public float Y;
    public float Z;
    public float W;

    public Quaternion()
    {
        W = 1;
        X = 0;
        Y = 0;
        Z = 0;
    }

    public Quaternion(float w, float x, float y, float z)
    {
        W = w;
        X = x;
        Y = y;
        Z = z;
    }

    public static bool Approximately(Quaternion q1, Quaternion q2)
    {
        return (
                MathUtils.Approximately(q1.W,q2.W) &&
                MathUtils.Approximately(q1.X,q2.X) &&
                MathUtils.Approximately(q1.Y,q2.Y) && 
                MathUtils.Approximately(q1.Z,q2.Z)
                ) 
               ||
               (
                   MathUtils.Approximately(-q1.W,q2.W) &&
                   MathUtils.Approximately(-q1.X,q2.X) &&
                   MathUtils.Approximately(-q1.Y,q2.Y) && 
                   MathUtils.Approximately(-q1.Z,q2.Z)
               );
    }
    
#pragma warning disable 0649
    private static delegate* unmanaged<ref Quaternion, float, ref Vector3, void> _quaternionFromAngleAxis_ptr;
#pragma warning restore 0649

    public Quaternion(float angle, Vector3 axis)
    {
        axis.Normalize();
        _quaternionFromAngleAxis_ptr(ref this, angle, ref axis);
    }

    public override string ToString()
    {
        return "{"+W+", "+X+"i, "+Y+"j, "+Z+"k}";
    }
}