namespace Crucible;

public class Scene
{
    public List<GameObject> GameObjects { get; set; } = new List<GameObject>();

    public Scene()
    {
        GameObjects.Add(new GameObject());
    }
}