using System.Runtime.CompilerServices;

namespace Crucible;

public static class GameWorld
{
    internal static Dictionary<Guid, WeakReference<GameObject>> gameObjects = new Dictionary<Guid, WeakReference<GameObject>>();

    public static GameObject? GetGameObjectByUUID(Guid uuid)
    {
        GameObject? go = null;
        gameObjects.GetValueOrDefault(uuid)?.TryGetTarget(out go);
        return go;
    }
    public static Scene Scene = new Scene();
}