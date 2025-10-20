using Crucible.Tests.Utilities;

namespace Crucible.Tests;
using Crucible.Core.Math;
[Test]
public static class Vector3Tests
{
    public static bool DotTest()
    {
        Vector3 v1 = new Vector3(1.2f, 5.3f, -5.0f);
        Vector3 v2 = new Vector3(4.3f, .023f, 1.0f);
        var dot = Vector3.DotProduct(v1, v2);
        if (Core.Math.Common.Approximately(dot,0.2819004f))
        {
            return true;
        }
        return false;
    }

    public static bool CrossTest()
    {
        Vector3 v1 = new Vector3(1.2f, 5.3f, -5.0f);
        Vector3 v2 = new Vector3(4.3f, .023f, 1.0f);
        var cross = Vector3.CrossProduct(v1, v2);
        Vector3 expected = new Vector3(5.415f, -22.7f, -22.762402f);
        return Vector3.Approximately(cross, expected);
    }
    
    public static bool MagnitudeTest()
    {
        return false;
    }

    public static bool NormalizeTest()
    {
        return false;
    }
    
    public static bool MatrixMultiplyTest()
    {
        return false;
    }
}