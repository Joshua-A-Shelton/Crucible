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
        public IntPtr GetManagedFunctionDelegate_ptr;
        public IntPtr NewInstance_ptr;
        public IntPtr FreeInstance_ptr;
        public IntPtr InvokeInstanceMethod_ptr;
        public IntPtr InvokeInstanceMethodReferenceReturn;
        public IntPtr InvokeInstanceMethodValueReturn;
        public IntPtr GameManagerInitialize_ptr;
        public IntPtr GameManagerCleanUp_ptr;
        public IntPtr ManagedEnvironmentCleanup_ptr;
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
            args->GetManagedFunctionDelegate_ptr = Marshal.GetFunctionPointerForDelegate(Interop.GetManagedFunctionDelegatePtr);
            args->NewInstance_ptr = Marshal.GetFunctionPointerForDelegate(Interop.NewInstancePtr);
            args->FreeInstance_ptr = Marshal.GetFunctionPointerForDelegate(Interop.FreeInstancePtr);
            args->InvokeInstanceMethod_ptr = Marshal.GetFunctionPointerForDelegate(Interop.InvokeInstanceMethodPtr);
            args->InvokeInstanceMethodReferenceReturn = Marshal.GetFunctionPointerForDelegate(Interop.InvokeInstanceMethodReferenceReturnPtr);
            args->InvokeInstanceMethodValueReturn = Marshal.GetFunctionPointerForDelegate(Interop.InvokeInstanceMethodValueReturnPtr);
            args->GameManagerInitialize_ptr = Marshal.GetFunctionPointerForDelegate(Interop.GameManagerInitializePtr);
            args->GameManagerCleanUp_ptr = Marshal.GetFunctionPointerForDelegate(Interop.GameManagerCleanUpPtr);
            args->ManagedEnvironmentCleanup_ptr = Marshal.GetFunctionPointerForDelegate(Interop.ManagedEnvoirnmentCleanup);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
        return 0;
    }

    public static void ManagedCleanup()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }
}