using System.Runtime.InteropServices;

namespace Crucible.Core.Math;
[StructLayout(LayoutKind.Sequential)]
public unsafe struct Matrix4x4
{
    private fixed float _data[16];

    public Matrix4x4()
    {
        _data[0] = 1.0f;
        _data[5] = 1.0f;
        _data[10] = 1.0f;
        _data[15] = 1.0f;
    }

    public Matrix4x4(Span<float> data)
    {
        fixed (float* source = data)
        {
            fixed (float* dest = _data)
            {
                Buffer.MemoryCopy(source,dest,16,16);
            }
        }
    }

    public Matrix4x4(float[] data)
    {
        if (data == null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        if (data.Count() != 16)
        {
            throw new ArgumentOutOfRangeException(nameof(data), "data must have 16 elements");
        }
        fixed (float* source = data)
        {
            fixed (float* dest = _data)
            {
                Buffer.MemoryCopy(source,dest,16,16);
            }
        }
        
    }

    public Matrix4x4(Vector3 position, Quaternion rotation, Vector3 scale)
    {
        CRUCIBLE_NATIVE_Matrix4X4FromPositionRotationScale(ref position, ref rotation, ref scale, ref this);
    }
    
    [DllImport("Crucible")]
    private static extern void CRUCIBLE_NATIVE_Matrix4X4Multiply(ref Matrix4x4 a, ref Matrix4x4 b, ref Matrix4x4 outMatrix);
    [DllImport("Crucible")]
    private static extern void CRUCIBLE_NATIVE_Matrix4X4FromPositionRotationScale(ref Vector3 position, ref Quaternion rotation, ref Vector3 scale, ref Matrix4x4 outMatrix);

    public static Matrix4x4 operator *(Matrix4x4 a, Matrix4x4 b)
    {
        Matrix4x4 outMatrix = new Matrix4x4();
        CRUCIBLE_NATIVE_Matrix4X4Multiply(ref a, ref b, ref outMatrix);
        return outMatrix;
    }

    public float this[int i, int j]
    {
        get { return _data[i * 4 + j]; }
        set { _data[i * 4 + j] = value; }
    }
    
}