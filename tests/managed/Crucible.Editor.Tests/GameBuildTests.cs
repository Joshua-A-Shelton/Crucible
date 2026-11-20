using Crucible.Assets;

namespace Crucible.Editor.Tests;

// Building a project's code (ADR 0072): the project file the editor writes,
// a real dotnet build of a tiny game, and its errors read back with their
// file and line.
[NotInParallel("dotnet-build")]
public class GameBuildTests
{
    [Test]
    public async Task TheGeneratedProjectCompilesTheCodeFolderAgainstTheEngine()
    {
        string folder = Path.Combine(Path.GetTempPath(), "crucible-build-" + Guid.NewGuid().ToString("N"));
        try
        {
            Project project = Project.Create(folder, "Tiny Game");
            GameBuilder builder = new(project);
            await Assert.That(builder.AssemblyName).IsEqualTo("TinyGame");
            await Assert.That(builder.HasCode).IsFalse();
            string text = builder.ProjectText();
            await Assert.That(text).Contains("<Compile Include=");
            await Assert.That(text).Contains("Crucible.Engine.dll");
            await Assert.That(text).Contains("Private=\"false\"");

            File.WriteAllText(Path.Combine(project.CodeFolder, "Tick.cs"), """
                using Crucible.Loop;
                namespace TinyGame;
                [System(Phases.Update)]
                public sealed class Tick : GameSystem
                {
                    public override void Run(FrameContext context) { }
                }
                """);
            await Assert.That(builder.HasCode).IsTrue();
            GameBuild built = builder.BuildNow();
            await Assert.That(built.Succeeded).IsTrue().Because(built.Output);
            await Assert.That(File.Exists(built.Assembly!)).IsTrue();

            File.WriteAllText(Path.Combine(project.CodeFolder, "Broken.cs"), "namespace TinyGame;\n\npublic class Broken { int x = ; }\n");
            GameBuild broken = builder.BuildNow();
            await Assert.That(broken.Succeeded).IsFalse();
            BuildError error = broken.Errors.First();
            await Assert.That(Path.GetFileName(error.File)).IsEqualTo("Broken.cs");
            await Assert.That(error.Line).IsEqualTo(3);
            await Assert.That(error.Message).StartsWith("CS");
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    // Skipping up-to-date builds (ADR 0110): a stamp of the build's inputs,
    // kept only while they and the assembly it made are unchanged, and the
    // same for a copy of the project elsewhere.
    [Test]
    public async Task ABuildIsSkippedWhileItsInputsAndAssemblyAreUnchanged()
    {
        string folder = Path.Combine(Path.GetTempPath(), "crucible-stamp-" + Guid.NewGuid().ToString("N"));
        try
        {
            Project project = Project.Create(Path.Combine(folder, "a"), "Stamped");
            string tick = Path.Combine(project.CodeFolder, "Tick.cs");
            const string code = "namespace Stamped;\n\npublic static class Tick { public static int Count; }\n";
            File.WriteAllText(tick, code);
            GameBuilder builder = new(project);
            await Assert.That(builder.IsUpToDate()).IsFalse(); // never built

            GameBuild first = builder.BuildNow();
            await Assert.That(first.Succeeded).IsTrue().Because(first.Output);
            await Assert.That(first.UpToDate).IsFalse();
            await Assert.That(File.Exists(builder.StampFile)).IsTrue();
            await Assert.That(builder.IsUpToDate()).IsTrue();
            GameBuild again = builder.BuildNow();
            await Assert.That(again.UpToDate && again.Succeeded && again.Assembly == first.Assembly).IsTrue();

            // Saved as it was: still up to date, whatever its time.
            File.WriteAllText(tick, code);
            File.SetLastWriteTimeUtc(tick, DateTime.UtcNow.AddMinutes(5));
            await Assert.That(builder.IsUpToDate()).IsTrue();

            // Each input counts: a file's contents, its name, a new file, the bindings.
            File.WriteAllText(tick, code.Replace("Count", "Total", StringComparison.Ordinal));
            await Assert.That(builder.IsUpToDate()).IsFalse();
            File.WriteAllText(tick, code);
            await Assert.That(builder.IsUpToDate()).IsTrue();
            File.Move(tick, Path.Combine(project.CodeFolder, "Tock.cs"));
            await Assert.That(builder.IsUpToDate()).IsFalse();
            File.Move(Path.Combine(project.CodeFolder, "Tock.cs"), tick);
            File.WriteAllText(Path.Combine(project.CodeFolder, "More.cs"), "namespace Stamped;\n");
            await Assert.That(builder.IsUpToDate()).IsFalse();
            File.Delete(Path.Combine(project.CodeFolder, "More.cs"));
            string bindings = Path.Combine(project.CacheFolder, "Build", "Native", "Bindings.g.cs");
            Directory.CreateDirectory(Path.GetDirectoryName(bindings)!);
            File.WriteAllText(bindings, "namespace Stamped.Interop;\n");
            await Assert.That(builder.IsUpToDate()).IsFalse();
            File.Delete(bindings);
            await Assert.That(builder.IsUpToDate()).IsTrue();

            // The assembly changed since: not the one the stamp's build made.
            byte[] assembly = File.ReadAllBytes(builder.OutputPath);
            File.WriteAllBytes(builder.OutputPath, [.. assembly, 0]);
            await Assert.That(builder.IsUpToDate()).IsFalse();
            File.WriteAllBytes(builder.OutputPath, assembly);
            await Assert.That(builder.IsUpToDate()).IsTrue();

            // A copy elsewhere, Cache and all, is up to date too; changed, it
            // builds (its obj folder, made in the other folder, cleared first).
            string copyRoot = Path.Combine(folder, "b");
            CopyFolder(project.Root, copyRoot);
            Project copy = Project.Open(Path.Combine(copyRoot, Path.GetFileName(project.ProjectFilePath)));
            GameBuilder copied = new(copy);
            await Assert.That(copied.IsUpToDate()).IsTrue();
            File.WriteAllText(Path.Combine(copy.CodeFolder, "Tick.cs"), code.Replace("Count", "Total", StringComparison.Ordinal));
            GameBuild rebuilt = copied.BuildNow();
            await Assert.That(rebuilt.Succeeded && !rebuilt.UpToDate).IsTrue().Because(rebuilt.Output);
            await Assert.That(copied.IsUpToDate()).IsTrue();

            // A failed build leaves no stamp: fixed, the code builds again.
            File.WriteAllText(tick, "namespace Stamped;\n\npublic class Broken { int x = ; }\n");
            await Assert.That(builder.BuildNow().Succeeded).IsFalse();
            await Assert.That(File.Exists(builder.StampFile)).IsFalse();
            File.WriteAllText(tick, code);
            await Assert.That(builder.IsUpToDate()).IsFalse();
            GameBuild fixedBuild = builder.BuildNow();
            await Assert.That(fixedBuild.Succeeded && !fixedBuild.UpToDate).IsTrue().Because(fixedBuild.Output);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    // The engine the build compiles against counts too (ADR 0110).
    [Test]
    public async Task TheEngineIsAnInput()
    {
        string folder = Path.Combine(Path.GetTempPath(), "crucible-stamp-" + Guid.NewGuid().ToString("N"));
        try
        {
            Project project = Project.Create(Path.Combine(folder, "game"), "Stamped");
            string engine = Path.Combine(folder, "Crucible.Engine.dll");
            File.Copy(GameBuilder.EngineAssembly(), engine);
            GameBuilder builder = new(project, engine);
            string before = builder.InputHash();
            await Assert.That(builder.InputHash()).IsEqualTo(before);
            File.AppendAllText(engine, "changed");
            await Assert.That(builder.InputHash()).IsNotEqualTo(before);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    private static void CopyFolder(string from, string to)
    {
        foreach (string file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    // A package's Data folder holds the runtime's assemblies and the engine's
    // core beside the game's (ADR 0100), so their names are taken.
    [Test]
    public async Task AGameIsNotNamedLikeTheRuntimeOrTheEngine()
    {
        await Assert.That(GameBuilder.Sanitize("Walker")).IsEqualTo("Walker");
        await Assert.That(GameBuilder.Sanitize("System")).IsEqualTo("GameSystem");
        await Assert.That(GameBuilder.Sanitize("Crucible")).IsEqualTo("GameCrucible");
        await Assert.That(GameBuilder.Sanitize("2048")).IsEqualTo("Game2048");
    }

    [Test]
    public async Task ErrorsAreReadFromDotnetsOutput()
    {
        const string output = """
            /home/me/Game/Code/Player.cs(12,5): error CS1002: ; expected [/home/me/Game/Cache/Build/Game.csproj]
            /home/me/Game/Code/Player.cs(12,5): error CS1002: ; expected [/home/me/Game/Cache/Build/Game.csproj]
            /home/me/Game/Code/Player.cs(12,9): error CS1002: ; expected [/home/me/Game/Cache/Build/Game.csproj]
            /home/me/Game/Code/Enemy.cs(3,1): warning CS0168: unused
            """;
        BuildError[] errors = [.. GameBuilder.Errors(output)];
        await Assert.That(errors.Length).IsEqualTo(2); // repeated lines once, warnings not at all
        await Assert.That(errors[0].ToString()).IsEqualTo("/home/me/Game/Code/Player.cs(12,5): CS1002: ; expected");
        await Assert.That(errors[1].Column).IsEqualTo(9); // same line, another column: another error
    }
}
