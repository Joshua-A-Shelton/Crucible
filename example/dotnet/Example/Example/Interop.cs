using System.Runtime.InteropServices;
using Crucible.Initialization;

namespace Example;

public class Interop
{
    public static int DynamicRuntimeEntry(IntPtr entryArgs, int argLength)
    {
        Callbacks.Assignments assignments =  new Callbacks.Assignments();
        assignments.Initialize = Initialize;
        assignments.Update = Update;
        assignments.Cleanup = Cleanup;
        
        return Callbacks.AssignCallbacks(entryArgs, argLength,ref assignments);
    }

    [UnmanagedCallersOnly(EntryPoint = "StaticRuntimeEntry")]
    public static int StaticRuntimeEntry(IntPtr entryArgs, int argLength)
    {
        return DynamicRuntimeEntry(entryArgs, argLength);
    }
    
    static void Initialize()
    {
    }

    static void Update(double delta)
    {
    }

    static void Cleanup()
    {
    }
}