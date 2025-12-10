using System.Runtime.InteropServices;
using Crucible.Core.Math;
using Crucible.Tests.Utilities;

namespace Crucible.Tests;
[Test]
public static class QuaternionTests
{
    [DllImport("Crucible")]
    private static extern void CRUCIBLE_NATIVE_QuaternionMultiply(ref Quaternion q1, ref Quaternion q2, ref Quaternion q3);
    public static TestResult MultiplyTest()
    {
        Quaternion q1 = new Quaternion(0.785398f,Vector3.Up());
        Quaternion q2 = new Quaternion(new Vector3(0.785398f,0.785398f,0));
        Quaternion actual = q1 * q2;
        Quaternion expected = new Quaternion();
        CRUCIBLE_NATIVE_QuaternionMultiply(ref q1, ref q2, ref expected);
        var same =  Quaternion.Approximately(actual, expected);
        if (same)
        {
            return TestResult.Pass();
        }
        return TestResult.Fail("Quaternion multiplication gave incorrect result");
    }

    [DllImport("Crucible")]
    private static extern float CRUCIBLE_NATIVE_QuaternionMagnitude(ref Quaternion of);

    public static TestResult MagnitudeTest()
    {
        Quaternion q1 = new Quaternion(0.785398f,Vector3.Up());
        Quaternion q2 = new Quaternion(new Vector3(0.785398f,0.785398f,0));
        Quaternion result = q1 * q2 * q1 * q2 * q2 * q1;
        float expected = result.Magnitude();
        float actual = CRUCIBLE_NATIVE_QuaternionMagnitude(ref result);
        var same = Core.Math.Common.Approximately(actual, expected);
        if (same)
        {
            return TestResult.Pass();
        }
        return TestResult.Fail("Quaternion magnitude gave incorrect result");
    }

    [DllImport("Crucible")]
    private static extern void CRUCIBLE_NATIVE_QuaternionNormalized(ref Quaternion of, ref Quaternion outResult);
    
    public static TestResult NormalizeTest()
    {
        Quaternion q1 = new Quaternion(0.785398f,Vector3.Up());
        Quaternion q2 = new Quaternion(new Vector3(0.785398f,0.785398f,0));
        Quaternion result = q1 * q2 * q1 * q2 * q2 * q1;
        Quaternion expected = new Quaternion();
        CRUCIBLE_NATIVE_QuaternionNormalized(ref result, ref expected);
        var actual = result.Normalized();
        if (!Core.Math.Common.Approximately(actual.Magnitude(), 1.0f))
        {
            return TestResult.Fail("Magnitude not 1 after normalizing");
        }
        var same = Quaternion.Approximately(expected, actual);
        if (same)
        {
            return TestResult.Pass();
        }
        return TestResult.Fail("Quaternion normalization gave incorrect result");
    }
    [DllImport("Crucible")]
    private static extern float CRUCIBLE_NATIVE_QuaternionInverse(ref Quaternion of, ref Quaternion outResult);

    public static TestResult InverseTest()
    {
        Quaternion q1 = new Quaternion(0.785398f,Vector3.Up());
        Quaternion expected = new Quaternion();
        CRUCIBLE_NATIVE_QuaternionInverse(ref q1, ref expected);
        var actual = q1.Inverse();
        var same = Quaternion.Approximately(expected, actual);
        if (!same)
        {
            return TestResult.Fail("Quaternion inverse gave incorrect result");
        }
        return TestResult.Pass();
    }
}