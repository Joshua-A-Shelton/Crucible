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
            await Assert.That(builder.AssemblyName).IsEqualTo("TinyGame.Game");
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

    [Test]
    public async Task ErrorsAreReadFromDotnetsOutput()
    {
        const string output = """
            /home/me/Game/Code/Player.cs(12,5): error CS1002: ; expected [/home/me/Game/Cache/Build/Game.Game.csproj]
            /home/me/Game/Code/Player.cs(12,5): error CS1002: ; expected [/home/me/Game/Cache/Build/Game.Game.csproj]
            /home/me/Game/Code/Player.cs(12,9): error CS1002: ; expected [/home/me/Game/Cache/Build/Game.Game.csproj]
            /home/me/Game/Code/Enemy.cs(3,1): warning CS0168: unused
            """;
        BuildError[] errors = [.. GameBuilder.Errors(output)];
        await Assert.That(errors.Length).IsEqualTo(2); // repeated lines once, warnings not at all
        await Assert.That(errors[0].ToString()).IsEqualTo("/home/me/Game/Code/Player.cs(12,5): CS1002: ; expected");
        await Assert.That(errors[1].Column).IsEqualTo(9); // same line, another column: another error
    }
}
