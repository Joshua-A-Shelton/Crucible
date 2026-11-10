using System.Numerics;
using Crucible.Assets;
using Crucible.Ecs;
using Crucible.Serialization;

namespace Crucible.Animation;

// The pose runtime's components (ADR 0079), declared in C++
// (crucible/Animation.h); this half adds saving, the defaults an Animator
// starts with, and the calls that act on a character's pose.

/// <summary>
/// A character (ADR 0079): the skeleton its pose has (a model asset) and the
/// clip its base plays. Skinned meshes on it or below it use its pose.
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
    /// Throws if the entity has no Animator or the clip can't be read.
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
