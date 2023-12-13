using System.Runtime.InteropServices;

namespace Crucible;

public unsafe class Mesh
{
    private static delegate* unmanaged<IntPtr> _createMesh_ptr;
    private static delegate* unmanaged<IntPtr, void> _deleteMesh_ptr;
    private IntPtr _lowLevelHandle;

    public Mesh()
    {
        _lowLevelHandle = _createMesh_ptr();
    }

    ~Mesh()
    {
        _deleteMesh_ptr(_lowLevelHandle);
    }
}