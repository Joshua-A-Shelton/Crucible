using System.Runtime.InteropServices;

namespace Crucible.Core;
/// <summary>
/// Class that holds a reference to a node that is destroyed alongside the NodeKeeper
/// </summary>
public partial class NodeKeeper
{
    private static HashSet<UUID> _keptNodes = new HashSet<UUID>();
    /// <summary>
    /// Reference to kept node
    /// </summary>
    public NodeReference? KeptNode { get; private set; }
    private bool _relinquished =  true;
    public NodeKeeper()
    {
        var node = new Node(CRUCIBLE_NATIVE_NodeNew());
        KeptNode = new NodeReference(node.Uuid());
        _relinquished = false;
        _keptNodes.Add(node.Uuid());
    }

    internal NodeKeeper(Node node)
    {
        if (_keptNodes.Contains(node.Uuid()))
        {
            throw new InvalidOperationException("Node is already kept");
        }
        else
        {
            _keptNodes.Add(node.Uuid());
        }
        KeptNode = new NodeReference(node.Uuid());
        _relinquished = false;
    }

    ~NodeKeeper()
    {
        if (!_relinquished)
        {
            if (!ReferenceEquals(KeptNode, null))
            {
                _keptNodes.Remove(KeptNode.nodeID());
                var node = KeptNode.Acquire();
                if (!node.IsNull)
                {
                    CRUCIBLE_NATIVE_NodeDelete(node.Pointer);
                }
            }
            _relinquished = true;
        }
    }

    [LibraryImport("Crucible")]
    private static partial IntPtr CRUCIBLE_NATIVE_NodeNew();
    [LibraryImport("Crucible")]
    private static partial void CRUCIBLE_NATIVE_NodeDelete(IntPtr node);
    
    /// <summary>
    /// Stop managing the node, and set a new parent for it
    /// </summary>
    /// <param name="newParent">The new node that will manage this node as it's new parent node</param>
    void Relinquish(Node newParent)
    {
        if (!ReferenceEquals(KeptNode, null))
        {
            _keptNodes.Remove(KeptNode.nodeID());
            var node = KeptNode.Acquire();
            node.SetParent(newParent);
            _relinquished = true;
            KeptNode = null;
        }
        else
        {
            throw new NullReferenceException("Node keeper no longer managing node");
        }
    }
}