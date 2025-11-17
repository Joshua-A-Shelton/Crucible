using System.Runtime.InteropServices;

namespace Crucible.Core;
/// <summary>
/// Represents a weak link to a node, used to prevent having Node from referencing a deleted node in unmanaged code
/// </summary>
public partial class NodeReference
{
    private UUID _uuid;

    internal NodeReference(UUID uuid)
    {
        _uuid = uuid;
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
    
    [LibraryImport("Crucible")]
    private static partial IntPtr CRUCIBLE_NATIVE_NodeReferenceFromUUID(ref UUID uuid);
    
    public static NodeReference? FromUUID(UUID uuid)
    {
        var pointer = CRUCIBLE_NATIVE_NodeReferenceFromUUID(ref uuid);
        if (pointer != IntPtr.Zero)
        {
            return new NodeReference(uuid);
        }

        return null;
    }

    public Node Acquire()
    {
        var pointer = CRUCIBLE_NATIVE_NodeReferenceFromUUID(ref _uuid);
        return new Node(pointer);
    }

    public bool TryAcquire(out Node node)
    {
        var pointer = CRUCIBLE_NATIVE_NodeReferenceFromUUID(ref _uuid);
        if (pointer != IntPtr.Zero)
        {
            node = new Node(pointer);
            return true;
        }
        node = new Node(IntPtr.Zero);
        return false;
    }
}