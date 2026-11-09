using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Crucible.Assets;

namespace Crucible.Editor;

/// <summary>
/// Builds a project's native module (ADR 0074): every .cpp file under its
/// Native folder, as a shared library linking the engine's shared core,
/// through a CMake project the editor writes (Cache/Build/Native/CMakeLists.txt)
/// and builds with the tools the engine was built with (CrucibleModule.json,
/// beside the launchers). Headers marked CRUCIBLE_EXPOSE go through the
/// engine's binding generator; the C# lands beside it for the game assembly.
/// </summary>
public sealed partial class NativeBuilder
{
    private readonly Project _project;
    private readonly string _toolkitPath;
    private Task<GameBuild>? _running;

    public NativeBuilder(Project project, string toolkitPath)
    {
        _project = project;
        _toolkitPath = toolkitPath;
    }

    /// <summary>The module's library name: "Diorama.Native".</summary>
    public string LibraryName => $"{GameBuilder.Sanitize(_project.Name)}.Native";

    public string Folder => Path.Combine(_project.CacheFolder, "Build", "Native");

    public string ProjectFile => Path.Combine(Folder, "CMakeLists.txt");

    /// <summary>Where the built library lands.</summary>
    public string OutputPath => Path.Combine(Folder, "bin", LibraryName + LibraryExtension);

    /// <summary>The bindings' C# for the game assembly; there only when the module exposes something.</summary>
    public string BindingsPath => Path.Combine(Folder, "Bindings.g.cs");

    public static string LibraryExtension => OperatingSystem.IsWindows() ? ".dll" : ".so";

    /// <summary>The toolkit file beside an executable (the editor's).</summary>
    public static string ToolkitBeside(string executable) =>
        Path.Combine(Path.GetDirectoryName(executable) ?? "", "CrucibleModule.json");

    /// <summary>Whether the project has native code (a Native folder with .cpp files in it).</summary>
    public bool HasCode =>
        Directory.Exists(_project.NativeFolder) &&
        Directory.EnumerateFiles(_project.NativeFolder, "*.cpp", SearchOption.AllDirectories).Any();

    public bool IsBuilding => _running is { IsCompleted: false };

    /// <summary>Starts a build in the background; false if one is already running.</summary>
    public bool Start()
    {
        if (IsBuilding)
        {
            return false;
        }
        _running = Task.Run(Build);
        return true;
    }

    /// <summary>The finished build, once; null while it's running or if none was started.</summary>
    public GameBuild? TakeFinished()
    {
        if (_running is not { IsCompleted: true } done)
        {
            return null;
        }
        _running = null;
        return done.IsFaulted
            ? new GameBuild(false, null, [new BuildError("", 0, 0, done.Exception?.GetBaseException().Message ?? "the build failed")], "")
            : done.Result;
    }

    /// <summary>Builds now, waiting for it (opening a project, before its code builds).</summary>
    public GameBuild BuildNow()
    {
        _running?.Wait();
        _running = null;
        return Build();
    }

