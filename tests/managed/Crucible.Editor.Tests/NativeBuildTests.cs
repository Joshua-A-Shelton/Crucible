using Crucible.Assets;

namespace Crucible.Editor.Tests;

// Building a project's native module (ADR 0074): the CMake project the editor
// writes, with the bindings only when a header exposes something, and the
// compilers' errors read back with their file, line, and column. A real
// build, loaded and played, is launcher.jit.native.
public class NativeBuildTests
{
    private static readonly ModuleToolkit _toolkit = new("/usr/bin/cmake", "/usr/bin/ninja", "/usr/bin/c++", "",
        "Debug", ["/engine/include", "/slag/include"], ["SLAG_BACKEND_VULKAN"], ["-fno-exceptions", "-fno-rtti"],
        "/engine/libcrucible_core.so", "/engine/include/crucible", "/usr/bin/dotnet", "/tools/Crucible.BindGen.dll",
        "/usr/lib/libclang.so", "/usr/bin/clang");

    [Test]
    public async Task TheGeneratedProjectBuildsTheNativeFolderAgainstTheCore()
    {
        string folder = Path.Combine(Path.GetTempPath(), "crucible-native-" + Guid.NewGuid().ToString("N"));
        try
        {
            Project project = Project.Create(folder, "Tiny Game");
            NativeBuilder builder = new(project, Path.Combine(folder, "missing.json"));
            await Assert.That(builder.LibraryName).IsEqualTo("TinyGame.Native");
            await Assert.That(Path.GetFileName(builder.OutputPath)).IsEqualTo("TinyGame.Native" + NativeBuilder.LibraryExtension);
            await Assert.That(builder.HasCode).IsFalse();
            await Assert.That(builder.ReadToolkit()).IsNull();
            GameBuild missing = builder.BuildNow();
            await Assert.That(missing.Succeeded).IsFalse();
            await Assert.That(missing.Errors[0].Message).Contains("no module toolkit");

            Directory.CreateDirectory(project.NativeFolder);
            File.WriteAllText(Path.Combine(project.NativeFolder, "Tiny.cpp"), "int tiny() { return 1; }\n");
            await Assert.That(builder.HasCode).IsTrue();
            string text = builder.ProjectText(_toolkit);
            await Assert.That(text).Contains("add_library(TinyGame.Native SHARED ${sources})");
            await Assert.That(text).Contains("\"/engine/libcrucible_core.so\"");
            await Assert.That(text).Contains("\"/slag/include\"");
            await Assert.That(text).Contains("SLAG_BACKEND_VULKAN");
            await Assert.That(text).Contains("-fno-exceptions -fno-rtti");
            await Assert.That(text).DoesNotContain("Crucible.BindGen.dll"); // nothing exposed

            File.WriteAllText(Path.Combine(project.NativeFolder, "Tiny.h"),
                "#include <crucible/Expose.h>\nnamespace tinyGame { CRUCIBLE_EXPOSE int tiny(); }\n");
            text = builder.ProjectText(_toolkit);
            await Assert.That(text).Contains("Crucible.BindGen.dll");
            await Assert.That(text).Contains("--root-namespace tinyGame");
            await Assert.That(text).Contains("--library TinyGame.Native");
            await Assert.That(text).Contains("--interop-namespace TinyGame.Interop");
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
    public async Task ErrorsAreReadFromEachCompiler()
    {
        const string output = """
            /home/me/Game/Native/Bob.cpp:12:5: error: expected ';' before '}' token
            /home/me/Game/Native/Bob.cpp:3:1: warning: unused variable
            C:\Game\Native\Bob.cpp(7,9): error C2065: 'x': undeclared identifier
            C:\Game\Native\Bob.cpp(9): error C2059: syntax error: '}'
            /home/me/Game/Native/Bob.h:2:10: fatal error: missing.h: No such file or directory
            """;
        BuildError[] errors = [.. NativeBuilder.Errors(output)];
        await Assert.That(errors.Length).IsEqualTo(4);
        await Assert.That(errors[0].ToString()).IsEqualTo("/home/me/Game/Native/Bob.cpp(12,5): expected ';' before '}' token");
        await Assert.That(errors[1].ToString()).IsEqualTo("C:\\Game\\Native\\Bob.cpp(7,9): C2065: 'x': undeclared identifier");
        await Assert.That(errors[2].ToString()).IsEqualTo("C:\\Game\\Native\\Bob.cpp(9,0): C2059: syntax error: '}'"); // MSVC: no column
        await Assert.That(errors[3].Line).IsEqualTo(2);
    }

    [Test]
    public async Task TheToolkitIsBesideTheEditor()
    {
        await Assert.That(NativeBuilder.ToolkitBeside(Path.Combine("bin", "crucible_editor")))
            .IsEqualTo(Path.Combine("bin", "CrucibleModule.json"));
    }
}
