using System.Runtime.InteropServices;

namespace Crucible.Core;

public interface IDeferredInit
{
    void Initialize();

    private static void CallDeferredInitialize(IntPtr deferredInitHandle)
    {
        var deferredInit = (IDeferredInit?)GCHandle.FromIntPtr(deferredInitHandle).Target;
        deferredInit?.Initialize();
    }
}