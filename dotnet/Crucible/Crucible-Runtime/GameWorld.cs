using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Crucible.Nodes;


namespace Crucible;

public static class GameWorld
{
    //TODO: this may not be needed
    internal static Dictionary<Guid, WeakReference<Node>> nodes = new Dictionary<Guid, WeakReference<Node>>();

    public static Node? GetGameObjectByUUID(Guid uuid)
    {
        Node? go = null;
        nodes.GetValueOrDefault(uuid)?.TryGetTarget(out go);
        return go;
    }
    public static Scene Scene = new Scene();
    public static Node MainCamera = null;
    
}