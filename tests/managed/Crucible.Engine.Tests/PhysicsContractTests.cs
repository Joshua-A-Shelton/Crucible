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
        RayHit hit = Crucible.Physics.Api.Raycast(world.Handle, Vector3.Zero, Vector3.UnitZ, 100, uint.MaxValue);
        await Assert.That(hit.Hit).IsFalse();
        Crucible.Physics.Api.SetLayersCollide(4, 5, false);
        await Assert.That(Crucible.Physics.Api.LayersCollide(5, 4)).IsFalse();
        Crucible.Physics.Api.SetLayersCollide(4, 5, true);
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
