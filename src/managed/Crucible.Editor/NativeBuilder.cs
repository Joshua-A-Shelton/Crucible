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
    private readonly string _variant;
    private Task<GameBuild>? _running;

    /// <param name="variant">"" for the editor's build; "Shipping" for a
    /// Shipping package's, with the shipping preset's toolkit, kept apart
    /// (Cache/Build/NativeShipping, AotShipping; ADR 0103).</param>
    public NativeBuilder(Project project, string toolkitPath, string variant = "")
    {
        _variant = variant;
        _project = project;
        _toolkitPath = toolkitPath;
    }

    /// <summary>The module's library name: "Diorama.Core" (ADR 0100).</summary>
    public string LibraryName => $"{GameBuilder.Sanitize(_project.Name)}.Core";

    public string Folder => Path.Combine(_project.CacheFolder, "Build", "Native" + _variant);

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

    /// <summary>Where a NativeAOT executable is linked (ADR 0101): Cache/Build/Aot/Link (AotShipping for a Shipping package).</summary>
    public string AotFolder => Path.Combine(_project.CacheFolder, "Build", "Aot" + _variant, "Link");

    /// <summary>
    /// The CMake project linking a game's NativeAOT executable (ADR 0101):
    /// the AOT launcher's objects, the module's sources if it has any (with
    /// their bindings and a file linking it in), the game's NativeAOT static
    /// library and the runtime's libraries it lists, and the core's static
    /// libraries. On Linux the debug information is moved to its own file.
    /// </summary>
    public string AotProjectText(ModuleToolkit toolkit, string executable, string gameLibrary, string linkInputs)
    {
        string native = Escape(_project.NativeFolder);
        StringBuilder text = new();
        text.Append("# Written by the Crucible editor (ADR 0101); rewritten before every build.\n");
        text.Append("cmake_minimum_required(VERSION 3.25)\n");
        // The static C runtime, as the engine's libraries are built (ADR 0031).
        text.Append("set(CMAKE_MSVC_RUNTIME_LIBRARY MultiThreaded)\n");
        text.Append($"project({executable}_Aot LANGUAGES CXX)\n\n");
        text.Append($"add_executable({executable}");
        foreach (string objectFile in toolkit.AotObjects ?? [])
        {
            text.Append($"\n    \"{Escape(objectFile)}\"");
        }
        text.Append(")\n");
        if (HasCode)
        {
            text.Append($"file(GLOB_RECURSE sources CONFIGURE_DEPENDS \"{native}/*.cpp\")\n");
            text.Append($"target_sources({executable} PRIVATE ${{sources}} \"${{CMAKE_CURRENT_SOURCE_DIR}}/LinkedModule.cpp\")\n");
            string bindings = Path.Combine(Folder, "Bindings.g.cpp");
            if (File.Exists(bindings))
            {
                text.Append($"target_sources({executable} PRIVATE \"{Escape(bindings)}\")\n");
            }
            text.Append($"target_include_directories({executable} PRIVATE \"{native}\"");
            foreach (string directory in toolkit.IncludeDirectories)
            {
                text.Append($"\n    \"{Escape(directory)}\"");
            }
            text.Append(")\n");
            if (toolkit.CompileDefinitions.Count > 0)
            {
                text.Append($"target_compile_definitions({executable} PRIVATE {string.Join(' ', toolkit.CompileDefinitions)})\n");
            }
            if (toolkit.CompileOptions.Count > 0)
            {
                text.Append($"target_compile_options({executable} PRIVATE {string.Join(' ', toolkit.CompileOptions)})\n");
            }
        }
        text.Append($"set_target_properties({executable} PROPERTIES\n");
        // Without the module's sources it's object files alone, which say no language.
        text.Append("    CXX_STANDARD 20 CXX_STANDARD_REQUIRED ON LINKER_LANGUAGE CXX\n");
        text.Append("    RUNTIME_OUTPUT_DIRECTORY \"${CMAKE_CURRENT_SOURCE_DIR}/bin\")\n");
        // The game first: it refers to the core and the runtime, which
        // follow (static libraries resolve left to right). On Linux, one
        // group, so their references to each other resolve in any order.
        List<string> libraries = [$"\"{Escape(gameLibrary)}\"", .. (toolkit.AotLibraries ?? []).Select(library => $"\"{Escape(library)}\""),
            $"\"LINKER:@{Escape(linkInputs)}\""];
        bool group = !OperatingSystem.IsWindows();
        text.Append($"target_link_libraries({executable} PRIVATE");
        if (group)
        {
            text.Append("\n    -Wl,--start-group");
        }
        foreach (string library in libraries)
        {
            text.Append($"\n    {library}");
        }
        if (group)
        {
            text.Append("\n    -Wl,--end-group");
        }
        foreach (string library in toolkit.AotSystemLibraries ?? [])
        {
            text.Append($"\n    {library}");
        }
        text.Append(")\n");
        if (!OperatingSystem.IsWindows())
        {
            // Its debug information beside it, for crash reports, not in
            // the package (ADR 0101).
            text.Append($"add_custom_command(TARGET {executable} POST_BUILD\n");
            text.Append($"    COMMAND \"${{CMAKE_OBJCOPY}}\" --only-keep-debug \"$<TARGET_FILE:{executable}>\" \"$<TARGET_FILE:{executable}>.debug\"\n");
            text.Append($"    COMMAND \"${{CMAKE_OBJCOPY}}\" --strip-debug \"--add-gnu-debuglink=$<TARGET_FILE:{executable}>.debug\" \"$<TARGET_FILE:{executable}>\"\n");
            text.Append("    VERBATIM)\n");
        }
        return text.ToString();
    }

    /// <summary>
    /// Links a game's NativeAOT executable (ADR 0101) from its static library
    /// and the runtime's link inputs, waiting for it; the result's assembly is
    /// the executable's path.
    /// </summary>
    public GameBuild LinkAot(string executable, string gameLibrary, string linkInputs)
    {
        if (ReadToolkit() is not { AotObjects.Count: > 0 } toolkit)
        {
            return new GameBuild(false, null,
                [new BuildError("", 0, 0, $"no AOT launcher in the module toolkit at {_toolkitPath}: the engine's build writes it")], "");
        }
        Directory.CreateDirectory(AotFolder);
        string projectFile = Path.Combine(AotFolder, "CMakeLists.txt");
        string text = AotProjectText(toolkit, executable, gameLibrary, linkInputs);
        if (!File.Exists(projectFile) || File.ReadAllText(projectFile) != text)
        {
            File.WriteAllText(projectFile, text);
        }
        if (HasCode)
        {
            string linked = $$"""
                // Written by the Crucible editor (ADR 0101): the module linked into the
                // NativeAOT executable, started where a loaded one would be.
                #include <crucible/Module.h>

                extern "C" bool crucibleModuleStart();
                extern "C" void crucibleModuleStop();

                namespace {
                const bool linked = crucible::linkModule("{{LibraryName}}", &crucibleModuleStart, &crucibleModuleStop);
                }

                """;
            string linkedFile = Path.Combine(AotFolder, "LinkedModule.cpp");
            if (!File.Exists(linkedFile) || File.ReadAllText(linkedFile) != linked)
            {
                File.WriteAllText(linkedFile, linked);
            }
        }
        string build = Path.Combine(AotFolder, "build");
        string output = Path.Combine(AotFolder, "bin", executable + (OperatingSystem.IsWindows() ? ".exe" : ""));
        StringBuilder log = new();
        int code = 0;
        ClearIfMoved(build);
        if (!File.Exists(Path.Combine(build, "CMakeCache.txt")))
        {
            code = Run(toolkit, log, toolkit.Cmake, "-S", AotFolder, "-B", build, "-G", "Ninja",
                $"-DCMAKE_MAKE_PROGRAM={toolkit.Ninja}", $"-DCMAKE_CXX_COMPILER={toolkit.CxxCompiler}",
                $"-DCMAKE_BUILD_TYPE={toolkit.BuildType}");
        }
        if (code == 0)
        {
            code = Run(toolkit, log, toolkit.Cmake, "--build", build);
        }
        string all = log.ToString();
        List<BuildError> errors = [.. Errors(all)];
        bool built = code == 0 && File.Exists(output);
        if (!built && errors.Count == 0)
        {
            string last = all.Split('\n').Select(line => line.Trim()).LastOrDefault(line => line.Length > 0) ?? "";
            errors.Add(new BuildError("", 0, 0, $"linking the NativeAOT executable failed ({code}): {last}"));
        }
        return new GameBuild(built, built ? output : null, errors, all);
    }

    // CMake won't build in a folder configured somewhere else: a project
    // moved (or copied) with its Cache configures afresh (ADR 0110).
    internal static void ClearIfMoved(string build)
    {
        string cache = Path.Combine(build, "CMakeCache.txt");
        if (!File.Exists(cache))
        {
            return;
        }
        const string key = "CMAKE_CACHEFILE_DIR:INTERNAL=";
        string? configured = File.ReadLines(cache).FirstOrDefault(line => line.StartsWith(key, StringComparison.Ordinal))?[key.Length..];
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (configured is null ||
            !string.Equals(Path.GetFullPath(configured).TrimEnd('/', '\\'), Path.GetFullPath(build).TrimEnd('/', '\\'), comparison))
        {
            Directory.Delete(build, recursive: true);
        }
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
        ClearIfMoved(build);
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

/// <summary>
/// What the engine's build records for building native modules
/// (CrucibleModule.json, ADR 0074), and for linking a game's NativeAOT
/// executable (ADR 0101): the AOT launcher's objects, the core's static
/// libraries and the system's, and the NativeAOT compiler's instruction set.
/// </summary>
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
    string Clang,
    IReadOnlyList<string>? AotObjects = null,
    IReadOnlyList<string>? AotLibraries = null,
    IReadOnlyList<string>? AotSystemLibraries = null,
    string AotInstructionSet = "",
    string Launcher = "",
    string CoreRuntime = "",
    string EngineDirectory = "",
    bool Shipping = false,
    string ShippingToolkit = "");

[System.Text.Json.Serialization.JsonSerializable(typeof(ModuleToolkit))]
internal sealed partial class ModuleToolkitJson : System.Text.Json.Serialization.JsonSerializerContext;
