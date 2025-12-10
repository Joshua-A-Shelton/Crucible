using System.Runtime.InteropServices;
using Crucible.Core.Math;
using Crucible.Tests.Utilities;

namespace Crucible.Tests;
[Test]
public static class Vector2Tests
{
    [DllImport("Crucible")]
    private static extern float CRUCIBLE_NATIVE_Vector2DotProduct(ref Vector2 v1, ref Vector2 v2);
    public static TestResult DotProductTest()
    {
        Vector2 v1 = new Vector2(10, -15.3f);
        Vector2 v2 = new Vector2(-1.01f, 9);
        var expected = CRUCIBLE_NATIVE_Vector2DotProduct(ref v1, ref v2);
        var actual = Vector2.DotProduct(v1, v2);
        var same = Core.Math.Common.Approximately(expected, actual);
        if (same)
        {
            return TestResult.Pass();
        }
        return TestResult.Fail("Dot product does not match native version");
    }
    [DllImport("Crucible")]
    private static extern float CRUCIBLE_NATIVE_Vector2Magnitude(ref Vector2 v1);
    public static TestResult MagnitudeTest()
    {
        Vector2 v1 = new Vector2(10, -15.3f);
        var expected = CRUCIBLE_NATIVE_Vector2Magnitude(ref v1);
        var actual = v1.Magnitude();
        var same = Core.Math.Common.Approximately(expected, actual);
        if (same)
        {
            return TestResult.Pass();
        }
        return TestResult.Fail("Magnitude does not match native version");
    }
    [DllImport("Crucible")]
    private static extern void CRUCIBLE_NATIVE_Vector2Normalized(ref Vector2 v1, ref Vector2 outValue);
    public static TestResult NormalizedTest()
    {
        Vector2 v1 = new Vector2(10, -15.3f);
        Vector2 expected = new Vector2();
        CRUCIBLE_NATIVE_Vector2Normalized(ref v1, ref expected);
        var actual = v1.Normalized();
        var same = Vector2.Approximately(expected, actual);
        if (same)
        {
            return TestResult.Pass();
        }
        return TestResult.Fail("Normalized does not match native version");
    }
}