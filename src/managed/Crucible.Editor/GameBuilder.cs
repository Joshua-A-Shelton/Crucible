using System.Diagnostics;
using System.IO.Hashing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using Crucible.Assets;

namespace Crucible.Editor;

/// <summary>
/// Builds a project's own code (ADR 0072): every .cs file under its Code
/// folder, through a project file the editor writes (Cache/Build/&lt;Project&gt;.csproj:
/// the engine referenced, not copied; the engine's source generators; the
/// engine's AOT-safe settings), with <c>dotnet build</c> run in the
/// background. The build's errors come back with their file, line, and column.
/// </summary>
public sealed partial class GameBuilder
{
    private readonly Project _project;
    private readonly string? _engine;
    private readonly bool _shipping;
    private Task<GameBuild>? _running;

    public GameBuilder(Project project) => _project = project;

    /// <summary>
    /// A builder for a Shipping package (ADR 0103): against the shipping
    /// preset's engine assembly, <paramref name="engine"/>, with
    /// <c>CRUCIBLE_SHIPPING</c> defined instead of <c>CRUCIBLE_LOG_INFO</c>,
    /// so the game's information messages compile out. Its projects are kept
    /// apart from the editor's (Cache/Build/PackageShipping, AotShipping).
    /// </summary>
    public GameBuilder(Project project, string engine) : this(project)
    {
        _engine = engine;
        _shipping = true;
    }

    // "Shipping" for a Shipping builder's folders, "" for the editor's.
    private string Variant => _shipping ? "Shipping" : "";

    /// <summary>The project's game assembly's name, the project's own: "Diorama" (ADR 0100).</summary>
    public string AssemblyName => Sanitize(_project.Name);

    public string ProjectFile => Path.Combine(_project.CacheFolder, "Build", AssemblyName + ".csproj");

    /// <summary>Where the built assembly lands.</summary>
    public string OutputPath => Path.Combine(_project.CacheFolder, "Build", "bin", AssemblyName + ".dll");

    /// <summary>
    /// Whether the project has code to build: a Code folder with .cs files in
    /// it, or a plugin with code (ADR 0118), which the game's assembly names.
    /// </summary>
    public bool HasCode =>
        (Directory.Exists(_project.CodeFolder) &&
         Directory.EnumerateFiles(_project.CodeFolder, "*.cs", SearchOption.AllDirectories).Any()) ||
        _project.Plugins.Any(plugin => plugin.HasCode);

    // The plugins with code, in the order they load (ADR 0118); the source
    // ones are built with the game, the compiled ones referenced.
    private IEnumerable<Plugin> CodePlugins => _project.Plugins.Where(plugin => plugin.HasCode);

    private IEnumerable<Plugin> SourcePlugins => CodePlugins.Where(plugin => !plugin.IsCompiled);

    public bool IsBuilding => _running is { IsCompleted: false };

