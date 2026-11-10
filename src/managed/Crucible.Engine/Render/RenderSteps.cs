namespace Crucible.Render;

/// <summary>
/// The renderer's contract (ADR 0048, 0076): its components (MeshInstance,
/// Camera, the lights, Sky, PostProcess), these steps by name in a surface's
/// <see cref="RenderSchedule"/>, and <see cref="RenderAssets"/>. A game or
/// plugin replaces, removes, or adds steps around them by name. The default
/// steps, in order, then the editor's own.
/// </summary>
public static class RenderSteps
{
    public const string Clear = "Clear";
    public const string DepthPrepass = "DepthPrepass";
    public const string LightClustering = "LightClustering";
    public const string Shadows = "Shadows";
    public const string Opaque = "Opaque";
    public const string Outlines = "Outlines";
    public const string Sky = "Sky";
    public const string Transparent = "Transparent";
    public const string Bloom = "Bloom";
    public const string Tonemap = "Tonemap";

    // The editor's, around them (editor views only).
    public const string Grid = "Grid";
    public const string Pick = "Pick";
    public const string SelectionOutline = "SelectionOutline";
}
