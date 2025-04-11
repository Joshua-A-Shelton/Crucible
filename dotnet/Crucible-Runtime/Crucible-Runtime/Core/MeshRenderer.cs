namespace Crucible.Core;
/// <summary>
/// Component used to render a mesh
/// </summary>
public class MeshRenderer
{
    /// <summary>
    /// The mesh to draw
    /// </summary>
    public MeshInstance Mesh { get; set; }
    /// <summary>
    /// The material the mesh will be drawn with
    /// </summary>
    public Material Material { get; set; }
    /// <summary>
    /// The render order priority to draw, lower priority gets drawn first
    /// </summary>
    public byte Priority { get; set; }
    /// <summary>
    /// Create a Mesh Renderer
    /// </summary>
    /// <param name="mesh">The Mesh to draw</param>
    /// <param name="shader">The shader name to get the material for</param>
    /// <param name="priority">The render order priority to draw, lower priority gets drawn first</param>
    public MeshRenderer(MeshInstance mesh, string shader, byte priority)
    {
        Mesh = mesh;
        Material = new Material(shader);
        Priority = priority;
    }
    /// <summary>
    /// Create a Mesh Renderer
    /// </summary>
    /// <param name="meshData">Raw Mesh data formatted according to crucible mesh chunk format</param>
    /// <param name="shader">The shader name to get the material for</param>
    /// <param name="priority">The render order priority to draw, lower priority gets drawn first</param>
    public MeshRenderer(Span<byte> meshData, string shader, byte priority)
    {
        Mesh = new MeshInstance(meshData);
        Material = new Material(shader);
        Priority = priority;
    }
    /// <summary>
    /// Create a Mesh Renderer
    /// </summary>
    /// <param name="modelFile">The file to load the mesh from</param>
    /// <param name="shader">The shader name to get the material for</param>
    /// <param name="priority">The render order priority to draw, lower priority gets drawn first</param>
    public MeshRenderer(string modelFile, string shader, byte priority)
    {
        Mesh = new MeshInstance(modelFile);
        Material = new Material(shader);
        Priority = priority;
    }

}