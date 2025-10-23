using System.Runtime.InteropServices;
using Crucible.Core.Math;
using Crucible.Tests.Utilities;

namespace Crucible.Tests;
[Test]
public static class QuaternionTests
{
    public static bool MultiplyTest()
    {
        Quaternion q1 = new Quaternion(0.785398f,Vector3.Up());
        Quaternion q2 = new Quaternion(new Vector3(0.785398f,0.785398f,0));
        Quaternion result = q1 * q2;
        Quaternion compare = new Quaternion(0.131233543f, 1.13123357f, -0.468572199f, 0.316825867f);
        var same =  Quaternion.Approximately(result, compare);
        return same;
    }

    [DllImport("Crucible")]
    private static extern float CRUCIBLE_NATIVE_QuaternionMagnitude(ref Quaternion of);

    public static bool MagnitudeTest()
    {
        Quaternion q1 = new Quaternion(0.785398f,Vector3.Up());
        Quaternion q2 = new Quaternion(new Vector3(0.785398f,0.785398f,0));
        Quaternion result = q1 * q2 * q1 * q2 * q2 * q1;
        float expected = result.Magnitude();
        float actual = CRUCIBLE_NATIVE_QuaternionMagnitude(ref result);
        return Core.Math.Common.Approximately(actual, expected);
    }

    [DllImport("Crucible")]
    private static extern void CRUCIBLE_NATIVE_QuaternionNormalized(ref Quaternion of, ref Quaternion outResult);
    
    public static bool NormalizeTest()
    {
        Quaternion q1 = new Quaternion(0.785398f,Vector3.Up());
        Quaternion q2 = new Quaternion(new Vector3(0.785398f,0.785398f,0));
        Quaternion result = q1 * q2 * q1 * q2 * q2 * q1;
        Quaternion expected = new Quaternion();
        CRUCIBLE_NATIVE_QuaternionNormalized(ref result, ref expected);
        var actual = result.Normalized();
        if (!Core.Math.Common.Approximately(actual.Magnitude(), 1.0f))
        {
            return false;
        }
        return Quaternion.Approximately(expected, actual);
    }
    [DllImport("Crucible")]
    private static extern float CRUCIBLE_NATIVE_QuaternionInverse(ref Quaternion of, ref Quaternion outResult);

    public static bool InverseTest()
    {
        Quaternion q1 = new Quaternion(0.785398f,Vector3.Up());
        Quaternion expected = new Quaternion();
        CRUCIBLE_NATIVE_QuaternionInverse(ref q1, ref expected);
        var actual = q1.Inverse();
        return Quaternion.Approximately(expected, actual);
    }
    
}