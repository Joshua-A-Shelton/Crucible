using System.Numerics;
using Crucible.Ecs;
using Crucible.Loop;
using Crucible.Physics;

namespace Crucible.Tests;

// The physics contract from C# (ADR 0075), under JIT and NativeAOT: its
// gameplay systems join a schedule only when asked, the null physics's
// queries answer through the bindings, and its components save.
public class PhysicsContractTests
{
    [Before(Class)]
    public static void Register() => Crucible.Scenes.EngineComponents.Register();

    private static string[] Names(ScheduleBuilder builder) => [.. builder.Build().Systems.Select(system => system.Name)];

    [Test]
    public async Task GameplaySystemsJoinOnlyWhenAsked()
    {
        ScheduleBuilder editor = ScheduleBuilder.CreateDefault();
        await Assert.That(Names(editor)).DoesNotContain("Physics.Step");
        ScheduleBuilder game = ScheduleBuilder.CreateDefault();
        game.IncludeGameplaySystems = true;
        string[] names = Names(game);
        await Assert.That(names).Contains("Physics.Sync");
        await Assert.That(names).Contains("Physics.Step");
        await Assert.That(names).Contains("Physics.WriteBack");
        await Assert.That(names).Contains("Audio.Update");
    }

    [Test]
    public async Task TheNullPhysicsAnswersThroughTheBindings()
    {
        using World world = new();
        RayHit hit = Crucible.Physics.Api.Raycast(world.Handle, Vector3.Zero, Vector3.UnitZ, 100, QueryFilter.Default);
        await Assert.That(hit.Hit).IsFalse();
    }

    [Test]
    public async Task ABodyStartsOnTheDefaultLayerCollidingWithEverything()
    {
        RigidBody body = new();
        await Assert.That(body.Kind).IsEqualTo(BodyKind.Dynamic);
        await Assert.That(body.Layers).IsEqualTo(1u);
        await Assert.That(body.Mask).IsEqualTo(uint.MaxValue);
        // Added in the editor (an empty object read), it gets the same.
        RigidBody added = Crucible.Serialization.Json.Deserialize<RigidBody>("{}");
        await Assert.That(added.Mask).IsEqualTo(uint.MaxValue);
        await Assert.That(new CharacterController().Layers).IsEqualTo(1u);
    }

    [Test]
    public async Task LayerMasksAreMarkedForTheInspector()
    {
        Crucible.Serialization.SerializedField[] fields = [.. RigidBody.SerializedFields];
        await Assert.That(fields.Single(field => field.Name == "Layers").IsLayers).IsTrue();
        await Assert.That(fields.Single(field => field.Name == "Mask").IsLayers).IsTrue();
        await Assert.That(fields.Single(field => field.Name == "Mass").IsLayers).IsFalse();
    }

    [Test]
    public async Task ItsComponentsSave()
    {
        await Assert.That(SavedComponents.Find("Crucible.Physics.RigidBody")).IsNotNull();
        await Assert.That(SavedComponents.Find("Crucible.Physics.Collider")).IsNotNull();
        await Assert.That(SavedComponents.Find("Crucible.Physics.Joint")).IsNotNull();
        await Assert.That(SavedComponents.Find("Crucible.Physics.CharacterController")).IsNotNull();
    }
}
