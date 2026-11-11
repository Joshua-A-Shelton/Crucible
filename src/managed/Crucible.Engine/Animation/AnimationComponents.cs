using System.Numerics;
using Crucible.Assets;
using Crucible.Ecs;
using Crucible.Serialization;

namespace Crucible.Animation;

// The pose runtime's components (ADR 0079, 0081), declared in C++
// (crucible/Animation.h); this half adds saving, the defaults an Animator
// starts with, and the calls that act on a character's pose.

/// <summary>
/// A character (ADR 0079): the skeleton its pose has (a model asset) and the
/// clip its base plays, or a state machine choosing its clips (ADR 0081).
/// Skinned meshes on it or below it use its pose.
/// </summary>
[Serialized]
public partial struct Animator
{
    public Animator()
    {
        Speed = 1;
        Loop = true;
    }

    /// <summary>
    /// Plays a clip on the base, fading from the one playing over
    /// <paramref name="fadeSeconds"/> (0 cuts); sets <see cref="Clip"/>.
    /// Throws if the entity has no Animator or the clip can't be read, or
    /// while a state machine plays the base.
    /// </summary>
    public static void Play(World world, Entity entity, AssetId clip, float fadeSeconds = 0) =>
        Api.Play(world.Live, entity, clip, fadeSeconds);

    /// <summary>
    /// Sets a layer (0 to 7) over the base: a new clip starts from its start,
    /// and the weight fades to <see cref="LayerDesc.Weight"/>. The mask's
    /// entries are joints (<see cref="FindJoint"/>) and everything below them;
    /// none masks nothing.
    /// </summary>
    public static void SetLayer(World world, Entity entity, int layer, LayerDesc desc,
        ReadOnlySpan<MaskEntry> mask = default) =>
        Api.SetLayer(world.Live, entity, (uint)layer, desc, mask);

    /// <summary>Fades a layer out over <paramref name="fadeSeconds"/>, then clears it.</summary>
    public static void ClearLayer(World world, Entity entity, int layer, float fadeSeconds = 0) =>
        Api.ClearLayer(world.Live, entity, (uint)layer, fadeSeconds);

    /// <summary>A joint of the entity's skeleton by name, or -1.</summary>
    public static int FindJoint(World world, Entity entity, string name) =>
        Api.FindJoint(world.Live, entity, name);

    /// <summary>The joint's matrix relative to the entity, as last posed; identity without a pose.</summary>
    public static Matrix4x4 JointModelTransform(World world, Entity entity, int joint) =>
        Api.JointModelTransform(world.Live, entity, joint);

    /// <summary>Debug lines (<see cref="Crucible.Debug.DebugLines"/>) along every posed skeleton of the world, this frame.</summary>
    public static void DrawSkeletons(World world, Vector4 color) => Api.DrawSkeletons(world.Live, color);

    /// <summary>Sets a Float parameter of the entity's state machine; throws if it has none of that name.</summary>
    public static void SetFloat(World world, Entity entity, string parameter, float setting) =>
        Api.SetFloat(world.Live, entity, parameter, setting);

    /// <summary>Sets a Bool parameter of the entity's state machine; throws if it has none of that name.</summary>
    public static void SetBool(World world, Entity entity, string parameter, bool setting) =>
        Api.SetBool(world.Live, entity, parameter, setting);

    /// <summary>Sets a Trigger, until a transition that tests it is taken; throws if there's none of that name.</summary>
    public static void Trigger(World world, Entity entity, string parameter) =>
        Api.Trigger(world.Live, entity, parameter);

    /// <summary>The state the entity's state machine is in (<see cref="FindState"/>), or -1.</summary>
    public static int CurrentState(World world, Entity entity) => Api.CurrentState(world.Live, entity);

    /// <summary>A state of the entity's state machine by name, or -1.</summary>
    public static int FindState(World world, Entity entity, string name) => Api.FindState(world.Live, entity, name);

    /// <summary>
    /// The world's events from the last animation update, by timestamp, into
    /// <paramref name="into"/> as far as it holds; returns how many there are.
    /// </summary>
    public static int Events(World world, Span<AnimationEvent> into) => (int)Api.Events(world.Live, into);

    /// <summary>The entity's open windows, into <paramref name="into"/> as far as it holds; returns how many there are.</summary>
    public static int OpenWindows(World world, Entity entity, Span<OpenWindow> into) =>
        (int)Api.OpenWindows(world.Live, entity, into);

    /// <summary>How far root motion moved the entity in the last update (zero without <see cref="RootMotion"/>).</summary>
    public static RootMotion LastRootMotion(World world, Entity entity) => Api.RootMotion(world.Live, entity);

    /// <summary>Sets an IK slot (0 to 3) to two-bone IK; call each frame as the target moves.</summary>
    public static void SetTwoBoneIk(World world, Entity entity, int slot, TwoBoneIkDesc desc) =>
        Api.SetTwoBoneIk(world.Live, entity, (uint)slot, desc);

    /// <summary>Sets an IK slot (0 to 3) to look-at; call each frame as the target moves.</summary>
    public static void SetLookAt(World world, Entity entity, int slot, LookAtDesc desc) =>
        Api.SetLookAt(world.Live, entity, (uint)slot, desc);

    /// <summary>Fades an IK slot out over <paramref name="fadeSeconds"/>, then clears it.</summary>
    public static void ClearIk(World world, Entity entity, int slot, float fadeSeconds = 0) =>
        Api.ClearIk(world.Live, entity, (uint)slot, fadeSeconds);
}

/// <summary>Something an animation update passed: an event, or a window opening or closing (ADR 0081).</summary>
public partial struct AnimationEvent
{
    /// <summary>The hash an event or window is known by, from its name in the sidecar.</summary>
    public static uint NameOf(string name) => Api.EventName(name);
}

/// <summary>Two-bone IK toward a target, bending toward a pole (ADR 0081).</summary>
public partial struct TwoBoneIkDesc
{
    public TwoBoneIkDesc()
    {
        Weight = 1;
    }
}

/// <summary>Turning a joint to look at a target, within a cone (ADR 0081).</summary>
public partial struct LookAtDesc
{
    public LookAtDesc()
    {
        Aim = Vector3.UnitZ;
        MaxAngle = MathF.PI / 2;
        Weight = 1;
    }
}

/// <summary>A layer's clip, weight, and blending (ADR 0079).</summary>
public partial struct LayerDesc
{
    public LayerDesc()
    {
        Weight = 1;
        Speed = 1;
        Loop = true;
    }
}
