namespace Crucible.Core;

public class MeshRenderer
{
    public MeshInstance Mesh { get; private set; }
    public Material Material { get; private set; }
    public byte Priority { get; private set; }
    
    public MeshRenderer(MeshInstance mesh, string shader, byte priority)
    {
        Mesh = mesh;
        Material = new Material(shader);
        Priority = priority;
    }

    public MeshRenderer(Span<byte> meshData, string shader, byte priority)
    {
        Mesh = new MeshInstance(meshData);
        Material = new Material(shader);
        Priority = priority;
    }

    public MeshRenderer(string modelFile, string shader, byte priority)
    {
        Mesh = new MeshInstance(modelFile);
        Material = new Material(shader);
        Priority = priority;
    }

}