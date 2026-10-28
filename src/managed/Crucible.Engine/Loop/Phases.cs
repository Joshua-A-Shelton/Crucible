namespace Crucible.Loop;

/// <summary>
/// The default phase names (ADR 0036), as constants so attributes can use
/// them and a typo fails to compile. Custom phases are plain strings. Names
/// are placeholders (docs/naming.md).
/// </summary>
public static class Phases
{
    public const string BeginFrame = "BeginFrame";
    public const string Input = "Input";

    /// <summary>The fixed-tick group's place in the frame; systems go in its sub-phases.</summary>
    public const string FixedTick = "FixedTick";

    public const string PrePhysics = "PrePhysics";
    public const string Physics = "Physics";
    public const string PostPhysics = "PostPhysics";
    public const string Update = "Update";
    public const string Animation = "Animation";
    public const string LateUpdate = "LateUpdate";
    public const string RenderPrepare = "RenderPrepare";
    public const string RenderSubmit = "RenderSubmit";
    public const string EndFrame = "EndFrame";
}
