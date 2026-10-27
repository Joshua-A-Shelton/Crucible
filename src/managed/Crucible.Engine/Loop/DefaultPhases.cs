using Crucible.Platform;

namespace Crucible.Loop;

/// <summary>
/// ADR 0013's BeginFrame: the pacing wait (ADR 0003), first, so input read
/// next is as fresh as possible.
/// </summary>
public sealed class BeginFramePhase : FramePhase
{
    public override string Name => "BeginFrame";

    public override void Run(FrameContext context)
    {
        if (context.Surface.Value == 0)
        {
            return; // headless: nothing to pace against
        }
        Status status = Render.Api.WaitForNextFrame(context.Surface);
        if (status < 0)
        {
            context.Fail(status, Name);
        }
    }
}

/// <summary>
/// ADR 0013's Input: reads this frame's events, and requests quit when the OS
/// asks or the main window is closed.
/// </summary>
public sealed class InputPhase : FramePhase
{
    public override string Name => "Input";

    public override void Run(FrameContext context)
    {
        int count = (int)Platform.Api.PollEvents(context.EventBuffer);
        context.SetEventCount(count);
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
/// ADR 0013's RenderSubmit. Until the render graph arrives, the frame is a
/// clear: a slow pulse, so a stutter or a dropped frame is visible by eye.
/// </summary>
public sealed class RenderSubmitPhase : FramePhase
{
    public override string Name => "RenderSubmit";

    public override void Run(FrameContext context)
    {
        if (context.Surface.Value == 0)
        {
            return; // headless
        }
        // One cycle every two seconds, on the frame's start time.
        double seconds = context.FrameStartNs / 1e9;
        float pulse = (float)(0.5 + 0.5 * Math.Sin(seconds * Math.PI));
        Status status = Render.Api.ClearAndPresent(context.Surface, 0.02f, 0.02f + 0.08f * pulse, 0.1f, 1.0f);
        if (status < 0)
        {
            context.Fail(status, Name);
        }
    }
}
