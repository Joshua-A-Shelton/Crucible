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

    // Editor assemblies (ADR 0116): code under any folder named Editor
    // builds into <Game>.Editor.dll beside the game's, referencing it; the
    // game's own build has CRUCIBLE_EDITOR, a package's doesn't, and a check
    // builds the game's code without it.
    [Test]
    public async Task EditorFoldersBuildIntoAnEditorAssemblyReferencingTheGame()
    {
        string folder = Path.Combine(Path.GetTempPath(), "crucible-build-" + Guid.NewGuid().ToString("N"));
        try
        {
            Project project = Project.Create(folder, "Tiny Game");
            GameBuilder builder = new(project);
            File.WriteAllText(Path.Combine(project.CodeFolder, "Stats.cs"), """
                namespace TinyGame;
                public sealed class Stats { public int Health = 10; }
                #if CRUCIBLE_EDITOR
                public sealed class OnlyInTheEditor { }
                #endif
                """);
            await Assert.That(builder.HasEditorCode).IsFalse();
            await Assert.That(builder.UsesEditorSymbol).IsTrue();
            string nested = Path.Combine(project.CodeFolder, "Characters", "Editor");
            Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(nested, "StatsTool.cs"), """
                namespace TinyGame.Tools;
                public static class StatsTool
                {
                    public static int Read(TinyGame.Stats stats) => stats.Health;
                    public static string Editor => typeof(Crucible.Editor.GameBuilder).Name;
                }
                """);
            await Assert.That(builder.HasEditorCode).IsTrue();
            await Assert.That(builder.EditorAssemblyName).IsEqualTo("TinyGame.Editor");
            await Assert.That(builder.ProjectText()).Contains("CRUCIBLE_EDITOR");
            await Assert.That(builder.ProjectText(package: true)).DoesNotContain("CRUCIBLE_EDITOR");
            await Assert.That(builder.ProjectText(package: true)).Contains("Exclude=");

            GameBuild built = builder.BuildNow();
            await Assert.That(built.Succeeded).IsTrue().Because(built.Output);
            await Assert.That(built.Assembly).IsEqualTo(builder.OutputPath);
            await Assert.That(built.EditorAssembly).IsEqualTo(builder.EditorOutputPath);
            string[] gameTypes = TypeNames(builder.OutputPath);
            string[] editorTypes = TypeNames(builder.EditorOutputPath);
            await Assert.That(gameTypes).Contains("Stats");
            await Assert.That(gameTypes).Contains("OnlyInTheEditor");
            await Assert.That(gameTypes).DoesNotContain("StatsTool");
            await Assert.That(editorTypes).Contains("StatsTool");
            await Assert.That(builder.IsUpToDate()).IsTrue();
            await Assert.That(builder.BuildNow().UpToDate).IsTrue();

            // An editor file changed: the pair builds again.
            string before = builder.InputHash();
            File.AppendAllText(Path.Combine(nested, "StatsTool.cs"), "// changed\n");
            await Assert.That(builder.InputHash()).IsNotEqualTo(before);

            // Editor code in the game's folders isn't the game's: an error there names the editor file.
            File.WriteAllText(Path.Combine(nested, "Broken.cs"), "namespace TinyGame.Tools;\npublic class Broken { int x = ; }\n");
            GameBuild broken = builder.BuildNow();
            await Assert.That(broken.Succeeded).IsFalse();
            await Assert.That(broken.Assembly).IsNull();
            await Assert.That(Path.GetFileName(broken.Errors.First().File)).IsEqualTo("Broken.cs");
            File.Delete(Path.Combine(nested, "Broken.cs"));

            // Game code that only builds with CRUCIBLE_EDITOR: the check says so.
            await Assert.That(builder.StartSymbolCheck()).IsTrue();
            await Assert.That(builder.WaitForSymbolCheck()?.Succeeded).IsTrue();
            File.WriteAllText(Path.Combine(project.CodeFolder, "Uses.cs"), """
                namespace TinyGame;
                public static class Uses { public static object Make() => new OnlyInTheEditor(); }
                """);
            await Assert.That(builder.BuildNow().Succeeded).IsTrue();
            await Assert.That(builder.StartSymbolCheck()).IsTrue();
            GameBuild? check = builder.WaitForSymbolCheck();
            await Assert.That(check?.Succeeded).IsFalse();
            await Assert.That(Path.GetFileName(check!.Errors.First().File)).IsEqualTo("Uses.cs");
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    // An assembly's type names, read without loading it.
    private static string[] TypeNames(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using System.Reflection.PortableExecutable.PEReader pe = new(stream);
        System.Reflection.Metadata.MetadataReader reader = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(pe);
        return [.. reader.TypeDefinitions.Select(handle => reader.GetString(reader.GetTypeDefinition(handle).Name))];
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
