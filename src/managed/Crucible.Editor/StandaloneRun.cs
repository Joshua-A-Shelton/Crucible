using System.Collections.Concurrent;
using System.Diagnostics;

namespace Crucible.Editor;

/// <summary>
/// Run Standalone (ADR 0073): the project's game in a process of its own,
/// started with the game launcher, its own window, and the project and
/// scene to open. One at a time; its output is collected for the Log,
/// which reads it on the main thread.
/// </summary>
public sealed class StandaloneRun : IDisposable
{
    private readonly ConcurrentQueue<string> _lines = new();
    private Process? _process;

    /// <summary>Whether the game's process is running.</summary>
    public bool IsRunning => _process is { HasExited: false };

    /// <summary>
    /// The launcher's arguments: the engine's assemblies, the game (none
    /// when the project has no code), the project, the scene, and any extra.
    /// </summary>
    public static List<string> Arguments(string engineFolder, string? game, string projectFile, string scene,
        IEnumerable<string> extra)
    {
        List<string> arguments = ["--managed-dir", engineFolder];
        if (game is not null)
        {
            arguments.AddRange(["--game", game]);
        }
        arguments.AddRange(["--project", projectFile, "--scene", scene]);
        arguments.AddRange(extra);
        return arguments;
    }

    /// <summary>The game launcher beside the editor's own executable (crucible_launcher).</summary>
    public static string LauncherPath(string editorExecutable)
    {
        string name = OperatingSystem.IsWindows() ? "crucible_launcher.exe" : "crucible_launcher";
        return Path.Combine(Path.GetDirectoryName(editorExecutable) ?? "", name);
    }

    /// <summary>Starts the game; stops one still running first.</summary>
    public void Start(string launcher, IEnumerable<string> arguments, string workingFolder)
    {
        Stop();
        ProcessStartInfo start = new(launcher)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingFolder,
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        Process process = new() { StartInfo = start };
        process.OutputDataReceived += (_, e) => Collect(e.Data);
        process.ErrorDataReceived += (_, e) => Collect(e.Data);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _process = process;
    }

    /// <summary>The game's output lines since the last call.</summary>
    public List<string> TakeLines()
    {
        List<string> lines = [];
        while (_lines.TryDequeue(out string? line))
        {
            lines.Add(line);
        }
        return lines;
    }

    /// <summary>The exit code, once, after the process ended and all its output was read; null until then.</summary>
    public int? TakeExit()
    {
        if (_process is not { HasExited: true } process)
        {
            return null;
        }
        process.WaitForExit(); // the output read to its end
        int code = process.ExitCode;
        process.Dispose();
        _process = null;
        return code;
    }

    /// <summary>Ends the game's process, if it's running.</summary>
    public void Stop()
    {
        if (_process is null)
        {
            return;
        }
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
            _process.WaitForExit();
        }
        catch (InvalidOperationException)
        {
            // It ended on its own meanwhile.
        }
        _process.Dispose();
        _process = null;
    }

    public void Dispose() => Stop();

    private void Collect(string? line)
    {
        if (line is not null)
        {
            _lines.Enqueue(line);
        }
    }
}
