using Crucible.Loop;
using Crucible.Render;

namespace Crucible.Editor;

/// <summary>
/// The editor in the frame loop (ADR 0066): the shell made at start, the
/// frame's drags, window closes, and dialog answers handled after the UI has
/// had its input, and the floating windows rendered beside the main one.
/// </summary>
[System(Phases.RenderSubmit)]
public sealed class EditorSystem : GameSystem
{
    private Shell? _shell;

    internal Shell? Shell => _shell;

    public override void Start(FrameContext context)
    {
        // The editor's view in the scene's schedule: Grid, Pick, SelectionOutline (ADR 0067).
        RenderSchedule.Default.AddEditorSteps();
        _shell = new Shell(context, context.GameArguments.TryGetValue("settings", out string? settings)
            ? settings
            : EditorSettings.DefaultPath);
        if (context.GameArguments.TryGetValue("project", out string? project))
        {
            _shell.OpenProject(project);
        }
        if (context.GameArguments.TryGetValue("scene", out string? scene))
        {
            _shell.OpenScene(scene);
        }
    }

    public override void Run(FrameContext context)
    {
        if (_shell is null)
        {
            return;
        }
        _shell.ProcessEvents(context.Events, context.PolledText);
        _shell.RenderFloating();
    }

    public override void Stop(FrameContext context)
    {
        _shell?.Stop();
        _shell = null;
    }
}

/// <summary>
/// The editor's viewports (ADR 0067), rendered before the main window's
/// frame, which draws their images; and picks answered.
/// </summary>
[System(Phases.RenderSubmit, Before = [typeof(RenderSystem)])]
public sealed class EditorViewportSystem : GameSystem
{
    public override void Run(FrameContext context) => Shell.Current?.RenderViewports();
}

