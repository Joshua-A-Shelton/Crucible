using System.Collections.Immutable;
using Crucible.Loop;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Crucible.Tests.Generators;

// Runs one of Crucible's generators on small sources with Roslyn's own driver.
internal static class GeneratorHarness
{
    // The running .NET's assemblies plus the engine: enough to compile a game
    // source that uses the engine's attributes and types.
    private static readonly MetadataReference[] _references =
    [
        .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Where(path => Path.GetFileName(path) is not "Crucible.Engine.dll")
            .Select(path => MetadataReference.CreateFromFile(path)),
        MetadataReference.CreateFromFile(typeof(GameSystem).Assembly.Location),
    ];

    public static CSharpCompilation Compile(params string[] sources) =>
        CSharpCompilation.Create("Game",
            sources.Select(source => CSharpSyntaxTree.ParseText(source, path: "Game.cs")),
            _references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

    public static GeneratorDriver Driver(IIncrementalGenerator generator) =>
        CSharpGeneratorDriver.Create([generator.AsSourceGenerator()],
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

    // The generated sources, the generator's diagnostics, and every error in
    // the compilation with the generated code added.
    public static (string[] Sources, ImmutableArray<Diagnostic> Generator, Diagnostic[] Errors) Run(
        IIncrementalGenerator generator, string source)
    {
        GeneratorDriver driver = Driver(generator).RunGeneratorsAndUpdateCompilation(Compile(source),
            out Compilation output, out ImmutableArray<Diagnostic> diagnostics);
        GeneratorRunResult result = driver.GetRunResult().Results.Single();
        string[] generated = [.. result.GeneratedSources.Select(generatedSource => generatedSource.SourceText.ToString())];
        Diagnostic[] errors = [.. output.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)];
        return (generated, diagnostics, errors);
    }
}
