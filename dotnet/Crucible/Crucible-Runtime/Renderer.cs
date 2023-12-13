namespace Crucible;

[Component]
public struct Renderer
{
    public Mesh Mesh;
    public Renderer()
    {
        Mesh = new Mesh();
    }
}