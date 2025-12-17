using System.Runtime.InteropServices;

namespace Crucible.Core;

internal interface IDeferredInit
{
    void Initialize();

    private static void CallDeferredInitialize(IntPtr deferredInitHandle)
    {
        var deferredInit = (IDeferredInit?)GCHandle.FromIntPtr(deferredInitHandle).Target;
        deferredInit?.Initialize();
    }
}

internal class DeferredInit<T>: IDeferredInit
{
    public T InitObject { get; set; }
    public Action<T>  Callback { get; set; }

    public DeferredInit(T obj, Action<T> callback)
    {
        InitObject = obj;
        Callback = callback;
    }
    public void Initialize()
    {
        Callback?.Invoke(InitObject);
    }
}