using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Crucible.Initialization;
//This must match CallbackLocations defined in Callbacks.h
[StructLayout(LayoutKind.Sequential)]
public struct Callbacks
{
    private IntPtr Initialize;
    private IntPtr Update;
    private IntPtr Cleanup;
    
    public delegate void VoidCallback();
    public delegate void UpdateCallback(double delta);
    
    public struct Assignments
    {
        public VoidCallback Initialize;
        public UpdateCallback Update;
        public VoidCallback Cleanup;
    }

    public static unsafe int AssignCallbacks(IntPtr entryArgs, int argLength, ref Assignments assignments)
    {
        Initialize_ptr = assignments.Initialize;
        Update_ptr = assignments.Update;
        Cleanup_ptr = assignments.Cleanup;
        
        var callbacks = (Callbacks*)(entryArgs);
        callbacks->Initialize = Marshal.GetFunctionPointerForDelegate(Initialize_ptr);
        callbacks->Update = Marshal.GetFunctionPointerForDelegate(Update_ptr);
        callbacks->Cleanup = Marshal.GetFunctionPointerForDelegate(Cleanup_ptr);
        return 0;
    }
    
    

    private static VoidCallback? Initialize_ptr;
    private static UpdateCallback? Update_ptr;
    private static VoidCallback? Cleanup_ptr;

}