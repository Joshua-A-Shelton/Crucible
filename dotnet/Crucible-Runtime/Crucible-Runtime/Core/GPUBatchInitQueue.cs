using System.Runtime.InteropServices;

namespace Crucible.Core;

public partial class GPUBatchInitQueue
{
    
    private IntPtr _deferredJobQueueHandle =  IntPtr.Zero;
    internal IntPtr DeferredJobQueueHandle{get{return _deferredJobQueueHandle;}}

    GPUBatchInitQueue()
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

    public void Process()
    {
        CRUCIBLE_NATIVE_DeferredJobQueueProcess(_deferredJobQueueHandle);
    }
}