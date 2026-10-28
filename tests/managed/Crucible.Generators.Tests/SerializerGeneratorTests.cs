using System.Collections.Immutable;
using Crucible.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Crucible.Tests.Generators;

/// <summary>
/// Runs the serializer generator (ADR 0038) on small sources: what it writes,
/// that the result compiles, and the errors it reports. What the generated
/// code does at runtime is tested in Crucible.Engine.Tests.
/// </summary>
public class SerializerGeneratorTests
{
    private const string Usings = "using Crucible.Serialization;\nusing System.Text.Json.Nodes;\n";

    private static (string[] Sources, ImmutableArray<Diagnostic> Generator, Diagnostic[] Errors) Run(string source) =>
        GeneratorHarness.Run(new SerializerGenerator(), Usings + source);

    [Test]
    public async Task WritesOneCompilingFilePerTypeWithMembersInDeclarationOrder()
    {
        (string[] sources, ImmutableArray<Diagnostic> diagnostics, Diagnostic[] errors) = Run("""
            namespace Game;

            [Serialized]
            public partial struct Stats { public int Zeta; public float Alpha; }

            public static partial class Outer
            {
                [Serialized(Version = 2)]
                internal partial record Settings
                {
                    public Stats Stats { get; set; }
                    [FormerlyNamed("Old")] public string Name = "";
                    [Serialized] private int _hidden;
                    public int Computed => _hidden;
                    static void Migrate(JsonObject data, int fromVersion) { }
                }
            }
            """);
        await Assert.That(diagnostics).IsEmpty();
        await Assert.That(errors).IsEmpty();
        await Assert.That(sources.Length).IsEqualTo(2);
        string stats = sources.Single(source => source.Contains("partial struct Stats"));
        await Assert.That(stats.IndexOf("\"Zeta\"u8", StringComparison.Ordinal))
            .IsLessThan(stats.IndexOf("\"Alpha\"u8", StringComparison.Ordinal));
        string settings = sources.Single(source => source.Contains("partial record Settings"));
        await Assert.That(settings).Contains("partial class Outer");
        await Assert.That(settings).Contains("reader.ValueTextEquals(\"Name\"u8) || reader.ValueTextEquals(\"Old\"u8)");
        await Assert.That(settings).Contains("\"_hidden\"u8");
        await Assert.That(settings).DoesNotContain("Computed");
        await Assert.That(settings).Contains("writer.WriteNumber(\"$version\"u8, 2);");
    }

    // Each misuse and the error it gives. "Run" is a member every case can share.
    [Test]
    [Arguments("CRU0101", "[Serialized] public class NotPartial { }")]
    [Arguments("CRU0101", "public class Outer { [Serialized] public partial class Inner { } }")]
    [Arguments("CRU0102", "[Serialized] public partial class Generic<T> { }")]
    [Arguments("CRU0102", "[Serialized] public abstract partial class Abstract { }")]
    [Arguments("CRU0102", "public class Base { } [Serialized] public partial class Derived : Base { }")]
    [Arguments("CRU0102", "[Serialized] public ref partial struct Stack { }")]
    [Arguments("CRU0103", "[Serialized] public partial class NeedsArgs { public NeedsArgs(int x) { } }")]
    [Arguments("CRU0104", "[Serialized] public partial class Anything { public object Value = 1; }")]
    [Arguments("CRU0104", "[Serialized] public partial class IntKeys { public System.Collections.Generic.Dictionary<int, int> Map = new(); }")]
    [Arguments("CRU0104", "[Serialized] public partial class Grid { public int[,] Cells = new int[1, 1]; }")]
    [Arguments("CRU0105", "[Serialized] public partial class Fixed { public readonly int Value; }")]
    [Arguments("CRU0105", "[Serialized] public partial class Init { public int Value { get; init; } }")]
    [Arguments("CRU0105", "[Serialized] public partial class Computed { [Serialized] private int Value => 1; }")]
    [Arguments("CRU0106", "[Serialized] public partial class Clash { [FormerlyNamed(\"B\")] public int A; public int B; }")]
    [Arguments("CRU0107", "[Serialized(Version = 2)] public partial class NoMigrate { }")]
    [Arguments("CRU0107", "[Serialized(Version = 0)] public partial class Zero { }")]
    [Arguments("CRU0107", "[Serialized(Version = 2)] public partial class WrongMigrate { static void Migrate(JsonObject data) { } }")]
    [Arguments("CRU0108", "public class Plain { [Serialized] private int _value; }")]
    [Arguments("CRU0108", "[Serialized] public partial class Both { [Serialized, NotSerialized] public int Value; }")]
    public async Task ReportsMisuse(string id, string source)
    {
        (string[] generated, ImmutableArray<Diagnostic> diagnostics, _) = Run(source);
        await Assert.That(string.Join(" ", diagnostics.Select(diagnostic => diagnostic.Id))).IsEqualTo(id);
        FileLinePositionSpan where = diagnostics[0].Location.GetLineSpan();
        await Assert.That(where.Path).IsEqualTo("Game.cs");
        await Assert.That(where.StartLinePosition.Line).IsEqualTo(2); // after the usings
        // A type with errors gets no code, so its own errors are the only ones.
        await Assert.That(generated).IsEmpty();
    }

    [Test]
    public async Task UnrelatedEditsReuseTheCachedResult()
    {
        CSharpCompilation compilation =
            GeneratorHarness.Compile(Usings + "[Serialized] public partial struct Stats { public int Health; }");
        GeneratorDriver driver = GeneratorHarness.Driver(new SerializerGenerator()).RunGenerators(compilation);
        driver = driver.RunGenerators(compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText("public class Unrelated { }")));
        IncrementalStepRunReason[] reasons =
        [
            .. driver.GetRunResult().Results.Single().TrackedOutputSteps.SelectMany(step => step.Value)
                .SelectMany(run => run.Outputs).Select(output => output.Reason),
        ];
        await Assert.That(reasons).IsNotEmpty();
        await Assert.That(reasons.All(reason => reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged)).IsTrue();
    }
}
