namespace Crucible.Loop;

/// <summary>
/// The loop driver (ADR 0011, 0013): runs the phases in order, once per frame,
/// until a phase requests quit or fails, or the frame limit is reached. The
/// driver is replaceable (ADR 0013), so it holds no state a phase needs.
/// </summary>
public sealed class FrameLoop(IReadOnlyList<FramePhase> phases)
{
    // An array, not the list: indexing it per frame never allocates an enumerator.
    private readonly FramePhase[] _phases = [.. phases];

    /// <summary>The phases, in the order they run.</summary>
    public IReadOnlyList<FramePhase> Phases => _phases;

    /// <summary>
    /// ADR 0013's default list, as far as M2 has it: BeginFrame (pacing),
    /// Input, RenderSubmit.
    /// </summary>
    public static FrameLoop CreateDefault() =>
        new([new BeginFramePhase(), new InputPhase(), new RenderSubmitPhase()]);

    /// <summary>Runs frames; returns how many ran.</summary>
    /// <param name="frameLimit">Stop after this many frames; 0 runs until quit or failure.</param>
    public ulong Run(FrameContext context, uint frameLimit = 0)
    {
        while (!context.QuitRequested && context.Failure == Status.Ok &&
               (frameLimit == 0 || context.FrameNumber < frameLimit))
        {
            context.FrameStartNs = Platform.Api.TimestampNow();
            foreach (FramePhase phase in _phases)
            {
                phase.Run(context);
                if (context.Failure != Status.Ok)
                {
                    break;
                }
            }
            context.FrameNumber++;
        }
        return context.FrameNumber;
    }
}
