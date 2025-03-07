using System.Runtime.InteropServices;
using Crucible.Initialization;

namespace Crucible.Core;

internal unsafe struct NodePointer
{
    private IntPtr _pointer;

    public NodePointer(IntPtr pointer)
    {
        _pointer = pointer;
    }
    
#pragma warning disable 0649
    private static delegate* unmanaged<IntPtr, void> _nodePointerLockFamily_ptr;
    private static delegate* unmanaged<IntPtr, void> _nodePointerUnlockFamily_ptr;
    
    private static delegate* unmanaged<IntPtr, UUID> _nodePointerUUID_ptr;
    private static delegate* unmanaged<IntPtr, IntPtr> _nodePointerParent_ptr;
    private static delegate* unmanaged<IntPtr, IntPtr, void> _nodePointerSetParent_ptr;
    private static delegate* unmanaged<IntPtr, int> _nodePointerChildCount_ptr;
    private static delegate* unmanaged<IntPtr, int, IntPtr> _nodePointerGetChild_ptr;
    private static delegate* unmanaged<IntPtr, IntPtr> _nodePointerAddChild_ptr;
    private static delegate* unmanaged<IntPtr, int, void> _nodePointerRemoveChildByIndex_ptr;
    private static delegate* unmanaged<IntPtr, IntPtr, void> _nodePointerRemoveChildByValue_ptr;

    private static delegate* unmanaged<IntPtr, string, UInt64, UInt64, IntPtr, void> _nodePointerAddDataComponent_ptr;
    private static delegate* unmanaged<IntPtr, string, UInt64, UInt64, void> _nodePointerRemoveDataComponent_ptr;
    private static delegate* unmanaged<IntPtr, string, UInt64, UInt64, void*> _nodePointerGetDataComponent_ptr;
    
    private static delegate* unmanaged<IntPtr, string, IntPtr, IntPtr, void> _nodePointerAddReferenceComponent_ptr;
    private static delegate* unmanaged<IntPtr, string, void> _nodePointerRemoveReferenceComponent_ptr;
    private static delegate* unmanaged<IntPtr, string, IntPtr> _nodePointerGetReferenceComponent_ptr;
    
    private static delegate* unmanaged<IntPtr, ref Transform, void> _nodePointerGetCumulativeTransform_ptr;
    
#pragma warning restore 0649

    public UUID Uuid()
    {
        return _nodePointerUUID_ptr(_pointer);
    }

    public NodePointer? Parent()
    {
        var parentPointer = _nodePointerParent_ptr(_pointer);
        if (parentPointer == IntPtr.Zero)
        {
            return null;
        }

        return new NodePointer(parentPointer);
    }

    public void SetParent(NodePointer parent)
    {
        _nodePointerSetParent_ptr(_pointer,parent._pointer);
    }

    public int ChildCount()
    {
        return _nodePointerChildCount_ptr(_pointer);
    }

    public NodePointer GetChild(int index)
    {
        _nodePointerLockFamily_ptr(_pointer);
        if (index < 0 && index >= ChildCount())
        {
            _nodePointerUnlockFamily_ptr(_pointer);
            throw new IndexOutOfRangeException();
        }
        var child = _nodePointerGetChild_ptr(_pointer, index);
        _nodePointerUnlockFamily_ptr(_pointer);
        return new NodePointer(child);

    }

    public NodePointer AddChild()
    {
        var child = _nodePointerAddChild_ptr(_pointer);
        return new NodePointer(child);
    }

    public void RemoveChild(int index)
    {
        _nodePointerRemoveChildByIndex_ptr(_pointer, index);
    }

    public void RemoveChild(NodePointer child)
    {
        _nodePointerRemoveChildByValue_ptr(_pointer, child._pointer);
    }

    public void AddDataComponent<T>(T component)where T:unmanaged
    {
        var handle = GCHandle.Alloc(component, GCHandleType.Pinned);
        _nodePointerAddDataComponent_ptr(_pointer, typeof(T).FullName, (UInt64)sizeof(T), (UInt64)BlittableHelper.AlignmentOf<T>(),handle.AddrOfPinnedObject());
        handle.Free();
    }

    public void RemoveDataComponent<T>()where T:unmanaged
    {
        _nodePointerRemoveDataComponent_ptr(_pointer, typeof(T).FullName, (UInt64) sizeof(T), (UInt64) BlittableHelper.AlignmentOf<T>());
    }

    public ref T GetDataComponent<T>()where T:unmanaged
    {
        var componentPointer = _nodePointerGetDataComponent_ptr(_pointer, typeof(T).FullName, (UInt64) sizeof(T),
            (UInt64) BlittableHelper.AlignmentOf<T>());
        if (componentPointer == null)
        {
            throw new ArgumentException("Node does not have given component registered");
        }
        return ref System.Runtime.CompilerServices.Unsafe.AsRef<T>(componentPointer);
    }

    public void AddReferenceComponent<T>(T component) where T : class
    {
        var handle = GCHandle.Alloc(component, GCHandleType.Normal);
        _nodePointerAddReferenceComponent_ptr(_pointer, typeof(T).FullName, typeof(T).TypeHandle.Value, GCHandle.ToIntPtr(handle));
    }

    public void RemoveReferenceComponent<T>() where T : class
    {
        _nodePointerRemoveReferenceComponent_ptr(_pointer, typeof(T).FullName);
    }

    public T? GetReferenceComponent<T>() where T : class
    {
        var pointer = _nodePointerGetReferenceComponent_ptr(_pointer, typeof(T).FullName);
        if (pointer == IntPtr.Zero)
        {
            return null;
        }
        var handle = GCHandle.FromIntPtr(pointer);
        return (T?)handle.Target;
    }

    public Transform CumulativeTransform()
    {
        Transform t = new Transform();
        _nodePointerGetCumulativeTransform_ptr(_pointer,ref t);
        return t;
    }
    
}