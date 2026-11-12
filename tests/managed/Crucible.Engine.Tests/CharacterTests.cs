using System.Numerics;
using Crucible.Ecs;
using Crucible.Loop;
using Crucible.Physics;
using Crucible.Scenes;

namespace Crucible.Tests;

// Characters from C# (ADR 0091), under JIT and NativeAOT: a world played a
// few ticks by the engine's physics, a character moved and launched.
[NotInParallel("jobs")]
public class CharacterTests
{
    [Before(Class)]
    public static void Register() => EngineComponents.Register();

    [Test]
    public async Task ACharacterFallsWithTheWorldsGravityAndEasesUpStepsByDefault()
    {
        await Assert.That(new CharacterController().GravityScale).IsEqualTo(1f);
        await Assert.That(Crucible.Serialization.Json.Deserialize<CharacterController>("{}").GravityScale).IsEqualTo(1f);
        await Assert.That(new CharacterController().StepSmoothing).IsEqualTo(0.1f);
    }

    [Test]
    public async Task ACharacterLandsWalksAndJumps()
    {
        using World world = new();
        Entity floor = world.CreateEntity(Components.Of<Collider>(), Components.Of<LocalTransform>(),
            Components.Of<WorldTransform>());
        world.Set(floor, new Collider { Size = new Vector3(20, 1, 20) });
        world.Set(floor, LocalTransform.Identity with { Position = new Vector3(0, -0.5f, 0) });
        Entity walker = world.CreateEntity(Components.Of<CharacterController>(), Components.Of<LocalTransform>(),
            Components.Of<WorldTransform>());
        world.Set(walker, new CharacterController());
        world.Set(walker, LocalTransform.Identity with { Position = new Vector3(0, 1, 0) });
        ScheduleBuilder builder = ScheduleBuilder.CreateDefault();
        builder.IncludeGameplaySystems = true;
        ulong now = 1_000_000_000;
        new FrameLoop(builder.Build()) { Clock = () => now += 16_666_667 }.Run(new FrameContext { World = world }, frameLimit: 3);

        CharacterMove move = default;
        for (int i = 0; i < 60; ++i)
        {
            move = Crucible.Physics.Api.MoveCharacter(world.Handle, walker, new Vector3(0.02f, 0, 0), 1 / 60f);
        }
        await Assert.That(move.Grounded).IsTrue();
        await Assert.That(move.Position.Y).IsEqualTo(0f).Within(0.02f);
        await Assert.That(move.Position.X).IsEqualTo(1.2f).Within(0.01f);
        CharacterController.Launch(world, walker, 4);
        move = Crucible.Physics.Api.MoveCharacter(world.Handle, walker, Vector3.Zero, 1 / 60f);
        await Assert.That(move.Grounded).IsFalse();
        await Assert.That(move.FallSpeed).IsGreaterThan(3.5f);
        await Assert.That(move.Position.Y).IsGreaterThan(0.05f);
    }
}
