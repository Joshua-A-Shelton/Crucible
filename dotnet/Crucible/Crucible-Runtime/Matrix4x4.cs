using System.Runtime.InteropServices;

namespace Crucible;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct Matrix4x4
{
    private fixed float members[16];

    public Matrix4x4()
    {
        members[0] = 1;
        members[5] = 1;
        members[10] = 1;
        members[15] = 1;
    }
    
    public float this[int column, int row]
    {
        get
        {
            return members[column * 4 + row];
        }
        set
        {
            members[column * 4 + row] = value;
        }
    }

    private static delegate* unmanaged<ref Matrix4x4, ref Matrix4x4, ref Matrix4x4, void> _matrix_matrix_multiply;

    public static Matrix4x4 operator *(Matrix4x4 m1, Matrix4x4 m2)
    {
        Matrix4x4 result = new Matrix4x4();
        _matrix_matrix_multiply(ref m1, ref m2, ref result);
        return result;
    }

    private static delegate* unmanaged<ref Vector3, ref Quaternion, ref Vector3, ref Matrix4x4, void> _matrixFromTRS;

    public static Matrix4x4 FromTRS(Vector3 translation, Quaternion rotation, Vector3 scale)
    {
        Matrix4x4 result = new Matrix4x4();
        _matrixFromTRS(ref translation, ref rotation, ref scale, ref result);
        return result;
    }
}