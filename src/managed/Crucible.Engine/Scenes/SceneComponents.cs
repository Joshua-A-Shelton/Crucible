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
/// A 2D entity's place relative to its parent (ADR 0093): in the XY plane,
/// turned by <c>Angle</c> radians about Z, scaled in X and Y, at Z =
/// <c>Depth</c>. Declared in C++ (crucible/Transform.h); this half adds
/// saving and a scale of 1.
/// </summary>
[Serialized]
public partial struct Transform2D
{
    public Transform2D() => Scale = Vector2.One;

    public static Transform2D Identity => new();
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
        Crucible.Physics2D.Api.RegisterPhysics2D();
        Crucible.Audio.Api.RegisterAudio();
        // The pose runtime (ADR 0079) and the frame's debug lines.
        Crucible.Animation.Api.RegisterAnimation();
        Crucible.Debug.Api.RegisterDebugLines();
        Components.Register<LocalTransform>();
        Components.Register<Transform2D>();
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
        Components.Register<Crucible.Physics2D.RigidBody2D>();
        Components.Register<Crucible.Physics2D.Collider2D>();
        Components.Register<Crucible.Audio.AudioSource>();
        Components.RegisterTag<Crucible.Audio.AudioListener>();
        Components.Register<Crucible.Animation.Animator>();
        Components.Register<Crucible.Animation.PoseHandle>();
        Components.Register<SceneEntityId>();
        Components.RegisterManaged<EntityName>();
        Components.Register<PrefabInstance>();
        SavedComponents.Add<LocalTransform>();
        SavedComponents.Add<Transform2D>();
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
        SavedComponents.Add<Crucible.Physics2D.RigidBody2D>();
        SavedComponents.Add<Crucible.Physics2D.Collider2D>();
        SavedComponents.Add<Crucible.Audio.AudioSource>();
        SavedComponents.AddTag<Crucible.Audio.AudioListener>();
        SavedComponents.Add<Crucible.Animation.Animator>();
        return true;
    });

    public static void Register() => _ = _registered.Value;
}
