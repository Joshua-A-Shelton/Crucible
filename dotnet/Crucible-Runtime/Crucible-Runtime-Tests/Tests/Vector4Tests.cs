using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using Crucible.Core.Math;
using Crucible.Tests.Utilities;

namespace Crucible.Tests;
[Test]
public static class Vector4Tests
{
    [DllImport("Crucible")]
    private static extern float CRUCIBLE_NATIVE_Vector4DotProduct(ref Vector4 vector1, ref Vector4 vector2);
    public static bool DotProduct()
    {
        Vector4 v1 = new Vector4(-12.3f, 6, .002f, 5);
        Vector4 v2 = new Vector4(4, 2.2f, 9.8f, 1);
        var expected = CRUCIBLE_NATIVE_Vector4DotProduct(ref v1, ref v2);
        var actual = Vector4.DotProduct(v1, v2);
        return Core.Math.Common.Approximately(expected, actual);
    }
    
    [DllImport("Crucible")]
    private static extern float CRUCIBLE_NATIVE_Vector4Magnitude(ref Vector4 vector);
    public static bool MagnitudeTest()
    {
        Vector4 v1 = new Vector4(-12.3f, 6, .002f, 5);
        var expected = CRUCIBLE_NATIVE_Vector4Magnitude(ref v1);
        var actual = v1.Magnitude();
        return Core.Math.Common.Approximately(expected, actual);
    }
    [DllImport("Crucible")]
    private static extern void CRUCIBLE_NATIVE_Vector4Normalized(ref Vector4 vector, ref Vector4 outValue);
    public static bool NormalizeTest()
    {
        Vector4 v1 = new Vector4(-12.3f, 6, .002f, 5);
        var expected = new Vector4();
        CRUCIBLE_NATIVE_Vector4Normalized(ref v1, ref expected);
        var actual = v1.Normalized();
        return Vector4.Approximately(expected, actual);
    }
    [DllImport("Crucible")]
    private static extern void CRUCIBLE_NATIVE_Vector4MatrixMultipy(ref Vector4 vector, ref Matrix4x4 matrix, ref Vector4 outValue);
    public static bool MatrixMultiplyTest()
    {
        Vector4 vector1 = new Vector4(-12.3f, 6, .002f, 5);
        Matrix4x4 matrix1 = new Matrix4x4(new Vector3(0,1,0),new Quaternion(75,new Vector3(0,1,0)),new Vector3(1,1,1));
        Vector4 expected = new Vector4();
        CRUCIBLE_NATIVE_Vector4MatrixMultipy(ref vector1, ref matrix1,ref expected);
        var actual = vector1 * matrix1;
        return Vector4.Approximately(expected, actual);
    }
}