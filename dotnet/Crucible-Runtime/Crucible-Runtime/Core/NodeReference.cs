﻿namespace Crucible.Core;

public unsafe struct NodeReference
{
    private UUID _uuid;

    internal NodeReference(UUID id)
    {
        _uuid = id;
    }

    public static bool operator ==(NodeReference n1, NodeReference n2)
    {
        return n1._uuid == n2._uuid;
    }

    public static bool operator !=(NodeReference n1, NodeReference n2)
    {
        return n1._uuid != n2._uuid;
    }
    

    public static void checkValid()
    {
        if (_nodeReferenceFromUUID_ptr == null)
        {
            Console.WriteLine("pointer is not mapped");
        }
        else
        {
            Console.WriteLine("pointer is mapped");
        }
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

    public void SetParent(NodeReference newParent)
    {
        var pointer = PointerFromUUID();
        var newParentPointer = newParent.PointerFromUUID();
        pointer.SetParent(newParentPointer);
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