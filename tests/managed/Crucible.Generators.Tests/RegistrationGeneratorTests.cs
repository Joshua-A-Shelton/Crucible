using System.Collections.Immutable;
using Crucible.Generators;
using Microsoft.CodeAnalysis;

namespace Crucible.Tests.Generators;

/// <summary>
/// The component and service registration generators (ADR 0041): what they
/// emit, and the errors they report.
/// </summary>
public class RegistrationGeneratorTests
{
    private const string Usings = "using Crucible.Ecs;\nusing Crucible.Loop;\nusing Crucible.Serialization;\n";

    [Test]
    public async Task ComponentsRegisterByStorageAndSaveWhenSerialized()
    {
        // Compiling the result needs the [Serialized] generator too (for
        // SavedComponents' constraint); Crucible.Engine.Tests runs both.
        (string[] sources, ImmutableArray<Diagnostic> diagnostics, _) = GeneratorHarness.Run(
            new ComponentRegistrationGenerator(), Usings + """
            namespace Game;
            [Component, Serialized] public partial struct Health { public int Value; }
            [Component(Sparse = true)] public struct Stunned { public float Seconds; }
            [Component, Serialized] public partial struct Enemy { }
            [Component] public struct Cached { public System.IntPtr Pointer; }
            [Component, Serialized] public partial class Inventory { public System.Collections.Generic.List<string> Items = new(); }
            [Component] public struct Named { public string Value; }
            """);
        await Assert.That(diagnostics).IsEmpty();
        string[] calls = [.. sources.Single().Split('\n').Select(line => line.Trim()).Where(line => line.StartsWith("global::"))];
        await Assert.That(string.Join("\n", calls)).IsEqualTo("""
            global::Crucible.Ecs.Components.Register<global::Game.Cached>();
            global::Crucible.Ecs.Components.RegisterTag<global::Game.Enemy>();
            global::Crucible.Ecs.SavedComponents.AddTag<global::Game.Enemy>();
            global::Crucible.Ecs.Components.Register<global::Game.Health>();
            global::Crucible.Ecs.SavedComponents.Add<global::Game.Health>();
            global::Crucible.Ecs.Components.RegisterManaged<global::Game.Inventory>();
            global::Crucible.Ecs.SavedComponents.AddManaged<global::Game.Inventory>();
            global::Crucible.Ecs.Components.RegisterManaged<global::Game.Named>();
            global::Crucible.Ecs.Components.RegisterSparse<global::Game.Stunned>();
            """);
    }

    [Test]
    public async Task ServicesAreCreatedAndSavedWhenSerialized()
    {
        (string[] sources, ImmutableArray<Diagnostic> diagnostics, _) = GeneratorHarness.Run(
            new ServiceRegistrationGenerator(), Usings + """
            namespace Game;
            [Service, Serialized] public sealed partial class QuestLog { public int Done; }
            [Service] internal sealed class Dice { }
            """);
        await Assert.That(diagnostics).IsEmpty();
        string source = sources.Single();
        await Assert.That(source).Contains("services.Add(new global::Game.Dice());");
        await Assert.That(source).Contains("services.Add(new global::Game.QuestLog());\n            services.AddSaved<global::Game.QuestLog>();");
        await Assert.That(source).Contains("[assembly: global::Crucible.Loop.ServiceRegistration(");
    }

    [Test]
    [Arguments("[Component] public struct Box<T> { public T Value; }")]
    [Arguments("[Component(Sparse = true)] public class Bag { }")]
    [Arguments("[Component(Sparse = true)] public struct Flag { }")]
    [Arguments("[Component] public abstract class Shape { }")]
    [Arguments("public class Outer { [Component] private struct Hidden { public int X; } }")]
    public async Task ComponentMisuseIsReported(string source)
    {
        (string[] sources, ImmutableArray<Diagnostic> diagnostics, _) =
            GeneratorHarness.Run(new ComponentRegistrationGenerator(), Usings + source);
        await Assert.That(string.Join(" ", diagnostics.Select(diagnostic => diagnostic.Id))).IsEqualTo("CRU0201");
        await Assert.That(sources).IsEmpty();
    }

    [Test]
    [Arguments("[Service] public abstract class Rules { }")]
    [Arguments("[Service] public class Cache<T> { }")]
    [Arguments("[Service] public class NeedsArgs { public NeedsArgs(int x) { } }")]
    [Arguments("public class Outer { [Service] private class Hidden { } }")]
    public async Task ServiceMisuseIsReported(string source)
    {
        (string[] sources, ImmutableArray<Diagnostic> diagnostics, _) =
            GeneratorHarness.Run(new ServiceRegistrationGenerator(), Usings + source);
        await Assert.That(string.Join(" ", diagnostics.Select(diagnostic => diagnostic.Id))).IsEqualTo("CRU0202");
        await Assert.That(sources).IsEmpty();
    }
}
