using System.Runtime.InteropServices;

namespace Crucible.Core;

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

    public override bool Equals(object? obj)
    {
        if (obj is NodeReference nodeRef)
        {
            return this == nodeRef;
        }
        return false;
    }

    public override int GetHashCode()
    {
        return _uuid.GetHashCode();
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

    internal NodePointer PointerFromUUID()
    {
        var pointer = _nodeReferenceFromUUID_ptr(ref _uuid);
        if (pointer == IntPtr.Zero)
        {
            throw new NullReferenceException();
        }

        return new NodePointer(pointer);
    }

    public string Name
    {
        get
        {
            var pointer = PointerFromUUID();
            return pointer.Name;
        }
        set
        {
            var pointer = PointerFromUUID();
            pointer.Name = value;
        }
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

    public NodeReference AddChild(string name)
    {
        var pointer = PointerFromUUID();
        var child = pointer.AddChild(name);
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

    public void AddDataComponent<T>(T component)where T:unmanaged
    {
        var pointer = PointerFromUUID();
        pointer.AddDataComponent(component);
    }
    
    public void RemoveDataComponent<T>()where T:unmanaged
    {
        var pointer = PointerFromUUID();
        pointer.RemoveDataComponent<T>();
    }

    public ref T GetDataComponent<T>() where T : unmanaged
    {
        var pointer = PointerFromUUID();
        return ref pointer.GetDataComponent<T>();
    }

    public void AddReferenceComponent<T>(T component) where T : class
    {
        var pointer = PointerFromUUID();
        pointer.AddReferenceComponent(component);
    }

    public void RemoveReferenceComponent<T>() where T : class
    {
        var pointer = PointerFromUUID();
        pointer.RemoveReferenceComponent<T>();
    }

    public T? GetReferenceComponent<T>() where T : class
    {
        var pointer = PointerFromUUID();
        return pointer.GetReferenceComponent<T>();
    }
}