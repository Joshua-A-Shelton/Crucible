using System.Numerics;
using Crucible.Ecs;
using Crucible.Loop;
using Crucible.Physics2D;
using Crucible.Scenes;

namespace Crucible.Tests;

// 2D characters and joints from C# (ADR 0094), under JIT and NativeAOT: the
// defaults, and a world played a few ticks, then a character walked and
// launched, and a block held by a joint.
[NotInParallel("jobs")]
public class Physics2DCharacterTests
{
    [Before(Class)]
    public static void Register() => EngineComponents.Register();

    [Test]
    public async Task ACharacterStartsAsThe3DOneDoes()
    {
        CharacterController2D controller = new();
        await Assert.That(controller.GravityScale).IsEqualTo(1f);
        await Assert.That(controller.StepSmoothing).IsEqualTo(0.1f);
        await Assert.That(Crucible.Serialization.Json.Deserialize<CharacterController2D>("{}").Radius).IsEqualTo(0.4f);
    }

    [Test]
    public async Task ACharacterWalksAndJumpsAndAJointHolds()
    {
        using World world = new();
        Entity floor = world.CreateEntity(Components.Of<Collider2D>(), Components.Of<Transform2D>(), Components.Of<WorldTransform>());
        world.Set(floor, new Collider2D { Size = new Vector2(20, 1) });
        world.Set(floor, new Transform2D { Position = new Vector2(0, -0.5f) });
        Entity walker = world.CreateEntity(Components.Of<CharacterController2D>(), Components.Of<Transform2D>(),
            Components.Of<WorldTransform>());
        world.Set(walker, new CharacterController2D());
        world.Set(walker, new Transform2D());
        Entity held = world.CreateEntity(Components.Of<Collider2D>(), Components.Of<RigidBody2D>(), Components.Of<Transform2D>(),
            Components.Of<WorldTransform>());
        world.Set(held, new Collider2D());
        world.Set(held, new RigidBody2D());
        world.Set(held, new Transform2D { Position = new Vector2(5, 4) });
        world.Set(held, new Joint2D { Kind = JointKind2D.Fixed });
        ScheduleBuilder builder = ScheduleBuilder.CreateDefault();
        builder.IncludeGameplaySystems = true;
        ulong now = 1_000_000_000;
        new FrameLoop(builder.Build()) { Clock = () => now += 16_666_667 }.Run(new FrameContext { World = world }, frameLimit: 60);
        await Assert.That(world.Read<Transform2D>(held).Position.Y).IsEqualTo(4f).Within(0.02f);

        CharacterMove2D move = default;
        for (int i = 0; i < 30; ++i)
        {
            move = CharacterController2D.Move(world, walker, new Vector2(0.02f, 0), 1 / 60f);
        }
        await Assert.That(move.Grounded).IsTrue();
        await Assert.That(move.Position.X).IsEqualTo(0.6f).Within(0.01f);
        CharacterController2D.Launch(world, walker, 4);
        move = CharacterController2D.Move(world, walker, Vector2.Zero, 1 / 60f);
        await Assert.That(move.Grounded).IsFalse();
        await Assert.That(move.FallSpeed).IsGreaterThan(3.5f);
    }
}
