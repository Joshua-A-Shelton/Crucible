namespace Crucible;

/// <summary>
/// Placeholder until the real engine API exists: proves the engine assembly
/// builds, can be tested, and can call into the C++ core.
/// </summary>
public static class EngineInfo
{
    public const string Name = "Crucible";

    /// <summary>Whether the C++ core was built with a Slag render backend.</summary>
    public static bool HasRenderBackend => Api.HasRenderBackend();
}
