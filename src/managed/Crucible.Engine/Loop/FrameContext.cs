using Crucible.Platform;
using Crucible.Render;

namespace Crucible.Loop;

/// <summary>
/// What the phases of one frame share: allocated once, reused every frame, so
/// the loop creates no garbage.
/// </summary>
public sealed class FrameContext
{
    // Enough for a frame's events at any realistic rate; more stay queued in
    // SDL for the next frame.
    private readonly InputEvent[] _events = new InputEvent[256];
    private int _eventCount;

    /// <summary>The main window, or default when headless.</summary>
    public WindowHandle Window { get; init; }

    /// <summary>The main window's surface, or default when headless.</summary>
    public SurfaceHandle Surface { get; init; }

    /// <summary>Whether BeginFrame waits for the next frame (ADR 0003); --no-pacing turns it off.</summary>
    public bool Pacing { get; init; } = true;

    /// <summary>The latency harness, when --latency is given; null otherwise.</summary>
    public LatencyRecorder? Latency { get; init; }

    /// <summary>Counts from 0.</summary>
    public ulong FrameNumber { get; internal set; }

    /// <summary>When the frame started, on the input event clock (Platform.Api.TimestampNow).</summary>
    public ulong FrameStartNs { get; internal set; }

    /// <summary>Set by a phase to end the loop after this frame.</summary>
    public bool QuitRequested { get; set; }

    /// <summary>Ok, or the status of the failure that ends the loop.</summary>
    public Status Failure { get; private set; } = Status.Ok;

    /// <summary>The phase that failed, for the message.</summary>
    public string? FailedPhase { get; private set; }

    /// <summary>This frame's input and window events, in arrival order.</summary>
    public ReadOnlySpan<InputEvent> Events => _events.AsSpan(0, _eventCount);

    /// <summary>Ends the loop after this frame with an error status.</summary>
    public void Fail(Status status, string phase)
    {
        if (Failure == Status.Ok)
        {
            Failure = status;
            FailedPhase = phase;
        }
    }

    /// <summary>The buffer the Input phase polls into.</summary>
    internal Span<InputEvent> EventBuffer => _events;

    internal void SetEventCount(int count) => _eventCount = count;
}
