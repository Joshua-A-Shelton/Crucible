using System;
using System.Runtime.InteropServices;

namespace Crucible
{
    public static class Initializer
    {
        //This must mirrror the ManagedFunctionPointers struct found in Interop.h
        [StructLayout(LayoutKind.Sequential)]
        internal unsafe struct ManagedFunctionPointers
        {
            public IntPtr RegisterUnmanagedFunction_ptr;
            public IntPtr FreeUnmanagedGCHandle_ptr;
            public IntPtr GetFunction_ptr;
            public IntPtr GetType_ptr;
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
                args->FreeUnmanagedGCHandle_ptr = Marshal.GetFunctionPointerForDelegate(Interop.FreeUnmanagedGcHandle_ptr);
                args->GetFunction_ptr = Marshal.GetFunctionPointerForDelegate(Interop.GetFunction_ptr);
                args->GetType_ptr = Marshal.GetFunctionPointerForDelegate(Interop.GetTypeHandle_ptr);
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
}