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

    public static bool MagnitudeTest()
    {
        return false;
    }

    public static bool NormalizeTest()
    {
        return false;
    }

    public static bool InverseTest()
    {
        return false;
    }
    
}