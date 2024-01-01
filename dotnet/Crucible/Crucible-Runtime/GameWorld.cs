using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Arch.Core;
using Arch.System;

namespace Crucible;

public static class GameWorld
{
    internal static World _world = World.Create();
    //TODO: this may not be needed
    internal static Dictionary<Guid, WeakReference<GameObject>> gameObjects = new Dictionary<Guid, WeakReference<GameObject>>();
    private static Group<float> _systems = new Group<float>();

    public static GameObject? GetGameObjectByUUID(Guid uuid)
    {
        GameObject? go = null;
        gameObjects.GetValueOrDefault(uuid)?.TryGetTarget(out go);
        return go;
    }
    public static Scene Scene = new Scene();
    public static GameObject MainCamera = null;

    public static void Process()
    {
        _systems.Update(.0001f);
    }

    private static void GetMainCamera(ref CameraViewData cameraView, ref Matrix4x4 cameraPosition)
    {
        if (MainCamera != null)
        {
            cameraView = MainCamera.GetComponent<CameraViewData>();
            var position = MainCamera.GetComponent<Transform3D>();
            cameraPosition = position.Matrix;
        }
    }
}