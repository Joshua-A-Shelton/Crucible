using Crucible.Nodes;

namespace Crucible;

public class Scene
{
    public List<Node> Nodes { get; set; } = new List<Node>();

    public Scene()
    {
        Node go = new Node3D();
        go.Name = "Top";
        Nodes.Add(go);
    }
}