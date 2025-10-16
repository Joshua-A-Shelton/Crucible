using System.Runtime.InteropServices;

namespace Crucible.Initialization;

public class Initializer
{
    /// <summary>
    /// This must match the ManagedFunctionPointers struct in ManagedFunctionPointers.h
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct ManagedFunctionPointers
    {
        public IntPtr LoadAssembly_ptr;
        public IntPtr UnloadContext_ptr;
        public IntPtr UnloadAllContextsPtr;
        public IntPtr GetManagedType_ptr;
        public IntPtr GetManagedFunction_ptr;
        public IntPtr NewInstance_ptr;
        public IntPtr FreeInstance_ptr;
        public IntPtr ManagedInitialize_ptr;
        public IntPtr ManagedCleanUp_ptr;
    }
    
    private static unsafe int RuntimeEntry(IntPtr entryArgs, int argLength)
    {
        if (argLength < System.Runtime.InteropServices.Marshal.SizeOf(typeof(ManagedFunctionPointers)))
        {
            return 1;
        }

        try
        {
            var args = (ManagedFunctionPointers*) entryArgs;
            args->LoadAssembly_ptr = Marshal.GetFunctionPointerForDelegate(Interop.LoadAssemblyPtr);
            args->UnloadContext_ptr = Marshal.GetFunctionPointerForDelegate(Interop.UnloadContextPtr);
            args->UnloadAllContextsPtr = Marshal.GetFunctionPointerForDelegate(Interop.UnloadAllContextsPtr);
            args->GetManagedType_ptr = Marshal.GetFunctionPointerForDelegate(Interop.GetManagedTypePtr);
            args->GetManagedFunction_ptr = Marshal.GetFunctionPointerForDelegate(Interop.GetManagedFunctionPtr);
            args->NewInstance_ptr = Marshal.GetFunctionPointerForDelegate(Interop.NewInstancePtr);
            args->FreeInstance_ptr = Marshal.GetFunctionPointerForDelegate(Interop.FreeInstancePtr);
            args->ManagedInitialize_ptr = Marshal.GetFunctionPointerForDelegate(Interop.ManagedInitializePtr);
            args->ManagedCleanUp_ptr = Marshal.GetFunctionPointerForDelegate(Interop.ManagedCleanUpPtr);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
        return 0;
    }
}