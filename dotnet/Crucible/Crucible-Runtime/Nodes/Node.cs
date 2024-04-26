namespace Crucible.Nodes;
public class Node
{
    public Guid Uuid { get; private set; }
    public string Name { get; set; }
    
    private List<Node> _children = new List<Node>();
    public IReadOnlyCollection<Node> Children
    {
        get { return _children; }
    }
    private Node? _parent = null;

    public Node? Parent
    {
        get
        {
            return _parent;
        }
        set
        {
            if (_parent != null)
            {
                _parent._children.Remove(this);
            }
            _parent = value;
            if (_parent != null)
            {
                _parent._children.Add(this);
            }
        }
    }

    public Node()
    {
        Name = "Game Object";
        Uuid = Guid.NewGuid();
        GameWorld.nodes.Add(Uuid,new WeakReference<Node>(this));
    }

    private Node(string name, Guid uuid)
    {
        Name = name;
        Uuid = uuid;
    }

    ~Node()
    {
        GameWorld.nodes.Remove(Uuid);
    }

    public void AddChild(Node node)
    {
        node.Parent = this;
    }

    public override string ToString()
    {
        return Name;
    }
}