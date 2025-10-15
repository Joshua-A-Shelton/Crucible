using System.Runtime.InteropServices;

namespace Crucible.Initialization;

public class Initializer
{
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct ManagedFunctionPointers
    {
        public IntPtr RegisterUnmanagedFunction_ptr;
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
            args->RegisterUnmanagedFunction_ptr = Marshal.GetFunctionPointerForDelegate(Interop.RegisterUnmanagedFunction_ptr);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
        return 0;
    }
}