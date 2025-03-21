namespace Crucible.Core;

public unsafe class MeshInstance
{
    private IntPtr _meshPointer;

    internal MeshInstance(IntPtr meshPointer)
    {
        _meshPointer = meshPointer;
    }

    ~MeshInstance()
    {
        
    }
}