    /// <summary>The toolkit the engine's build wrote; null if it's missing or unreadable.</summary>
    public ModuleToolkit? ReadToolkit()
    {
        try
        {
            return File.Exists(_toolkitPath)
                ? JsonSerializer.Deserialize(File.ReadAllBytes(_toolkitPath), ModuleToolkitJson.Default.ModuleToolkit)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The CMake project's text, for this toolkit.</summary>
    public string ProjectText(ModuleToolkit toolkit)
    {
        string native = Escape(_project.NativeFolder);
        bool exposes = ExposedHeaders().Any();
        StringBuilder text = new();
        text.Append("# Written by the Crucible editor (ADR 0074); rewritten before every build.\n");
        text.Append("cmake_minimum_required(VERSION 3.25)\n");
        text.Append($"project({LibraryName.Replace('.', '_')} LANGUAGES CXX)\n\n");
        text.Append($"file(GLOB_RECURSE sources CONFIGURE_DEPENDS \"{native}/*.cpp\")\n");
        text.Append($"add_library({LibraryName} SHARED ${{sources}})\n");
        if (exposes)
        {
            // The bindings: C ABI shims in the module, C# for the game assembly.
            text.Append($"file(GLOB_RECURSE headers CONFIGURE_DEPENDS \"{native}/*.h\")\n");
            text.Append($"file(GLOB engineHeaders \"{Escape(toolkit.CoreHeaders)}/*.h\")\n");
            text.Append("set(bindgen)\n");
            text.Append("foreach(header IN LISTS headers)\n    list(APPEND bindgen --header \"${header}\")\nendforeach()\n");
            text.Append("foreach(header IN LISTS engineHeaders)\n    list(APPEND bindgen --extern-header \"${header}\")\nendforeach()\n");
            foreach (string directory in (string[])[_project.NativeFolder, .. toolkit.IncludeDirectories])
            {
                text.Append($"list(APPEND bindgen --include-dir \"{Escape(directory)}\")\n");
            }
            text.Append("add_custom_command(\n");
            text.Append($"    OUTPUT \"${{CMAKE_CURRENT_SOURCE_DIR}}/Bindings.g.cpp\" \"{Escape(BindingsPath)}\"\n");
            text.Append($"    COMMAND \"{Escape(toolkit.Dotnet)}\" \"{Escape(toolkit.Bindgen)}\"\n");
            text.Append($"        --libclang \"{Escape(toolkit.Libclang)}\" --clang \"{Escape(toolkit.Clang)}\"\n");
            text.Append("        --output-cpp \"${CMAKE_CURRENT_SOURCE_DIR}/Bindings.g.cpp\"\n");
            text.Append($"        --output-cs \"{Escape(BindingsPath)}\"\n");
            text.Append("        --depfile \"${CMAKE_CURRENT_BINARY_DIR}/Bindings.d\"\n");
            text.Append($"        --library {LibraryName} --root-namespace {RootNamespace()}\n");
            text.Append($"        --interop-namespace {GameBuilder.Sanitize(_project.Name)}.Interop\n");
            text.Append("        --export-macro CRUCIBLE_MODULE_EXPORT --export-header crucible/Module.h\n");
            text.Append("        ${bindgen}\n");
            text.Append("    DEPENDS ${headers}\n");
            text.Append("    VERBATIM)\n");
            text.Append($"target_sources({LibraryName} PRIVATE \"${{CMAKE_CURRENT_SOURCE_DIR}}/Bindings.g.cpp\")\n");
        }
        text.Append($"set_target_properties({LibraryName} PROPERTIES\n");
        text.Append($"    PREFIX \"\" OUTPUT_NAME {LibraryName}\n");
        text.Append("    CXX_STANDARD 20 CXX_STANDARD_REQUIRED ON\n");
        text.Append("    CXX_VISIBILITY_PRESET hidden VISIBILITY_INLINES_HIDDEN ON\n");
        text.Append("    LIBRARY_OUTPUT_DIRECTORY \"${CMAKE_CURRENT_SOURCE_DIR}/bin\"\n");
        text.Append("    RUNTIME_OUTPUT_DIRECTORY \"${CMAKE_CURRENT_SOURCE_DIR}/bin\")\n");
        text.Append($"target_include_directories({LibraryName} PRIVATE \"{native}\"");
        foreach (string directory in toolkit.IncludeDirectories)
        {
            text.Append($"\n    \"{Escape(directory)}\"");
        }
        text.Append(")\n");
        if (toolkit.CompileDefinitions.Count > 0)
        {
            text.Append($"target_compile_definitions({LibraryName} PRIVATE {string.Join(' ', toolkit.CompileDefinitions)})\n");
        }
        if (toolkit.CompileOptions.Count > 0)
        {
            text.Append($"target_compile_options({LibraryName} PRIVATE {string.Join(' ', toolkit.CompileOptions)})\n");
        }
        text.Append($"target_link_libraries({LibraryName} PRIVATE \"{Escape(toolkit.CoreLibrary)}\")\n");
        return text.ToString();
    }

    // The module's C++ namespace for its exposed declarations: its project's name, lowercased.
    private string RootNamespace()
    {
        string name = GameBuilder.Sanitize(_project.Name);
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    private IEnumerable<string> ExposedHeaders() =>
        Directory.Exists(_project.NativeFolder)
            ? Directory.EnumerateFiles(_project.NativeFolder, "*.h", SearchOption.AllDirectories)
                .Where(header => File.ReadAllText(header).Contains("CRUCIBLE_EXPOSE", StringComparison.Ordinal))
            : [];

    private GameBuild Build()
    {
        if (ReadToolkit() is not { } toolkit)
        {
            return new GameBuild(false, null,
                [new BuildError("", 0, 0, $"no module toolkit at {_toolkitPath}: the engine's build writes it")], "");
        }
        Directory.CreateDirectory(Folder);
        string text = ProjectText(toolkit);
        // Unchanged, it's left alone, so the build stays incremental.
        if (!File.Exists(ProjectFile) || File.ReadAllText(ProjectFile) != text)
        {
            File.WriteAllText(ProjectFile, text);
        }
        if (!ExposedHeaders().Any() && File.Exists(BindingsPath))
        {
            File.Delete(BindingsPath);
        }
        string build = Path.Combine(Folder, "build");
        StringBuilder output = new();
        int code = 0;
        if (!File.Exists(Path.Combine(build, "CMakeCache.txt")))
        {
            code = Run(toolkit, output, toolkit.Cmake, "-S", Folder, "-B", build, "-G", "Ninja",
                $"-DCMAKE_MAKE_PROGRAM={toolkit.Ninja}", $"-DCMAKE_CXX_COMPILER={toolkit.CxxCompiler}",
                $"-DCMAKE_BUILD_TYPE={toolkit.BuildType}");
        }
        if (code == 0)
        {
            code = Run(toolkit, output, toolkit.Cmake, "--build", build);
        }
        string all = output.ToString();
        List<BuildError> errors = [.. Errors(all)];
        bool built = code == 0 && File.Exists(OutputPath);
        if (!built && errors.Count == 0)
        {
            string last = all.Split('\n').Select(line => line.Trim()).LastOrDefault(line => line.Length > 0) ?? "";
            errors.Add(new BuildError("", 0, 0, $"the native build failed ({code}): {last}"));
        }
        return new GameBuild(built, built ? OutputPath : null, errors, all);
    }

    // On Windows, through the compiler's environment script (vcvars64.bat).
    private static int Run(ModuleToolkit toolkit, StringBuilder output, string program, params string[] arguments)
    {
        ProcessStartInfo start;
        if (toolkit.Environment.Length > 0 && OperatingSystem.IsWindows())
        {
            string command = string.Join(' ', arguments.Select(argument => $"\"{argument}\""));
            // As one string: cmd doesn't read the escaping ArgumentList would add.
            start = new ProcessStartInfo("cmd.exe")
            {
                Arguments = $"/d /s /c \"call \"{toolkit.Environment}\" >nul && \"{program}\" {command}\"",
            };
        }
        else
        {
            start = new ProcessStartInfo(program);
            foreach (string argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }
        }
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        using Process process = Process.Start(start) ?? throw new InvalidOperationException($"{program} didn't start");
        Task<string> error = process.StandardError.ReadToEndAsync();
        output.Append(process.StandardOutput.ReadToEnd());
        process.WaitForExit();
        output.Append(error.Result);
        return process.ExitCode;
    }

    // GCC and Clang: "path:12:5: error: message"; MSVC: "path(12): error C2065: message"
    // (with a column only under /diagnostics:column).
    [GeneratedRegex(@"^(?<file>[^\r\n]+?)(:(?<line>\d+):(?<column>\d+):| ?\((?<line>\d+)(,(?<column>\d+))?\):) (fatal )?error( (?<code>\w+))?: (?<message>[^\r\n]*)$",
        RegexOptions.Multiline)]
    private static partial Regex ErrorLine();

    internal static IEnumerable<BuildError> Errors(string output) =>
        ErrorLine().Matches(output).Select(match => new BuildError(match.Groups["file"].Value.Trim(),
                int.Parse(match.Groups["line"].Value, System.Globalization.CultureInfo.InvariantCulture),
                match.Groups["column"].Success
                    ? int.Parse(match.Groups["column"].Value, System.Globalization.CultureInfo.InvariantCulture)
                    : 0,
                match.Groups["code"].Success
                    ? $"{match.Groups["code"].Value}: {match.Groups["message"].Value.Trim()}"
                    : match.Groups["message"].Value.Trim()))
            .Distinct();

    private static string Escape(string path) => path.Replace('\\', '/');
}

/// <summary>What the engine's build records for building native modules (CrucibleModule.json, ADR 0074).</summary>
public sealed record ModuleToolkit(
    string Cmake,
    string Ninja,
    string CxxCompiler,
    string Environment,
    string BuildType,
    IReadOnlyList<string> IncludeDirectories,
    IReadOnlyList<string> CompileDefinitions,
    IReadOnlyList<string> CompileOptions,
    string CoreLibrary,
    string CoreHeaders,
    string Dotnet,
    string Bindgen,
    string Libclang,
    string Clang);

[System.Text.Json.Serialization.JsonSerializable(typeof(ModuleToolkit))]
internal sealed partial class ModuleToolkitJson : System.Text.Json.Serialization.JsonSerializerContext;
