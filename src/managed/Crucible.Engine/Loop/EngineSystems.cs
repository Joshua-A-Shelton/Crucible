using Crucible.Platform;

namespace Crucible.Loop;

/// <summary>
/// ADR 0013's pacing wait (ADR 0003), in BeginFrame: first, so input read
/// next is as fresh as possible.
/// </summary>
public sealed class PacingSystem : GameSystem
{
    public override void Run(FrameContext context)
    {
        // Without a surface (C# tests) there's nothing to pace against.
        if (context.Pacing && context.Surface.Value != 0)
        {
            Status status = Render.Api.WaitForNextFrame(context.Surface);
            if (status < 0)
            {
                context.Fail(status, Name);
                return;
            }
        }
        context.Latency?.FrameStarted(context, Platform.Api.TimestampNow());
    }
}

/// <summary>
/// ADR 0013's input read, in Input: this frame's events, and a quit request
/// when the OS asks or the main window is closed.
/// </summary>
public sealed class InputSystem : GameSystem
{
    public override void Run(FrameContext context)
    {
        int count = (int)Platform.Api.PollEvents(context.EventBuffer);
        context.SetEventCount(count);
        context.Latency?.InputRead(context, Platform.Api.TimestampNow());
        foreach (ref readonly InputEvent inputEvent in context.Events)
        {
            if (inputEvent.Type == InputEventType.QuitRequested ||
                (inputEvent.Type == InputEventType.WindowCloseRequested &&
                 inputEvent.Window.Value == context.Window.Value))
            {
                context.QuitRequested = true;
            }
        }
    }
}

/// <summary>
/// The frame's render and present, in RenderSubmit: the render schedule's
/// steps (ADR 0048), over a clear that pulses slowly, so a stutter or a
/// dropped frame is visible by eye.
/// </summary>
public sealed class RenderSystem : GameSystem
{
    public override void Run(FrameContext context)
    {
        if (context.Surface.Value == 0)
        {
            return; // no surface (C# tests)
        }
        // One cycle every two seconds, on the frame's start time.
        double seconds = context.FrameStartNs / 1e9;
        float pulse = (float)(0.5 + 0.5 * Math.Sin(seconds * Math.PI));
        // Only measured with the harness on: it tells a submitted frame from a skipped one.
        ulong submittedBefore = context.Latency is null ? 0 : Render.Api.FramesSubmitted(context.Surface);
        Status status = Render.Api.RenderFrame(context.Surface, 0.02f, 0.02f + 0.08f * pulse, 0.1f, 1.0f);
        if (status < 0)
        {
            context.Fail(status, Name);
            return;
        }
        if (context.Latency is { } latency)
        {
            ulong submitted = Render.Api.FramesSubmitted(context.Surface);
            latency.Submitted(context, Platform.Api.TimestampNow(), submitted, submitted != submittedBefore);
        }
    }
}
