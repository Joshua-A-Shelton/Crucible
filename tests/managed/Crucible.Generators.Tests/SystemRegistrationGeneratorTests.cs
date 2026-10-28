using System.Collections.Immutable;
using Crucible.Generators;
using Crucible.Loop;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Crucible.Tests.Generators;

/// <summary>
/// Runs the system registration generator (ADR 0036) on small sources with
/// Roslyn's own driver: the code it writes, and the errors it reports.
/// </summary>
public class SystemRegistrationGeneratorTests
{
    private static CSharpCompilation Compile(params string[] sources) => GeneratorHarness.Compile(sources);

    private static GeneratorDriver Driver() => GeneratorHarness.Driver(new SystemRegistrationGenerator());

    private static (string? Source, ImmutableArray<Diagnostic> Generator, Diagnostic[] Errors) Run(string source)
    {
        (string[] sources, ImmutableArray<Diagnostic> diagnostics, Diagnostic[] errors) =
            GeneratorHarness.Run(new SystemRegistrationGenerator(), source);
        return (sources.SingleOrDefault(), diagnostics, errors);
    }

    private const string Usings = "using Crucible.Loop;\n";

    [Test]
    public async Task WritesOneAddSystemPerClassSortedByName()
    {
        (string? source, ImmutableArray<Diagnostic> diagnostics, Diagnostic[] errors) = Run(Usings + """
            namespace Game;

            [System(Phases.Update, After = new[] { typeof(Move) }, BeforeNames = new[] { "crucible.probe.Count" })]
            public sealed class Collide : GameSystem { public override void Run(FrameContext context) { } }

            [System(Phases.Update)]
            internal sealed class Move : GameSystem { public override void Run(FrameContext context) { } }

            public static class Outer
            {
                [System("Custom \"phase\"", Before = new[] { typeof(InputSystem) })]
                internal sealed class Nested : GameSystem { public override void Run(FrameContext context) { } }
            }
            """);
        await Assert.That(diagnostics).IsEmpty();
        await Assert.That(errors).IsEmpty();
        string[] calls = [.. source!.Split('\n').Select(line => line.Trim()).Where(line => line.StartsWith("builder.") || line.StartsWith("before:"))];
        await Assert.That(string.Join("\n", calls)).IsEqualTo("""
            builder.AddSystem("Update", new global::Game.Collide(),
            before: new string[] { "crucible.probe.Count" }, after: new string[] { "Game.Move" });
            builder.AddSystem("Update", new global::Game.Move(),
            before: null, after: null);
            builder.AddSystem("Custom \"phase\"", new global::Game.Outer.Nested(),
            before: new string[] { "Crucible.Loop.InputSystem" }, after: null);
            """);
        await Assert.That(source).Contains(
            "[assembly: global::Crucible.Loop.SystemRegistration(typeof(global::Crucible.Generated.SystemRegistration))]");
    }

    [Test]
    public async Task WritesNothingWithoutSystems()
    {
        (string? source, ImmutableArray<Diagnostic> diagnostics, Diagnostic[] errors) =
            Run(Usings + "public sealed class Plain : GameSystem { public override void Run(FrameContext context) { } }");
        await Assert.That(source).IsNull();
        await Assert.That(diagnostics).IsEmpty();
        await Assert.That(errors).IsEmpty();
    }

    // Each misuse and the error it gives. "Run" is the body every system needs.
    [Test]
    [Arguments("CRU0001", "[System(Phases.Update)] public sealed class NotASystem { }")]
    [Arguments("CRU0002", "[System(Phases.Update)] public abstract class Abstract : GameSystem { }")]
    [Arguments("CRU0002", "[System(Phases.Update)] public sealed class Generic<T> : GameSystem { Run }")]
    [Arguments("CRU0003", "[System(Phases.Update)] public sealed class NeedsArgs(int x) : GameSystem { Run }")]
    [Arguments("CRU0003", "[System(Phases.Update)] public sealed class Hidden : GameSystem { private Hidden() { } Run }")]
    [Arguments("CRU0004", "public class Outer { [System(Phases.Update)] private sealed class Inner : GameSystem { Run } }")]
    [Arguments("CRU0004", "[System(Phases.Update)] file sealed class Local : GameSystem { Run }")]
    [Arguments("CRU0005", "[System(Phases.Update)] public sealed class Renamed : GameSystem { public override string Name => \"x\"; Run }")]
    [Arguments("CRU0005", "public class Base : GameSystem { public override string Name => \"x\"; Run } " +
                          "[System(Phases.Update)] public sealed class Derived : Base { }")]
    [Arguments("CRU0006", "[System(\"\")] public sealed class NoPhase : GameSystem { Run }")]
    [Arguments("CRU0006", "[System(Phases.FixedTick)] public sealed class InTheGroup : GameSystem { Run }")]
    [Arguments("CRU0007", "[System(Phases.Update, After = new[] { typeof(string) })] public sealed class NotAType : GameSystem { Run }")]
    [Arguments("CRU0007", "[System(Phases.Update, After = new[] { typeof(GameSystem) })] public sealed class AbstractTarget : GameSystem { Run }")]
    [Arguments("CRU0007", "public sealed class Renamed : GameSystem { public override string Name => \"x\"; Run } " +
                          "[System(Phases.Update, Before = new[] { typeof(Renamed) })] public sealed class Target : GameSystem { Run }")]
    [Arguments("CRU0007", "[System(Phases.Update, AfterNames = new[] { \"\" })] public sealed class EmptyName : GameSystem { Run }")]
    public async Task ReportsMisuse(string id, string source)
    {
        (string? generated, ImmutableArray<Diagnostic> diagnostics, _) =
            Run(Usings + source.Replace("Run }", "public override void Run(FrameContext context) { } }"));
        await Assert.That(diagnostics.Select(diagnostic => diagnostic.Id)).IsEquivalentTo([id]);
        // Rebuilt from a path and span (the cache holds no syntax trees), so
        // it points at the file and line rather than into the tree.
        FileLinePositionSpan where = diagnostics[0].Location.GetLineSpan();
        await Assert.That(where.Path).IsEqualTo("Game.cs");
        await Assert.That(where.StartLinePosition.Line).IsEqualTo(1); // after the using
        // A class with errors gets no AddSystem call, so its own errors are
        // the only ones.
        await Assert.That(generated).IsNull();
    }

    [Test]
    public async Task UnrelatedEditsReuseTheCachedResult()
    {
        // The incremental pipeline: a second run after an edit that touches
        // no [System] class must find every stage's output unchanged.
        CSharpCompilation compilation = Compile(Usings +
            "[System(Phases.Update)] public sealed class Move : GameSystem { public override void Run(FrameContext context) { } }");
        GeneratorDriver driver = Driver().RunGenerators(compilation);
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
