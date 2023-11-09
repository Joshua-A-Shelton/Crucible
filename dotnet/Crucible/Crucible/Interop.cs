using System.Runtime.InteropServices;

namespace Crucible;

public static unsafe class Interop
{
    public static delegate* unmanaged[Cdecl]<Vector3, Vector3, out Vector3, void*> vector3Cross = (delegate* unmanaged[Cdecl]<Vector3, Vector3, out Vector3, void*>)NativeLibrary.GetExport(NativeLibrary.GetMainProgramHandle(),"crucibleVector3Cross");
    
}