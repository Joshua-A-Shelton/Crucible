namespace Crucible.Editor.Tests;

// Run Standalone's process (ADR 0073): the launcher's arguments, and a
// process's output and exit code collected for the Log. The whole run, the
// game launcher included, is launcher.jit.standalone.
public class StandaloneRunTests
{
    [Test]
    public async Task TheLauncherGetsTheEngineGameProjectAndScene()
    {
        List<string> arguments = StandaloneRun.Arguments("/engine", "/p/Cache/Build/bin/P.Game.dll", "/p/P.crproject",
            "/p/Cache/Run/Start.crscene", ["--headless"]);
        await Assert.That(string.Join(" ", arguments)).IsEqualTo(
            "--managed-dir /engine --game /p/Cache/Build/bin/P.Game.dll --project /p/P.crproject " +
            "--scene /p/Cache/Run/Start.crscene --headless");
        // A project without code runs its scene with the engine's systems alone.
        List<string> noCode = StandaloneRun.Arguments("/engine", null, "/p/P.crproject", "/p/s.crscene", []);
        await Assert.That(noCode).DoesNotContain("--game");
        await Assert.That(Path.GetFileName(StandaloneRun.LauncherPath("/bin/crucible_editor")))
            .IsEqualTo(OperatingSystem.IsWindows() ? "crucible_launcher.exe" : "crucible_launcher");
    }

    [Test]
    public async Task OutputAndTheExitCodeAreCollected()
    {
        using StandaloneRun run = new();
        (string shell, string[] script) = OperatingSystem.IsWindows()
            ? ("cmd.exe", new[] { "/c", "echo one& echo two 1>&2& exit 3" })
            : ("/bin/sh", new[] { "-c", "echo one; echo two >&2; exit 3" });
        run.Start(shell, script, Path.GetTempPath());
        int? code = null;
        List<string> lines = [];
        for (int i = 0; i < 500 && code is null; ++i)
        {
            lines.AddRange(run.TakeLines());
            code = run.TakeExit();
            await Task.Delay(10);
        }
        lines.AddRange(run.TakeLines());
        await Assert.That(code).IsEqualTo(3);
        await Assert.That(lines.Select(line => line.Trim())).Contains("one");
        await Assert.That(lines.Select(line => line.Trim())).Contains("two");
        await Assert.That(run.IsRunning).IsFalse();
        await Assert.That(run.TakeExit()).IsNull(); // once
    }

    [Test]
    public async Task StopEndsTheProcess()
    {
        using StandaloneRun run = new();
        (string shell, string[] script) = OperatingSystem.IsWindows()
            ? ("cmd.exe", new[] { "/c", "ping -n 30 127.0.0.1 >nul" })
            : ("/bin/sh", new[] { "-c", "sleep 30" });
        run.Start(shell, script, Path.GetTempPath());
        await Assert.That(run.IsRunning).IsTrue();
        run.Stop();
        await Assert.That(run.IsRunning).IsFalse();
        await Assert.That(run.TakeExit()).IsNull();
    }
}
