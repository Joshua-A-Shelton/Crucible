namespace Crucible.Render;

/// <summary>
/// A render step written in C# (ADR 0058): an entry of the render schedule,
/// like the engine's own C++ steps, added with <see cref="RenderSchedule"/>.
/// Every frame <see cref="AddPasses"/> adds the step's graph passes and
/// writes their commands, which native code plays back while the graph
/// records. Called on the main thread.
/// </summary>
public interface IRenderStep
{
    /// <summary>
    /// When the step joins a running renderer, or the renderer starts or
    /// restarts its steps (a new sample count): create pipelines here, never
    /// per frame (ADR 0025).
    /// </summary>
    void Prepare()
    {
    }

    /// <summary>
    /// This frame's passes. An exception fails the frame: no later step runs,
    /// nothing is drawn, and the render system rethrows it; the next frame
    /// starts cleanly.
    /// </summary>
    void AddPasses(RenderStepContext context);

    /// <summary>When the step leaves the schedule, or before it is prepared again: destroy pipelines here.</summary>
    void Release()
    {
    }
}
