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

    public void QueueMesh(BinaryReader reader, Mesh.MeshBufferAccessibility accessibility, Action<Mesh> onFinish)
    {
        Mesh.ReadFromStreamBatchedInit(reader,accessibility,this,onFinish);
    }

    public void QueueTexture2D(BinaryReader reader, Action<Texture2D> callback)
    {
        Texture2D.ReadFromStreamBatchInit(reader,this,callback);
    }

    public void QueueUpdateTexture2D(Texture2D texture,IEnumerable<Texture2D.UpdateRegion> pixelUpdates, Texture.PixelAspects aspect, Action<Texture2D.Region>? callback = null)
    {
        texture.SetPixels(pixelUpdates,aspect,this,callback);
    }

    public void Process()
    {
        CRUCIBLE_NATIVE_DeferredJobQueueProcess(_deferredJobQueueHandle);
    }

    internal void AddDeferredInit(IDeferredInit init)
    {
        GCHandle handle = GCHandle.Alloc(init);
        CRUCIBLE_NATIVE_DeferredJobQueueAddDeferredInit(_deferredJobQueueHandle, GCHandle.ToIntPtr(handle));
    }
}