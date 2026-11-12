using System.Numerics;
using Crucible.Ecs;
using Crucible.Loop;
using Crucible.Physics;
using Crucible.Scenes;

namespace Crucible.Tests;

// Joints from C# (ADR 0092), under JIT and NativeAOT: every kind saves by
// name, and a block held to the world by a fixed joint doesn't fall while a
// free one does.
[NotInParallel("jobs")]
public class JointTests
{
    [Before(Class)]
    public static void Register() => EngineComponents.Register();

    [Test]
    public async Task EveryKindSavesByName()
    {
        foreach (JointKind kind in Enum.GetValues<JointKind>())
        {
            string json = Crucible.Serialization.Json.SerializeToString(new Joint { Kind = kind });
            await Assert.That(json).Contains($"\"{kind}\"");
            await Assert.That(Crucible.Serialization.Json.Deserialize<Joint>(json).Kind).IsEqualTo(kind);
        }
    }

    [Test]
    public async Task AFixedJointToTheWorldHoldsABlock()
    {
        using World world = new();
        Entity Block(float x)
        {
            Entity entity = world.CreateEntity(Components.Of<Collider>(), Components.Of<RigidBody>(),
                Components.Of<LocalTransform>(), Components.Of<WorldTransform>());
            world.Set(entity, new Collider());
            world.Set(entity, new RigidBody());
            world.Set(entity, LocalTransform.Identity with { Position = new Vector3(x, 5, 0) });
            return entity;
        }
        Entity held = Block(0), free = Block(3);
        world.Add(held, Components.Of<Joint>());
        world.Set(held, new Joint { Kind = JointKind.Fixed });
        ScheduleBuilder builder = ScheduleBuilder.CreateDefault();
        builder.IncludeGameplaySystems = true;
        ulong now = 1_000_000_000;
        new FrameLoop(builder.Build()) { Clock = () => now += 16_666_667 }.Run(new FrameContext { World = world }, frameLimit: 60);
        await Assert.That(RigidBody.Pose(world, held).Position.Y).IsEqualTo(5f).Within(0.02f);
        await Assert.That(RigidBody.Pose(world, free).Position.Y).IsLessThan(4f);
    }
}
