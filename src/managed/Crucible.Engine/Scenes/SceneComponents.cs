using System.Numerics;
using Crucible.Assets;
using Crucible.Ecs;
using Crucible.Serialization;

namespace Crucible.Scenes;

/// <summary>
/// An entity's position, rotation, and scale relative to its parent
/// (ADR 0024: left-handed, Y up, meters). World transforms come with M5
/// (ADR 0045). Names are placeholders (docs/naming.md).
/// </summary>
[Serialized]
public partial struct LocalTransform
{
    public Vector3 Position;
    public Quaternion Rotation;
    public Vector3 Scale;

    public static LocalTransform Identity => new() { Rotation = Quaternion.Identity, Scale = Vector3.One };
}

/// <summary>One mesh of an imported model (ADR 0043, 0045), drawn from M5 on.</summary>
[Serialized]
public partial struct MeshInstance
{
    /// <summary>The model's asset: its imported MESH file.</summary>
    public AssetId Model;

    /// <summary>Which of the model's meshes.</summary>
    public int Mesh;
}

/// <summary>
/// The engine's own components, registered before scenes and prefabs use
/// them. Games' components come from their generated registration (ADR 0041).
/// </summary>
internal static class EngineComponents
{
    private static readonly Lazy<bool> _registered = new(() =>
    {
        Components.Register<LocalTransform>();
        Components.Register<MeshInstance>();
        Components.Register<SceneEntityId>();
        Components.Register<PrefabInstance>();
        SavedComponents.Add<LocalTransform>();
        SavedComponents.Add<MeshInstance>();
        return true;
    });

    public static void Register() => _ = _registered.Value;
}
