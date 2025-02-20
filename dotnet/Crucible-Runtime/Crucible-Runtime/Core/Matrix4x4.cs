using System.Runtime.InteropServices;
using System.Text;

namespace Crucible.Core;

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
#pragma warning disable 0649
    private static delegate* unmanaged<ref Matrix4x4, ref Matrix4x4, ref Matrix4x4, void> _matrixMultiply_ptr;
#pragma warning restore 0649
    
    public static Matrix4x4 operator *(Matrix4x4 m1, Matrix4x4 m2)
    {
        Matrix4x4 result = new Matrix4x4();
        _matrixMultiply_ptr(ref m1, ref m2, ref result);
        return result;
    }

    public override string ToString()
    {
        StringBuilder sb = new StringBuilder();
        for (int row = 0; row < 4; row++)
        {
            sb.Append("| ");
            for (int column = 0; column < 4; column++)
            {
                sb.Append(this[column, row]);
                if (column < 3)
                {
                    sb.Append(", ");
                }
            }

            sb.Append(" |");
        }
        return sb.ToString();
    }
}