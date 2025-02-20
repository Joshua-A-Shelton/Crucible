namespace Crucible.Core;

public unsafe struct NodeReference
{
    private UUID _uuid;

    internal NodeReference(UUID id)
    {
        _uuid = id;
    }
    
#pragma warning disable 0649
    private static delegate* unmanaged<ref UUID, IntPtr> _nodeReferenceFromUUID_ptr;
#pragma warning restore 0649
    
    public static NodeReference? FromUUID(UUID uuid)
    {
        var pointer = _nodeReferenceFromUUID_ptr(ref uuid);
        if (pointer != IntPtr.Zero)
        {
            return new NodeReference(uuid);
        }

        return null;
    }

    public bool IsValid()
    {
        var pointer = _nodeReferenceFromUUID_ptr(ref _uuid);
        return pointer != IntPtr.Zero;
    }

    private NodePointer PointerFromUUID()
    {
        var pointer = _nodeReferenceFromUUID_ptr(ref _uuid);
        if (pointer == IntPtr.Zero)
        {
            throw new NullReferenceException();
        }

        return new NodePointer(pointer);
    }

    public NodeReference? Parent()
    {
        var pointer = PointerFromUUID();
        var parent = pointer.Parent();
        if (parent.HasValue)
        {
            return new NodeReference(parent.Value.Uuid());
        }

        return null;
    }

    public int ChildCount()
    {
        var pointer = PointerFromUUID();
        return pointer.ChildCount();
    }

    public NodeReference GetChild(int childIndex)
    {
        var pointer = PointerFromUUID();
        var child = pointer.GetChild(childIndex);
        return new NodeReference(child.Uuid());
    }

    public NodeReference AddChild()
    {
        var pointer = PointerFromUUID();
        var child = pointer.AddChild();
        return new NodeReference(child.Uuid());
    }

    public void RemoveChild(int childIndex)
    {
        var pointer = PointerFromUUID();
        pointer.RemoveChild(childIndex);
    }

    public void RemoveChild(NodeReference child)
    {
        var pointer = PointerFromUUID();
        var childPointer = child.PointerFromUUID();
        pointer.RemoveChild(childPointer);
    }
}