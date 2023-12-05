using System.Runtime.CompilerServices;

namespace Crucible;

public static class GameWorld
{
    public static Scene Scene = new Scene();

    internal static Dictionary<Guid, WeakReference<GameObject>> gameObjects = new Dictionary<Guid, WeakReference<GameObject>>();
}