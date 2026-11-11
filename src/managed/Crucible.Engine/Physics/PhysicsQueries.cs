using System.Numerics;
using Crucible.Ecs;

namespace Crucible.Physics;

/// <summary>What a query hits (ADR 0090): colliders on its layers, except one entity's and, unless asked for, triggers.</summary>
public partial struct QueryFilter
{
    /// <summary>All layers, nothing left out, no triggers.</summary>
    public static QueryFilter Default => new() { Layers = uint.MaxValue };
}

/// <summary>
/// Physics queries (ADR 0090), on the world the game plays in: they see
/// where bodies were at the last tick, so a body moved this frame is seen
/// there after the next Physics.Sync; while editing there's nothing to hit.
/// </summary>
public static class Physics
{
    /// <summary>The first hit along a ray (<paramref name="direction"/> normalized here), or one whose <c>Hit</c> is false.</summary>
    public static RayHit Raycast(World world, Vector3 from, Vector3 direction, float distance, QueryFilter? filter = null) =>
        Api.Raycast(world.Live, from, Vector3.Normalize(direction), distance, filter ?? QueryFilter.Default);

    /// <summary>The first hit of a box, sphere, or capsule swept along a ray; one touching something at the start hits at distance 0.</summary>
    public static RayHit ShapeCast(World world, QueryShape shape, Vector3 from, Quaternion rotation, Vector3 direction,
        float distance, QueryFilter? filter = null) =>
        Api.ShapeCast(world.Live, shape, from, rotation, Vector3.Normalize(direction), distance, filter ?? QueryFilter.Default);

    /// <summary>The entities whose colliders overlap a shape, each once, as many as fit in <paramref name="into"/>; returns how many there are.</summary>
    public static int Overlap(World world, QueryShape shape, Vector3 at, Quaternion rotation, Span<Entity> into,
        QueryFilter? filter = null) =>
        (int)Api.Overlap(world.Live, shape, at, rotation, filter ?? QueryFilter.Default, into);
}
