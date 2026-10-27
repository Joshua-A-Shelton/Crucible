namespace Crucible.Loop;

/// <summary>
/// One step of the frame (ADR 0013). The frame is an ordered list of phases;
/// in M2 the list is fixed, and M3 lets games and plugins edit it. Names are
/// placeholders (docs/naming.md).
/// </summary>
public abstract class FramePhase
{
    /// <summary>The phase's name, as ADR 0013's default list spells it.</summary>
    public abstract string Name { get; }

    /// <summary>
    /// Runs once per frame. Runs every frame, so it must not throw (ADR 0030):
    /// it reports failure through <see cref="FrameContext.Fail"/> instead.
    /// </summary>
    public abstract void Run(FrameContext context);
}
