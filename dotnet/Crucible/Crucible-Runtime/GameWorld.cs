using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Arch.Core;
using Arch.System;

namespace Crucible;

public static class GameWorld
{
    internal static World _world = World.Create();
    internal static Dictionary<Guid, WeakReference<GameObject>> gameObjects = new Dictionary<Guid, WeakReference<GameObject>>();
    private static Group<float> _systems = new Group<float>();

    public static GameObject? GetGameObjectByUUID(Guid uuid)
    {
        GameObject? go = null;
        gameObjects.GetValueOrDefault(uuid)?.TryGetTarget(out go);
        return go;
    }
    public static Scene Scene = new Scene();

    public static void Process()
    {
        _systems.Update(.0001f);
    }
}