    /// <summary>Starts a build in the background; false if one is already running.</summary>
    public bool Start()
    {
        if (IsBuilding)
        {
            return false;
        }
        WriteProject();
        _running = Task.Run(RunBuild);
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

    /// <summary>Builds now, waiting for it (opening a project, before its scenes load).</summary>
    public GameBuild BuildNow()
    {
        _running?.Wait();
        _running = null;
        WriteProject();
        return RunBuild();
    }

    /// <summary>The project file a package is published from (ADR 0100): Cache/Build/Package/&lt;Project&gt;.csproj.</summary>
    public string PackageProjectFile => Path.Combine(_project.CacheFolder, "Build", "Package" + Variant, AssemblyName + ".csproj");

    /// <summary>
    /// The project file's text: the Code folder's sources, the engine, and its
    /// generators. For a package (<paramref name="package"/>), also the
    /// runtime for this platform, copied in with the engine, and ReadyToRun
    /// unless <paramref name="readyToRun"/> is false (ADR 0100).
    /// </summary>
    /// <remarks>
    /// Code under a folder named <c>Editor</c> is left out: it's the editor
    /// assembly's (ADR 0116). <c>CRUCIBLE_EDITOR</c> is defined for the
    /// editor's own build (<paramref name="editorSymbol"/>, by default every
    /// build but a package's).
    /// </remarks>
    public string ProjectText(bool package = false, bool readyToRun = true, bool? editorSymbol = null)
    {
        string engine = _engine ?? EngineAssembly();
        StringBuilder text = new();
        text.Append(Header(AssemblyName, Sanitize(_project.Name), "bin/", editorSymbol ?? !package));
        if (package)
        {
            // The game's assembly is the app its runtime starts for: its
            // runtimeconfig.json and deps.json name the runtime, copied in
            // (self-contained), and every assembly (ADR 0100).
            text.Append($"    <RuntimeIdentifier>{RuntimeIdentifier}</RuntimeIdentifier>\n");
            text.Append($"    <PublishReadyToRun>{(readyToRun ? "true" : "false")}</PublishReadyToRun>\n");
            text.Append("""
                    <SelfContained>true</SelfContained>
                    <GenerateRuntimeConfigurationFiles>true</GenerateRuntimeConfigurationFiles>
                    <UseAppHost>false</UseAppHost>
                    <Optimize>true</Optimize>

                """);
        }
        text.Append("""
              </PropertyGroup>
              <ItemGroup>

            """);
        text.Append($"    <Compile Include=\"{Escape(Path.Combine(_project.CodeFolder, "**", "*.cs"))}\" " +
                    $"Exclude=\"{Escape(EditorGlob)}\" />\n");
        // The plugins it uses (ADR 0118): the list the game loader reads, the
        // source ones' projects, and the compiled ones' assemblies.
        string variant = VariantFolder(package, editorSymbol);
        text.Append($"    <Compile Include=\"{Escape(Path.Combine(variant, PluginListFile))}\" Condition=\"Exists('{Escape(Path.Combine(variant, PluginListFile))}')\" />\n");
        text.Append(PluginReferences(CodePlugins, variant, copySource: package, copyCompiled: true));
        // The native module's bindings (ADR 0074), when it exposes anything.
        string bindings = Escape(Path.Combine(_project.CacheFolder, "Build", "Native" + Variant, "Bindings.g.cs"));
        text.Append($"    <Compile Include=\"{bindings}\" Condition=\"Exists('{bindings}')\" />\n");
        // Referenced, not copied, except into a package.
        text.Append($"    <Reference Include=\"Crucible.Engine\" HintPath=\"{Escape(engine)}\" Private=\"{(package ? "true" : "false")}\" />\n");
        if (Generators() is { } generators)
        {
            text.Append($"    <Analyzer Include=\"{Escape(generators)}\" />\n");
        }
        text.Append("""
              </ItemGroup>
            </Project>

            """);
        return text.ToString();
    }

    // A project's start (ADR 0072): its framework, names, the engine's
    // settings, where it builds to, and its symbols. Log.Info is kept only
    // where CRUCIBLE_LOG_INFO is defined: every build but a Shipping
    // package's (ADR 0103).
    private string Header(string assemblyName, string rootNamespace, string outputPath, bool editorSymbol, bool aotCompatible = true)
    {
        string framework = typeof(Crucible.Ecs.World).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName
            is { } name && name.Contains("Version=v", StringComparison.Ordinal)
            ? "net" + name[(name.IndexOf("Version=v", StringComparison.Ordinal) + 9)..]
            : "net10.0";
        StringBuilder text = new();
        text.Append("""
            <Project Sdk="Microsoft.NET.Sdk">
              <!-- Written by the Crucible editor (ADR 0072); rewritten before every build. -->
              <PropertyGroup>

            """);
        text.Append($"    <TargetFramework>{framework}</TargetFramework>\n");
        text.Append($"    <AssemblyName>{assemblyName}</AssemblyName>\n");
        text.Append($"    <RootNamespace>{rootNamespace}</RootNamespace>\n");
        text.Append("""
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>

            """);
        text.Append($"    <IsAotCompatible>{(aotCompatible ? "true" : "false")}</IsAotCompatible>\n");
        text.Append("""
                <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
                <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
                <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>

            """);
        text.Append($"    <OutputPath>{Escape(outputPath)}</OutputPath>\n");
        text.Append("    <GenerateDocumentationFile>false</GenerateDocumentationFile>\n");
        string editor = editorSymbol ? ";CRUCIBLE_EDITOR" : "";
        text.Append($"    <DefineConstants>$(DefineConstants);{(_shipping ? "CRUCIBLE_SHIPPING" : "CRUCIBLE_LOG_INFO")}{editor}</DefineConstants>\n");
        return text.ToString();
    }

    /// <summary>
    /// Whether the game's code (not its Editor folders') mentions
    /// <c>CRUCIBLE_EDITOR</c>: then each build is followed by a check that it
    /// builds without it (ADR 0116), as a package will.
    /// </summary>
    /// <remarks>Source plugins' code (not their Editor folders') counts too (ADR 0118).</remarks>
    public bool UsesEditorSymbol =>
        ((string[])[_project.CodeFolder, .. SourcePlugins.Select(plugin => plugin.CodeFolder)])
        .Where(Directory.Exists)
        .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories).Where(file => !IsEditorSourceIn(root, file)))
        .Any(file => File.ReadAllText(file).Contains(EditorSymbol, StringComparison.Ordinal));

    /// <summary>The symbol defined in the editor's own build of the game's code (ADR 0002, 0116).</summary>
    public const string EditorSymbol = "CRUCIBLE_EDITOR";

    private Task<GameBuild>? _symbolCheck;

    /// <summary>The game's project without <c>CRUCIBLE_EDITOR</c>, in a folder of its own.</summary>
    public string SymbolCheckProjectFile => Path.Combine(_project.CacheFolder, "Build", "SymbolCheck", AssemblyName + ".csproj");

    /// <summary>Starts building the game's code without <c>CRUCIBLE_EDITOR</c> in the background; false if one is running.</summary>
    public bool StartSymbolCheck()
    {
        if (_symbolCheck is { IsCompleted: false })
        {
            return false;
        }
        Write(SymbolCheckProjectFile, ProjectText(editorSymbol: false));
        WritePlugins(package: false, editorSymbol: false);
        string output = Path.Combine(Path.GetDirectoryName(SymbolCheckProjectFile)!, "bin", AssemblyName + ".dll");
        _symbolCheck = Task.Run(() => RunDotnet(["build", SymbolCheckProjectFile], output));
        return true;
    }

    /// <summary>The finished symbol check, once; null while it's running or if none was started.</summary>
    public GameBuild? TakeSymbolCheck()
    {
        if (_symbolCheck is not { IsCompleted: true } done)
        {
            return null;
        }
        _symbolCheck = null;
        return done.IsFaulted
            ? new GameBuild(false, null, [new BuildError("", 0, 0, done.Exception?.GetBaseException().Message ?? "the check failed")], "")
            : done.Result;
    }

    /// <summary>Waits for a symbol check started (the checks wait on it).</summary>
    public GameBuild? WaitForSymbolCheck()
    {
        _symbolCheck?.Wait();
        return TakeSymbolCheck();
    }

    // Every .cs file under a folder named Editor, at any depth in Code.
    private string EditorGlob => Path.Combine(_project.CodeFolder, "**", EditorFolder, "**", "*.cs");

    /// <summary>The name of the folders whose code is the editor assembly's (ADR 0116).</summary>
    public const string EditorFolder = "Editor";

    /// <summary>The project's editor assembly's name: "Walker.Editor" (ADR 0116).</summary>
    public string EditorAssemblyName => AssemblyName + ".Editor";

    /// <summary>Its project file, in a folder of its own so the two projects' obj folders stay apart.</summary>
    public string EditorProjectFile => Path.Combine(_project.CacheFolder, "Build", EditorFolder, EditorAssemblyName + ".csproj");

    /// <summary>Where the editor assembly lands, beside the game's.</summary>
    public string EditorOutputPath => Path.Combine(_project.CacheFolder, "Build", "bin", EditorAssemblyName + ".dll");

    /// <summary>Whether the project has editor code: .cs files under a folder named Editor in Code.</summary>
    public bool HasEditorCode => EditorSources().Any();

    private IEnumerable<string> EditorSources() => EditorSourcesIn(_project.CodeFolder);

    // A code folder's editor code: .cs files under a folder named Editor in it.
    private static IEnumerable<string> EditorSourcesIn(string root) =>
        Directory.Exists(root)
            ? Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories).Where(file => IsEditorSourceIn(root, file))
            : [];

    private static bool IsEditorSourceIn(string root, string file) =>
        Path.GetRelativePath(root, file).Replace('\\', '/').Split('/').SkipLast(1).Contains(EditorFolder);

    /// <summary>
    /// The editor project's text (ADR 0116): the Editor folders' code, with
    /// <c>CRUCIBLE_EDITOR</c>, referencing the game's project and the
    /// editor's assemblies; built into the game's output folder.
    /// </summary>
    public string EditorProjectText()
    {
        string game = ProjectText();
        int start = game.IndexOf("    <AssemblyName>", StringComparison.Ordinal);
        int end = game.IndexOf("</AssemblyName>\n", start, StringComparison.Ordinal) + "</AssemblyName>\n".Length;
        StringBuilder text = new(game[..start]);
        text.Append($"    <AssemblyName>{EditorAssemblyName}</AssemblyName>\n");
        text.Append(game[end..game.IndexOf("  <ItemGroup>", StringComparison.Ordinal)]
            .Replace("<OutputPath>bin/</OutputPath>", "<OutputPath>../bin/</OutputPath>", StringComparison.Ordinal)
            .Replace("<IsAotCompatible>true</IsAotCompatible>", "<IsAotCompatible>false</IsAotCompatible>", StringComparison.Ordinal));
        text.Append("  <ItemGroup>\n");
        text.Append($"    <Compile Include=\"{Escape(EditorGlob)}\" />\n");
        text.Append($"    <ProjectReference Include=\"{Escape(ProjectFile)}\" Private=\"false\" />\n");
        foreach (string assembly in (string[])[_engine ?? EngineAssembly(), EditorAssembly(), AssetsAssembly()])
        {
            text.Append($"    <Reference Include=\"{Path.GetFileNameWithoutExtension(assembly)}\" HintPath=\"{Escape(assembly)}\" Private=\"false\" />\n");
        }
        // Every plugin's assemblies, its editor assembly too (ADR 0118): the
        // game's project passes on only its source plugins' to the compiler.
        string variant = VariantFolder(false, null);
        text.Append(PluginReferences(CodePlugins, variant, copySource: false, copyCompiled: false));
        text.Append(PluginEditorReferences(CodePlugins, variant));
        text.Append("""
              </ItemGroup>
            </Project>

            """);
        return text.ToString();
    }

    private void WriteProject()
    {
        Write(ProjectFile, ProjectText());
        WritePlugins(package: false, editorSymbol: null);
        if (NeedsEditorProject)
        {
            Write(EditorProjectFile, EditorProjectText());
        }
    }

    // Unchanged, a project file is left alone, so the build stays incremental.
    private static void Write(string file, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        if (!File.Exists(file) || File.ReadAllText(file) != text)
        {
            File.WriteAllText(file, text);
        }
    }

    /// <summary>"linux-x64" or "win-x64": the only platform a package is made for is this one.</summary>
    public static string RuntimeIdentifier => OperatingSystem.IsWindows() ? "win-x64" : "linux-x64";

    /// <summary>
    /// Publishes the game into <paramref name="folder"/> with its own .NET
    /// runtime and the engine's assemblies (ADR 0100), waiting for it. The
    /// first publish downloads the runtime and ReadyToRun compiler from NuGet.
    /// </summary>
    public GameBuild Publish(string folder, bool readyToRun)
    {
        _running?.Wait();
        _running = null;
        Directory.CreateDirectory(Path.GetDirectoryName(PackageProjectFile)!);
        File.WriteAllText(PackageProjectFile, ProjectText(package: true, readyToRun));
        WritePlugins(package: true, editorSymbol: null);
        string assembly = Path.Combine(folder, AssemblyName + ".dll");
        return RunDotnet(["publish", PackageProjectFile, "-c", "Release", "-o", folder], assembly);
    }

    /// <summary>Where a NativeAOT build happens (ADR 0101): Cache/Build/Aot.</summary>
    public string AotFolder => Path.Combine(_project.CacheFolder, "Build", "Aot" + Variant);

    /// <summary>The NativeAOT project (ADR 0101): Cache/Build/Aot/&lt;Project&gt;.csproj.</summary>
    public string AotProjectFile => Path.Combine(AotFolder, AssemblyName + ".csproj");

    /// <summary>The runtime's libraries a NativeAOT build lists for the link (ADR 0101).</summary>
    public string AotLinkInputs => Path.Combine(AotFolder, "link.rsp");

    /// <summary>
    /// The NativeAOT project's text (ADR 0101): the game's code and a
    /// bootstrap handing the engine its assembly, compiled with the engine
    /// into one static library that calls the core and the game's C++ module
    /// directly (<paramref name="module"/>, if it has one).
    /// </summary>
    public string AotProjectText(string? module, string instructionSet)
    {
        string text = ProjectText(package: true, readyToRun: false);
        // Not self-contained .NET but NativeAOT: the same project otherwise.
        text = text.Replace("    <PublishReadyToRun>false</PublishReadyToRun>\n", "", StringComparison.Ordinal)
            .Replace("    <SelfContained>true</SelfContained>\n", "", StringComparison.Ordinal)
            .Replace("    <GenerateRuntimeConfigurationFiles>true</GenerateRuntimeConfigurationFiles>\n", "", StringComparison.Ordinal)
            .Replace("    <UseAppHost>false</UseAppHost>\n", "", StringComparison.Ordinal);
        StringBuilder aot = new();
        aot.Append("    <PublishAot>true</PublishAot>\n");
        aot.Append("    <NativeLib>Static</NativeLib>\n");
        if (instructionSet.Length > 0)
        {
            aot.Append($"    <IlcInstructionSet>{instructionSet}</IlcInstructionSet>\n");
        }
        text = text.Replace("  </PropertyGroup>\n", aot + "  </PropertyGroup>\n", StringComparison.Ordinal);
        StringBuilder items = new();
        items.Append($"    <Compile Include=\"{Escape(Path.Combine(AotFolder, "Bootstrap.g.cs"))}\" />\n");
        items.Append("    <DirectPInvoke Include=\"Crucible.Core\" />\n");
        // The engine's entry, crucible_engine_run, which the launcher calls:
        // NativeAOT exports only the published assembly's unless told.
        items.Append("    <UnmanagedEntryPointsAssembly Include=\"Crucible.Engine\" />\n");
        // Nothing outside the game refers to it, so it's kept whole: its
        // bootstrap, and the systems and components its attributes name.
        items.Append($"    <TrimmerRootAssembly Include=\"{AssemblyName}\" />\n");
        // Its plugins too (ADR 0118): the game loader finds them by name.
        foreach (Plugin plugin in CodePlugins)
        {
            items.Append($"    <TrimmerRootAssembly Include=\"{plugin.Name}\" />\n");
        }
        if (module is not null)
        {
            items.Append($"    <DirectPInvoke Include=\"{module}\" />\n");
        }
        text = text.Replace("  </ItemGroup>\n", items + "  </ItemGroup>\n", StringComparison.Ordinal);
        // The runtime's support libraries, which the final link needs: NativeAOT
        // knows them, so it writes them out (as Directory.Build.targets does
        // for the engine's own AOT launcher).
        string linkInputs = Escape(AotLinkInputs);
        text = text.Replace("</Project>", $$"""
              <Target Name="CrucibleWriteNativeLinkInputs" AfterTargets="SetupOSSpecificProps">
                <ItemGroup Condition="'$(OS)' != 'Windows_NT'">
                  <_CrucibleLinkInput Include="@(NativeLibrary->'%(FullPath)')" />
                  <_CrucibleLinkInput Include="@(NativeSystemLibrary->'-l%(Identity)')" />
                </ItemGroup>
                <ItemGroup Condition="'$(OS)' == 'Windows_NT'">
                  <_CrucibleLinkInput Include="@(NativeLibrary->'&quot;%(FullPath)&quot;')" />
                  <_CrucibleLinkInput Include="@(SdkNativeLibrary)" />
                </ItemGroup>
                <WriteLinesToFile File="{{linkInputs}}" Lines="@(_CrucibleLinkInput)" Overwrite="true" WriteOnlyWhenDifferent="true" />
              </Target>
            </Project>
            """, StringComparison.Ordinal);
        return text;
    }

    /// <summary>
    /// Compiles the game and the engine with NativeAOT into a static library
    /// (ADR 0101), waiting for it; the result's assembly is the library's path.
    /// </summary>
    public GameBuild PublishAot(string? module, string instructionSet)
    {
        _running?.Wait();
        _running = null;
        Directory.CreateDirectory(AotFolder);
        File.WriteAllText(AotProjectFile, AotProjectText(module, instructionSet));
        // Its plugins' projects are the package's (ADR 0118).
        WritePlugins(package: true, editorSymbol: null);
        File.WriteAllText(Path.Combine(AotFolder, "Bootstrap.g.cs"), """
            // Written by the Crucible editor (ADR 0101): hands the engine this
            // game, compiled into its NativeAOT executable, before it starts.
            namespace Crucible.Generated;

            internal static class CompiledInGame
            {
            #pragma warning disable CA2255 // A module initializer is the point: it runs before the engine's entry.
                [global::System.Runtime.CompilerServices.ModuleInitializer]
                internal static void Register() =>
                    global::Crucible.Hosting.GameLoader.CompiledIn = typeof(CompiledInGame).Assembly;
            #pragma warning restore CA2255
            }

            """);
        string publish = Path.Combine(AotFolder, "publish");
        string library = Path.Combine(publish, AssemblyName + (OperatingSystem.IsWindows() ? ".lib" : ".a"));
        return RunDotnet(["publish", AotProjectFile, "-c", "Release", "-o", publish], library);
    }

    /// <summary>
    /// The stamp of the last build that succeeded (ADR 0110): a hash of its
    /// inputs, the hash of the assembly it made, and the folder it was built in.
    /// </summary>
    public string StampFile => Path.Combine(_project.CacheFolder, "Build", AssemblyName + Variant + ".stamp");

    /// <summary>
    /// A hash of everything the editor's build depends on (ADR 0110): the
    /// project file, with the project's folder left out so a copy hashes the
    /// same; every .cs file under Code, by its path there and its contents;
    /// the native module's bindings; and the engine and its generators.
    /// </summary>
    public string InputHash()
    {
        XxHash128 hash = new();
        void Add(string text) => hash.Append(Encoding.UTF8.GetBytes(text + "\n"));
        Add(Relocatable(ProjectText()));
        Add(NeedsEditorProject
            ? Relocatable(EditorProjectText()) + ContentHash.OfFile(EditorAssembly())
            : "no editor code");
        HashPlugins(Add);
        if (Directory.Exists(_project.CodeFolder))
        {
            foreach (string file in Directory.EnumerateFiles(_project.CodeFolder, "*.cs", SearchOption.AllDirectories)
                         .Select(file => Path.GetRelativePath(_project.CodeFolder, file).Replace('\\', '/'))
                         .Order(StringComparer.Ordinal))
            {
                Add(file);
                Add(ContentHash.OfFile(Path.Combine(_project.CodeFolder, file)));
            }
        }
        string bindings = Path.Combine(_project.CacheFolder, "Build", "Native" + Variant, "Bindings.g.cs");
        Add(File.Exists(bindings) ? ContentHash.OfFile(bindings) : "no bindings");
        Add(ContentHash.OfFile(_engine ?? EngineAssembly()));
        Add(Generators() is { } generators ? ContentHash.OfFile(generators) : "no generators");
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>
    /// Whether the last build's assembly is still what a build would make
    /// (ADR 0110): its stamp's inputs match, and the assembly is the one it made.
    /// </summary>
    public bool IsUpToDate() => Current(ReadStamp(), InputHash());

    private bool Current(Stamp? stamp, string inputs) =>
        stamp is not null && stamp.Inputs == inputs && OutputHash() is { } output && stamp.Output == output;

    // The game's assembly's hash, the editor's after it when there's editor
    // code, then the source plugins'; null if one is missing.
    private string? OutputHash()
    {
        if (!File.Exists(OutputPath) || PluginOutputHash() is not { } plugins)
        {
            return null;
        }
        if (!NeedsEditorProject)
        {
            return ContentHash.OfFile(OutputPath) + plugins;
        }
        return File.Exists(EditorOutputPath) ? ContentHash.OfFile(OutputPath) + "+" + ContentHash.OfFile(EditorOutputPath) + plugins : null;
    }

    private sealed record Stamp(string Inputs, string Output, string Root);

    private Stamp? ReadStamp()
    {
        try
        {
            string[] lines = File.Exists(StampFile) ? File.ReadAllLines(StampFile) : [];
            return lines.Length == 4 && lines[0] == "crucible-build-stamp 1"
                ? new Stamp(lines[1], lines[2], lines[3])
                : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private GameBuild RunBuild()
    {
        string inputs = InputHash();
        Stamp? stamp = ReadStamp();
        bool editor = NeedsEditorProject;
        if (Current(stamp, inputs))
        {
            return WithPlugins(new GameBuild(true, OutputPath, [], "", UpToDate: true) { EditorAssembly = editor ? EditorOutputPath : null });
        }
        // dotnet's obj folders hold the project's absolute paths: a project
        // moved (or copied) with its Cache restores and compiles from scratch.
        if (stamp?.Root != _project.Root)
        {
            foreach (string obj in Directory.Exists(Path.GetDirectoryName(ProjectFile))
                         ? Directory.EnumerateDirectories(Path.GetDirectoryName(ProjectFile)!, "obj", SearchOption.AllDirectories)
                         : [])
            {
                Directory.Delete(obj, recursive: true);
            }
        }
        File.Delete(StampFile);
        // The editor project builds the game's too, which it references.
        GameBuild build = RunDotnet(["build", editor ? EditorProjectFile : ProjectFile], editor ? EditorOutputPath : OutputPath);
        if (editor)
        {
            build = build.Succeeded && File.Exists(OutputPath)
                ? build with { Assembly = OutputPath, EditorAssembly = EditorOutputPath }
                : build with { Succeeded = false, Assembly = null };
        }
        if (build.Succeeded && OutputHash() is { } output)
        {
            File.WriteAllLines(StampFile, ["crucible-build-stamp 1", inputs, output, _project.Root]);
        }
        return build.Succeeded ? WithPlugins(build) : build;
    }

    private static GameBuild RunDotnet(string[] command, string result)
    {
        if (DotnetPath() is not { } dotnet)
        {
            return new GameBuild(false, null, [new BuildError("", 0, 0, "no dotnet found to build with (set DOTNET_ROOT)")], "");
        }
        ProcessStartInfo start = new(dotnet)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            // Outside any repository the project sits in: a global.json there
            // could ask for an SDK the dotnet the editor runs on doesn't have.
            WorkingDirectory = Path.GetTempPath(),
        };
        foreach (string argument in (string[])[
                     .. command, "-nologo", "-v:q", "-clp:NoSummary",
                     // Not the settings of a repository the project happens to sit in.
                     "-p:ImportDirectoryBuildProps=false", "-p:ImportDirectoryBuildTargets=false",
                     "-p:ImportDirectoryPackagesProps=false"])
        {
            start.ArgumentList.Add(argument);
        }
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment["DOTNET_NOLOGO"] = "1";
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("dotnet didn't start");
        Task<string> error = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        string all = output + error.Result;
        List<BuildError> errors = [.. Errors(all)];
        bool built = process.ExitCode == 0 && File.Exists(result);
        if (!built && errors.Count == 0)
        {
            string first = all.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0) ?? "";
            errors.Add(new BuildError("", 0, 0, $"dotnet {command[0]} failed ({process.ExitCode}): {first}"));
        }
        return new GameBuild(built, built ? result : null, errors, all);
    }

    // "path(12,5): error CS1002: ; expected [project]"
    [GeneratedRegex(@"^(?<file>.+?)\((?<line>\d+),(?<column>\d+)\): error (?<code>\w+): (?<message>.*?)( \[[^\]]*\])?$", RegexOptions.Multiline)]
    private static partial Regex ErrorLine();

    internal static IEnumerable<BuildError> Errors(string output) =>
        ErrorLine().Matches(output).Select(match => new BuildError(match.Groups["file"].Value.Trim(),
                int.Parse(match.Groups["line"].Value, System.Globalization.CultureInfo.InvariantCulture),
                int.Parse(match.Groups["column"].Value, System.Globalization.CultureInfo.InvariantCulture),
                $"{match.Groups["code"].Value}: {match.Groups["message"].Value.Trim()}"))
            .Distinct();

    // The dotnet the editor runs on, if its SDK is there; else DOTNET_ROOT, then PATH.
    private static string? DotnetPath()
    {
        string exe = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "dotnet.exe" : "dotnet";
        List<string> roots = [];
        // .../dotnet/shared/Microsoft.NETCore.App/<version>/ → .../dotnet
        string runtime = RuntimeEnvironment.GetRuntimeDirectory();
        if (Directory.GetParent(runtime.TrimEnd(Path.DirectorySeparatorChar))?.Parent?.Parent is { } root)
        {
            roots.Add(root.FullName);
        }
        if (Environment.GetEnvironmentVariable("DOTNET_ROOT") is { Length: > 0 } dotnetRoot)
        {
            roots.Add(dotnetRoot);
        }
        roots.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator));
        return roots.Select(folder => Path.Combine(folder, exe)).FirstOrDefault(File.Exists);
    }

    // The engine's source generators: beside the engine (shipped), or in the build's artifacts.
    private static string? Generators()
    {
        string engineDir = Path.GetDirectoryName(EngineAssembly())!;
        string beside = Path.Combine(engineDir, "Crucible.Generators.dll");
        if (File.Exists(beside))
        {
            return beside;
        }
        // artifacts/bin/Crucible.Engine/<config> → artifacts/bin/Crucible.Generators/<config>
        string config = Path.GetFileName(engineDir);
        string built = Path.Combine(engineDir, "..", "..", "Crucible.Generators", config, "Crucible.Generators.dll");
        return File.Exists(built) ? Path.GetFullPath(built) : null;
    }

    // The editor runs JIT only (ADR 0016), never as a single-file app.
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("SingleFile", "IL3000",
        Justification = "The editor is never published as a single file")]
    internal static string EngineAssembly() => typeof(Crucible.Ecs.World).Assembly.Location;

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("SingleFile", "IL3000",
        Justification = "The editor is never published as a single file")]
    internal static string EditorAssembly() =>
        typeof(GameBuilder).Assembly.Location is { Length: > 0 } location
            ? location
            // Loaded from a stream as the launcher's game (Dependencies): at its --game path.
            : Path.GetFullPath(Crucible.Api.GamePath());

    // Beside the editor, where Dependencies finds it.
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("SingleFile", "IL3000",
        Justification = "The editor is never published as a single file")]
    internal static string AssetsAssembly() =>
        typeof(Crucible.Assets.AssetDatabase).Assembly.Location is { Length: > 0 } location
            ? location
            : Path.Combine(Path.GetDirectoryName(EditorAssembly())!, "Crucible.Assets.dll");

    // An identifier, and not a name already taken in a package's Data folder
    // (ADR 0100): the engine's core (Crucible.Core) or the runtime's own
    // assemblies (System.dll, mscorlib.dll).
    internal static string Sanitize(string name)
    {
        string kept = new([.. name.Where(c => char.IsLetterOrDigit(c) || c == '_')]);
        bool taken = kept.Equals("Crucible", StringComparison.OrdinalIgnoreCase) ||
                     File.Exists(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), kept + ".dll"));
        return kept.Length == 0 || char.IsDigit(kept[0]) || taken ? "Game" + kept : kept;
    }

    private static string Escape(string path) => System.Security.SecurityElement.Escape(path.Replace('\\', '/'))!;
}

/// <summary>An error from building a project's code: its file, line, and column (0 when it has none), and the message.</summary>
public sealed record BuildError(string File, int Line, int Column, string Message)
{
    // The column too: errors on one line would otherwise read as repeats.
    public override string ToString() => File.Length == 0 ? Message : $"{File}({Line},{Column}): {Message}";
}

/// <summary>
/// A finished build: whether it succeeded, the assembly, its errors, and
/// dotnet's whole output; <c>UpToDate</c> when nothing was built, the last
/// build's assembly being current (ADR 0110).
/// </summary>
public sealed record GameBuild(bool Succeeded, string? Assembly, IReadOnlyList<BuildError> Errors, string Output, bool UpToDate = false)
{
    /// <summary>The project's editor assembly (ADR 0116), built with the game's; null when it has no editor code.</summary>
    public string? EditorAssembly { get; init; }

    /// <summary>The source plugins' assemblies built with it (ADR 0118), in the order they load.</summary>
    public IReadOnlyList<string> PluginAssemblies { get; init; } = [];

    /// <summary>Their editor assemblies, for those with editor code, in the same order.</summary>
    public IReadOnlyList<string> PluginEditorAssemblies { get; init; } = [];
}
