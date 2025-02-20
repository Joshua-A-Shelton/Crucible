namespace Crucible.Core;

internal unsafe struct NodePointer
{
    private IntPtr _pointer;

    public NodePointer(IntPtr pointer)
    {
        _pointer = pointer;
    }
    
#pragma warning disable 0649
    private static delegate* unmanaged<IntPtr, void> _nodePointerLockFamily;
    private static delegate* unmanaged<IntPtr, void> _nodePointerUnlockFamily;
    
    private static delegate* unmanaged<IntPtr, UUID> _nodePointerUUID_ptr;
    private static delegate* unmanaged<IntPtr, IntPtr> _nodePointerParent_ptr;
    private static delegate* unmanaged<IntPtr, IntPtr, void> _nodePointerSetParent_ptr;
    private static delegate* unmanaged<IntPtr, int> _nodePointerChildCount_ptr;
    private static delegate* unmanaged<IntPtr, int, IntPtr> _nodePointerGetChild_ptr;
    private static delegate* unmanaged<IntPtr, IntPtr> _nodePointerAddChild_ptr;
    private static delegate* unmanaged<IntPtr, int, void> _nodePointerRemoveChildByIndex_ptr;
    private static delegate* unmanaged<IntPtr, IntPtr, void> _nodePointerRemoveChildByValue_ptr;
    
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
        _nodePointerLockFamily(_pointer);
        if (index < 0 && index >= ChildCount())
        {
            _nodePointerUnlockFamily(_pointer);
            throw new IndexOutOfRangeException();
        }
        var child = _nodePointerGetChild_ptr(_pointer, index);
        _nodePointerUnlockFamily(_pointer);
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
}