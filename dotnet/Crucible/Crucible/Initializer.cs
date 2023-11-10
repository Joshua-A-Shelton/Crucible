using System;
using System.Runtime.InteropServices;

namespace Crucible
{
    public static class Initializer
    {
        //This must mirrror the InitArgs struct found in Interop.h
        [StructLayout(LayoutKind.Sequential)]
        internal unsafe struct EntryArgs
        {
            public IntPtr RegisterUnmanagedFunction_ptr;
        }
        
        
        public static unsafe int RuntimeEntry(IntPtr entryArgs, int argLength)
        {
            if (argLength < System.Runtime.InteropServices.Marshal.SizeOf(typeof(EntryArgs)))
            {
                return 1;
            }

            try
            {
                var args = (EntryArgs*) entryArgs;
                //Tell C++ about managed functions
                Interop.RegisterUnmanagedFunction_ptr registerUnmanagedFunction = Interop.RegisterUnmanagedFunction;
                args->RegisterUnmanagedFunction_ptr = Marshal.GetFunctionPointerForDelegate(registerUnmanagedFunction);
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