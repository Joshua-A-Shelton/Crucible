using System.Runtime.InteropServices;
using Crucible.Tests.Utilities;

namespace Crucible.Tests;
using Crucible.Core.Math;
[Test]
public static class Vector3Tests
{
    [DllImport("Crucible")]
    private static extern float CRUCIBLE_NATIVE_Vector3DotProduct(ref Vector3 v1, ref Vector3 v2);
    
    public static bool DotTest()
    {
        Vector3 vector1 = new Vector3(.125f,-32.9061f,15);
        Vector3 vector2 = new Vector3(100,3,-20.3f);
        var expected = CRUCIBLE_NATIVE_Vector3DotProduct(ref vector1, ref vector2);
        var actual = Vector3.DotProduct(vector1, vector2);
        return Core.Math.Common.Approximately(expected, actual);
    }
    
    [DllImport("Crucible")]
    private static extern void CRUCIBLE_NATIVE_Vector3CrossProduct(ref Vector3 v1, ref Vector3 v2, ref Vector3 outValue);

    public static bool CrossTest()
    {
        Vector3 vector1 = new Vector3(.125f,-32.9061f,15);
        Vector3 vector2 = new Vector3(100,3,-20.3f);
        Vector3 expected = new Vector3();
        CRUCIBLE_NATIVE_Vector3CrossProduct(ref vector1, ref vector2, ref expected);
        var actual = Vector3.CrossProduct(vector1, vector2);
        return Vector3.Approximately(expected, actual);
    }
    [DllImport("Crucible")]
    private static extern float CRUCIBLE_NATIVE_Vector3Magnitude(ref Vector3 v1);
    public static bool MagnitudeTest()
    {
        Vector3 vector1 = new Vector3(.125f,-32.9061f,15);
        var expected = CRUCIBLE_NATIVE_Vector3Magnitude(ref vector1);
        var actual = vector1.Magnitude();
        return Core.Math.Common.Approximately(expected, actual);
    }
    [DllImport("Crucible")]
    private static extern void CRUCIBLE_NATIVE_Vector3Normalized(ref Vector3 vector, ref Vector3 outValue);
    public static bool NormalizedTest()
    {
        Vector3 vector1 = new Vector3(.125f,-32.9061f,15);
        Vector3 expected = new Vector3();
        CRUCIBLE_NATIVE_Vector3Normalized(ref vector1, ref expected);
        var actual = vector1.Normalized();
        return Vector3.Approximately(expected, actual);
    }
    [DllImport("Crucible")]
    private static extern void CRUCIBLE_NATIVE_Vector3MatrixMultipy(ref Vector3 vector, ref Matrix4x4 matrix, ref Vector3 outValue);
    public static bool MatrixMultiplyTest()
    {
        Vector3 vector1 = new Vector3(.125f,-32.9061f,15);
        Matrix4x4 matrix1 = new Matrix4x4(new Vector3(0,1,0),new Quaternion(75,new Vector3(0,1,0)),new Vector3(1,1,1));
        Vector3 expected = new Vector3();
        CRUCIBLE_NATIVE_Vector3MatrixMultipy(ref vector1, ref matrix1,ref expected);
        var actual = vector1 * matrix1;
        return Vector3.Approximately(expected, actual);
    }
}