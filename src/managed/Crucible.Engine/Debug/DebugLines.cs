using System.Numerics;

namespace Crucible.Debug;

/// <summary>
/// Lines for one frame (ADR 0079), drawn over the finished image in every
/// view by the DebugLines render step, with no depth test. Cleared when the
/// next frame begins. Main thread.
/// </summary>
public static class DebugLines
{
    /// <summary>Adds a line in world space, its color linear with alpha.</summary>
    public static void Draw(Vector3 from, Vector3 to, Vector4 color) => Api.DrawLine(from, to, color);

    /// <summary>The frame's lines so far.</summary>
    public static int Count => (int)Api.DebugLineCount();
}
