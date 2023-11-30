namespace Crucible;

public static class GameWorld
{
    public static Scene Scene = new Scene();

    public static Scene SceneObjects(ref int totalObjects)
    {
        totalObjects = Scene.GameObjects.Count;
        return Scene;
    }
}