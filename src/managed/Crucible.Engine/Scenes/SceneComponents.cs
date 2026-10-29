using System.Numerics;
using Crucible.Assets;
using Crucible.Ecs;
using Crucible.Serialization;

namespace Crucible.Scenes;

/// <summary>
/// An entity's position, rotation, and scale relative to its parent
/// (ADR 0024: left-handed, Y up, meters). Declared in C++
/// (crucible/Transform.h, ADR 0050); this half adds saving and
/// <see cref="Identity"/>.
/// </summary>
[Serialized]
public partial struct LocalTransform
{
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
        // C++ registers its components (and the propagation system) under
        // the same names, so both languages get the same IDs.
        Api.RegisterTransforms();
        Crucible.Render.Api.RegisterCamera();
        Components.Register<LocalTransform>();
        Components.Register<WorldTransform>();
        Components.Register<Crucible.Render.Camera>();
        Components.Register<MeshInstance>();
        Components.Register<SceneEntityId>();
        Components.Register<PrefabInstance>();
        SavedComponents.Add<LocalTransform>();
        SavedComponents.Add<MeshInstance>();
        SavedComponents.Add<Crucible.Render.Camera>();
        return true;
    });

    public static void Register() => _ = _registered.Value;
}
