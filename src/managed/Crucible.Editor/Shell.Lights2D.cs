using System.Numerics;
using Crucible.Ecs;
using Crucible.Render2D;
using Crucible.Scenes;
using Crucible.UI;

namespace Crucible.Editor;

/// <summary>Create > 2D's lights (ADR 0113).</summary>
public enum Light2DKind
{
    Point,
    Spot,
    Ambient,
}

// 2D lights in the editor (ADR 0113): made from the hierarchy's Create > 2D,
// and a selected one's range (and a spot's cone) drawn over the viewports.
internal sealed partial class Shell
{
    private static readonly Vector4 _lightLine = new(1f, 0.85f, 0.35f, 0.9f), _lightEdge = new(0.08f, 0.08f, 0.1f, 0.8f);

    /// <summary>A 2D light entity under <paramref name="parent"/> (or at the top), selected.</summary>
    public Entity CreateLight2D(Entity parent, Light2DKind kind)
    {
        if (!_context.HasWorld)
        {
            return Entity.Null;
        }
        Entity entity = World.CreateEntity();
        World.Set(entity, Transform2D.Identity);
        World.Set(entity, new SceneEntityId { Value = SceneIds.NewId() });
        string name;
        switch (kind)
        {
            case Light2DKind.Point:
                World.Set(entity, new PointLight2D());
                name = "Point Light";
                break;
            case Light2DKind.Spot:
                World.Set(entity, new SpotLight2D());
                name = "Spot Light";
                break;
            default:
                World.Set(entity, new AmbientLight2D());
                name = "Ambient Light";
                break;
        }
        World.SetManaged(entity, new EntityName { Value = name });
        if (HasLive(parent))
        {
            World.SetParent(entity, parent);
        }
        RecordCreated($"Create {name}", entity);
        Selection.Selected = entity;
        return entity;
    }

    /// <summary>The selected 2D light's range, and a spot's cone, over a viewport.</summary>
    internal void DrawLight2D(Canvas canvas, EditorCamera camera, Vector2 size)
    {
        Entity entity = Selection.Selected;
        if (!HasLive(entity))
        {
            return;
        }
        (float range, float outer, float inner) = World.Has(entity, Components.Of<PointLight2D>())
            ? (World.Read<PointLight2D>(entity).Range, 0f, 0f)
            : World.Has(entity, Components.Of<SpotLight2D>()) && World.Read<SpotLight2D>(entity) is { } spot
                ? (spot.Range, spot.OuterConeAngle, spot.InnerConeAngle)
                : (0f, 0f, 0f);
        if (range <= 0)
        {
            return;
        }
        Matrix4x4 world = World.ComputeWorldTransform(entity);
        Vector3 center = world.Translation;
        // In the world's XY plane, unscaled: a light's range is in meters.
        Vector2 down = Vector2.Normalize(new Vector2(-world.M21, -world.M22) is { } d && d != Vector2.Zero ? d : -Vector2.UnitY);
        Vector2? At(Vector2 offset) => camera.Project(center + new Vector3(offset, 0), size);
        void Line(List<Vector2> points)
        {
            if (points.Count > 1)
            {
                canvas.DrawPolyline([.. points], 3.5f, _lightEdge);
                canvas.DrawPolyline([.. points], 1.5f, _lightLine);
            }
        }
        Vector2 Turned(float angle) => new(down.X * MathF.Cos(angle) - down.Y * MathF.Sin(angle), down.X * MathF.Sin(angle) + down.Y * MathF.Cos(angle));
        const int segments = 48;
        if (outer <= 0)
        {
            Line([.. Enumerable.Range(0, segments + 1).Select(i => At(Turned(MathF.Tau * i / segments) * range)).OfType<Vector2>()]);
            return;
        }
        float half = MathF.Min(outer, MathF.Tau) * 0.5f;
        List<Vector2> cone = [.. new[] { At(Vector2.Zero) }.OfType<Vector2>()];
        cone.AddRange(Enumerable.Range(0, segments + 1).Select(i => At(Turned(-half + 2 * half * i / segments) * range)).OfType<Vector2>());
        if (At(Vector2.Zero) is { } origin)
        {
            cone.Add(origin);
        }
        Line(cone);
        if (inner > 0 && inner < outer)
        {
            foreach (float side in new[] { -inner * 0.5f, inner * 0.5f })
            {
                Line([.. new[] { At(Vector2.Zero), At(Turned(side) * range) }.OfType<Vector2>()]);
            }
        }
    }
}
