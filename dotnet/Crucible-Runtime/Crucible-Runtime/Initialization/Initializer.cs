using System;
using System.Runtime.InteropServices;

namespace Crucible.Initialization;

public static class Initializer
{
    //This must mirror the ManagedFunctionPointers struct found in ScriptingEngine.h
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct ManagedFunctionPointers
    {
        public IntPtr RegisterUnmanagedFunction_ptr;
        public IntPtr NewInstance_ptr;
        public IntPtr FreeUnmanagedGCHandle_ptr;
        public IntPtr GetFunction_ptr;
        public IntPtr InvokeInstanceMethod_ptr;
        public IntPtr InvokeInstanceMethodReturnReference_ptr;
        public IntPtr InvokeInstanceMethodReturnValue_ptr;
        public IntPtr GetType_ptr;
        public IntPtr GetMeshRenderData_ptr;
        public IntPtr GetSkeleton_ptr;
        public IntPtr GetCamera_ptr;
        public IntPtr LoadManagedDll_ptr;
        public IntPtr UnloadContext_ptr;
        public IntPtr UnloadAllContexts_ptr;
    }
        
        
    public static unsafe int RuntimeEntry(IntPtr entryArgs, int argLength)
    {
        if (argLength < System.Runtime.InteropServices.Marshal.SizeOf(typeof(ManagedFunctionPointers)))
        {
            return 1;
        }

        try
        {
            var args = (ManagedFunctionPointers*) entryArgs;
            //Tell C++ about managed functions
            args->RegisterUnmanagedFunction_ptr = Marshal.GetFunctionPointerForDelegate(Interop.RegisterUnmanagedFunction_ptr);
            args->NewInstance_ptr = Marshal.GetFunctionPointerForDelegate(Interop.NewInstance_ptr);
            args->FreeUnmanagedGCHandle_ptr = Marshal.GetFunctionPointerForDelegate(Interop.FreeUnmanagedGcHandle_ptr);
            args->GetFunction_ptr = Marshal.GetFunctionPointerForDelegate(Interop.GetFunction_ptr);
            args->InvokeInstanceMethod_ptr = Marshal.GetFunctionPointerForDelegate(Interop.InvokeInstanceMethod_ptr);
            args->InvokeInstanceMethodReturnReference_ptr = Marshal.GetFunctionPointerForDelegate(Interop.InvokeInstanceMethodWithReturnValueByReferenceDelegate_ptr);
            args->InvokeInstanceMethodReturnValue_ptr = Marshal.GetFunctionPointerForDelegate(Interop.InvokeInstanceMethodWithReturnValueByValueDelegate_ptr);
            args->GetType_ptr = Marshal.GetFunctionPointerForDelegate(Interop.GetTypeHandle_ptr);
            args->GetMeshRenderData_ptr = Marshal.GetFunctionPointerForDelegate(Interop.GetMeshRenderData_ptr);
            args->GetSkeleton_ptr = Marshal.GetFunctionPointerForDelegate(Interop.GetSkeleton_ptr);
            args->GetCamera_ptr = Marshal.GetFunctionPointerForDelegate(Interop.GetCamera_ptr);
            args->LoadManagedDll_ptr = Marshal.GetFunctionPointerForDelegate(Interop.LoadLibrary_ptr);
            args->UnloadContext_ptr = Marshal.GetFunctionPointerForDelegate(Interop.UnloadLibrary_ptr);
            args->UnloadAllContexts_ptr = Marshal.GetFunctionPointerForDelegate(Interop.UnloadAllContexts_ptr);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
        return 0;
    }
}