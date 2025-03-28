namespace Crucible.Core;

public unsafe class MeshInstance
{
    public enum VertexAttribute
    {
        ///X, Y, and Z position (floats)
        POSITION_3D,
        ///X and Y position (floats)
        POSITION_2D,
        ///Normal vector of vertex (X, Y, and Z floats)
        NORMAL,
        ///UV texture coordinates
        UV,
        ///R, G, B, A values (byte)
        COLOR,
        ///Bone ID (UINT_32) Bone weight (float) (x4)
        BONE_WEIGHT,
        ///Size of the VertexAttribute enum
        VERTEX_ATTRIBUTE_ENUM_SIZE
    }
    internal IntPtr _pointer = IntPtr.Zero;
    
#pragma warning disable 0649
    private static delegate* unmanaged<ref IntPtr,byte*,UInt64, void> _meshInstanceInitializeSerialized_ptr;
    private static delegate* unmanaged<ref IntPtr, string, void> _meshInstanceFromFile_ptr;
    private static delegate* unmanaged<IntPtr, void> _meshInstanceCleanup_ptr;
#pragma warning restore 0649

    public MeshInstance(Span<byte> meshData)
    {
        fixed (byte* ptr = meshData)
        {
            _meshInstanceInitializeSerialized_ptr(ref _pointer, ptr, (UInt64)meshData.Length);
        }

        if (_pointer == IntPtr.Zero)
        {
            throw new InvalidDataException("Unable to create mesh from data");
        }
    }

    public MeshInstance(string modelFile)
    {
        _meshInstanceFromFile_ptr(ref _pointer, modelFile);
        if (_pointer == IntPtr.Zero)
        {
            throw new InvalidDataException("Unable to create mesh from data");
        }
    }

    ~MeshInstance()
    {
        _meshInstanceCleanup_ptr(_pointer);
    }
}