namespace Crucible;

public class Scene
{
    public List<GameObject> GameObjects { get; set; } = new List<GameObject>();

    public Scene()
    {
        GameObject go = new GameObject();
        go.Name = "Top";
        go.AddComponent(new Transform3D());
        //go.AddComponent(new Renderer());
        GameObject go1 = new GameObject();
        go1.Name = "Bottom";
        go1.AddComponent(new Vector4());
        go.AddChild(go1);
        GameObjects.Add(go);
    }
}