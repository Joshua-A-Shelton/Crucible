using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Crucible.Core;
/// <summary>
/// Represents a weak link to a node, used to prevent having Node from referencing a deleted node in unmanaged code
/// </summary>
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

    public Node Acquire()
    {
        var pointer = _nodeReferenceFromUUID_ptr(ref _uuid);
        return new Node(pointer);
    }

    public bool TryAcquire(out Node node)
    {
        var pointer = _nodeReferenceFromUUID_ptr(ref _uuid);
        if (pointer != IntPtr.Zero)
        {
            node = new Node(pointer);
            return true;
        }
        node = new Node(IntPtr.Zero);
        return false;
    }
}