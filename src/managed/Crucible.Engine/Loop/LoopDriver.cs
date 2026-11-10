using System.Diagnostics.CodeAnalysis;

namespace Crucible.Loop;

/// <summary>
/// The loop driver contract (ADR 0013, 0076): what runs a <see cref="Schedule"/>
/// frame after frame until a system requests quit or fails, or the frame
/// limit is reached. <see cref="FrameLoop"/> is the engine's; a game replaces
/// it (to pipeline rendering onto another thread, say) by naming its own
/// with <see cref="LoopDriverAttribute"/>. The editor's play mode runs the
/// game's systems in its own loop, not the game's driver.
/// </summary>
public interface ILoopDriver
{
    /// <summary>The schedule it runs.</summary>
    Schedule Schedule { get; }

    /// <summary>
    /// Starts the systems, runs frames, and stops them; returns how many
    /// frames ran (counting from the context's frame number).
    /// </summary>
    /// <param name="frameLimit">Stop after this many frames; 0 runs until quit or failure.</param>
    ulong Run(FrameContext context, uint frameLimit = 0);
}

/// <summary>
/// Names a game assembly's loop driver (ADR 0076): a class implementing
/// <see cref="ILoopDriver"/> with a public constructor taking the
/// <see cref="Schedule"/>. The host runs the game through it instead of
/// <see cref="FrameLoop"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class LoopDriverAttribute(
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type driverType) : Attribute
{
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    public Type DriverType { get; } = driverType;
}
