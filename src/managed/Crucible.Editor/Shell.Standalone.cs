using Crucible.Assets;
using Crucible.Platform;
using Crucible.Scenes;
using Crucible.UI;

namespace Crucible.Editor;

// Run Standalone (ADR 0073): the open scene, as it is, run by the game
// launcher in its own process and window, with the project's last good
// build. The editor writes the scene to Cache/Run, passes the project and
// scene, and shows the game's output in the Log until it ends.
internal sealed partial class Shell
{
    private readonly StandaloneRun _standalone = new();

    /// <summary>The standalone run, running or not.</summary>
    public StandaloneRun Standalone => _standalone;

    private void RegisterStandaloneCommands()
    {
        Commands.Add(new Command("Play.RunStandalone", "Run Standalone", ToggleStandalone,
            () => _context.HasWorld && Project is not null)
        {
            Shortcut = new Shortcut(KeyModifiers.Ctrl | KeyModifiers.Shift, Keycode.P),
            CurrentTitle = () => _standalone.IsRunning ? "Stop Standalone" : "Run Standalone",
        });
    }

    private void ToggleStandalone()
    {
        if (_standalone.IsRunning)
        {
            StopStandalone();
        }
        else
        {
            RunStandalone();
        }
    }

    /// <summary>
    /// Runs the open scene in its own process; <paramref name="extra"/> are
    /// more launcher options (the checks run it headless for a few frames).
    /// </summary>
    public bool RunStandalone(params string[] extra)
    {
        if (Project is not { } project || !_context.HasWorld)
        {
            return false;
        }
        string launcher = StandaloneRun.LauncherPath(Environment.ProcessPath ?? "");
        if (!File.Exists(launcher))
        {
            Log($"Can't run standalone: no game launcher at {launcher}");
            return false;
        }
        // What changed is imported first, as the game reads only what's imported.
        RefreshAssets();
        // While playing, the scene as it was at Play.
        byte[] bytes = _playSnapshot ?? Scene.Save(World, SceneId, Prefabs);
        string scene = Path.Combine(project.CacheFolder, "Run",
            (ScenePath is { } path ? Path.GetFileNameWithoutExtension(path) : "Untitled") + Scene.Extension);
        Directory.CreateDirectory(Path.GetDirectoryName(scene)!);
        Files.WriteAtomically(scene, bytes);
        string? game = _builder is { HasCode: true } builder && File.Exists(builder.OutputPath) ? builder.OutputPath : null;
        try
        {
            _standalone.Start(launcher,
                StandaloneRun.Arguments(Path.GetDirectoryName(GameBuilder.EngineAssembly())!, game,
                    project.ProjectFilePath, scene, [.. ModuleArguments(), .. extra]),
                project.Root);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException)
        {
            Log($"Can't run standalone: {exception.Message}");
            return false;
        }
        Log($"Running {SceneName} standalone");
        return true;
    }

    // The native module's latest build (ADR 0074): a new process loads it fresh.
    private string[] ModuleArguments() =>
        _native is { HasCode: true } native && File.Exists(native.OutputPath) ? ["--module", native.OutputPath] : [];

    public void StopStandalone()
    {
        if (!_standalone.IsRunning)
        {
            return;
        }
        _standalone.Stop();
        Log("The standalone run was stopped");
    }

    // Each frame: the game's output into the Log, and its end.
    internal void PollStandalone()
    {
        foreach (string line in _standalone.TakeLines())
        {
            Log($"[game] {line}");
        }
        if (_standalone.TakeExit() is { } code)
        {
            foreach (string line in _standalone.TakeLines())
            {
                Log($"[game] {line}");
            }
            Log($"The standalone run ended (exit code {code})");
        }
    }
}
