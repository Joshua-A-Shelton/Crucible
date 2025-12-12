using System.Runtime.InteropServices;

namespace Crucible.Core;

public partial class GPUBatchInitQueue
{
    
    private IntPtr _deferredJobQueueHandle =  IntPtr.Zero;
    internal IntPtr DeferredJobQueueHandle{get{return _deferredJobQueueHandle;}}

    public GPUBatchInitQueue()
    {
        _deferredJobQueueHandle = CRUCIBLE_NATIVE_DeferredJobQueueNew();
    }
    
    ~GPUBatchInitQueue()
    {
        CRUCIBLE_NATIVE_DeferredJobQueueDelete(_deferredJobQueueHandle);
    }

    [LibraryImport("Crucible")]
    private static partial IntPtr CRUCIBLE_NATIVE_DeferredJobQueueNew();
    [LibraryImport("Crucible")]
    private static partial void CRUCIBLE_NATIVE_DeferredJobQueueDelete(IntPtr deferredJobQueueHandle);
    [LibraryImport("Crucible")]
    private static partial void CRUCIBLE_NATIVE_DeferredJobQueueProcess(IntPtr deferredJobQueueHandle);
    [LibraryImport("Crucible")]
    private static partial void CRUCIBLE_NATIVE_DeferredJobQueueAddDeferredInit(IntPtr deferredJobQueueHandle, IntPtr deferredInitHandle);

    public void Process()
    {
        CRUCIBLE_NATIVE_DeferredJobQueueProcess(_deferredJobQueueHandle);
    }

    private void AddDeferredInit(IDeferredInit init)
    {
        GCHandle handle = GCHandle.Alloc(init);
        CRUCIBLE_NATIVE_DeferredJobQueueAddDeferredInit(_deferredJobQueueHandle, GCHandle.ToIntPtr(handle));
    }
}