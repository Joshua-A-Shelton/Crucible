using System.Runtime.InteropServices;
using Crucible.Core.Math;
using Crucible.Tests.Utilities;

namespace Crucible.Tests;
[Test]
public static class Matrix4x4Tests
{
    [DllImport("Crucible")]
    private static extern void CRUCIBLE_NATIVE_Matrix4X4Multiply(ref Matrix4x4 a, ref Matrix4x4 b, ref Matrix4x4 outResult);
    
    public static bool MatrixMultiplicationTest()
    {
        Matrix4x4 a = new Matrix4x4(new Vector3(0,0,0), new Quaternion(25,new Vector3(0,1,0)),new Vector3(1, 1,1));
        Matrix4x4 b = new Matrix4x4(new Vector3(10,15,-12), new Quaternion(25,new Vector3(1,1,.5f).Normalized()),new Vector3(2,.5f,1));
        Matrix4x4 result = a * b;
        Matrix4x4 expected;
        CRUCIBLE_NATIVE_Matrix4X4Multiply(ref a, ref b, ref expected);
        bool matches = true;
        for (var column = 0; column < 4; column++)
        {
            for (var row = 0; row < 4; row++)
            {
                if (!Core.Math.Common.Approximately(expected[column, row], result[column, row]))
                {
                    matches = false;
                    break;
                }
            }
        }
        return matches;
    }
}