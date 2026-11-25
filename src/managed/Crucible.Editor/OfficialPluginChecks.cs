using Crucible.Loop;

namespace Crucible.Editor;

/// <summary>
/// An official plugin's checks (ADR 0123), in its <c>&lt;Name&gt;.Editor.Checks.dll</c>
/// beside it (every build but shipping ones): made once when the editor
/// loads the plugin, then run each frame. Unlike an extension, they outlive
/// the projects they open, as the editor's own checks do. They see the
/// editor's internals (InternalsVisibleTo); the plugin itself doesn't.
/// </summary>
internal abstract class OfficialPluginChecks
{
    /// <summary>When the editor has loaded the plugin: whether to run, from the game arguments.</summary>
    public abstract void Start(Shell shell, FrameContext context);

    /// <summary>Each frame.</summary>
    public abstract void Run(Shell shell, FrameContext context);
}
