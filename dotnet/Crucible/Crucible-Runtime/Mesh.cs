using System.Runtime.InteropServices;

namespace Crucible;

public unsafe class Mesh
{
    private static delegate* unmanaged<IntPtr> createMesh_ptr;
    private static delegate* unmanaged<IntPtr, void> deleteMesh_ptr;
    private IntPtr _lowLevelHandle;

    public Mesh()
    {
        _lowLevelHandle = createMesh_ptr();
    }

    ~Mesh()
    {
        deleteMesh_ptr(_lowLevelHandle);
    }
}