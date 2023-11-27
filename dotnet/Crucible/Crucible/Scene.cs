namespace Crucible;

public class Scene
{
    public List<GameObject> GameObjects { get; set; }

    public Scene()
    {
        GameObjects.Add(new GameObject());
    }
}