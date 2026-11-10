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

/// <summary>
/// One mesh of an imported model (ADR 0043, 0045), drawn from M5 on.
/// Declared in C++ (crucible/MeshInstance.h, ADR 0051); this half adds
/// saving.
/// </summary>
[Serialized]
public partial struct MeshInstance;

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
        Api.RegisterMeshInstance();
        Crucible.Render.Api.RegisterLights();
        // The physics and audio contracts (ADR 0075): their components, and
        // the null defaults' systems unless an implementation's came first.
        Crucible.Physics.Api.RegisterPhysics();
        Crucible.Audio.Api.RegisterAudio();
        Components.Register<LocalTransform>();
        Components.Register<WorldTransform>();
        Components.Register<Crucible.Render.Camera>();
        Components.Register<Crucible.Render.PostProcess>();
        Components.Register<MeshInstance>();
        Components.Register<LoadedMesh>();
        Components.Register<Crucible.Render.DirectionalLight>();
        Components.Register<Crucible.Render.PointLight>();
        Components.Register<Crucible.Render.SpotLight>();
        Components.Register<Crucible.Render.AmbientLight>();
        Components.Register<Crucible.Render.Sky>();
        Components.Register<Crucible.Physics.RigidBody>();
        Components.Register<Crucible.Physics.Collider>();
        Components.Register<Crucible.Physics.Joint>();
        Components.Register<Crucible.Physics.CharacterController>();
        Components.Register<Crucible.Audio.AudioSource>();
        Components.RegisterTag<Crucible.Audio.AudioListener>();
        Components.Register<SceneEntityId>();
        Components.RegisterManaged<EntityName>();
        Components.Register<PrefabInstance>();
        SavedComponents.Add<LocalTransform>();
        SavedComponents.AddManaged<EntityName>();
        SavedComponents.Add<MeshInstance>();
        SavedComponents.Add<Crucible.Render.Camera>();
        SavedComponents.Add<Crucible.Render.PostProcess>();
        SavedComponents.Add<Crucible.Render.DirectionalLight>();
        SavedComponents.Add<Crucible.Render.PointLight>();
        SavedComponents.Add<Crucible.Render.SpotLight>();
        SavedComponents.Add<Crucible.Render.AmbientLight>();
        SavedComponents.Add<Crucible.Render.Sky>();
        SavedComponents.Add<Crucible.Physics.RigidBody>();
        SavedComponents.Add<Crucible.Physics.Collider>();
        SavedComponents.Add<Crucible.Physics.Joint>();
        SavedComponents.Add<Crucible.Physics.CharacterController>();
        SavedComponents.Add<Crucible.Audio.AudioSource>();
        SavedComponents.AddTag<Crucible.Audio.AudioListener>();
        return true;
    });

    public static void Register() => _ = _registered.Value;
}
