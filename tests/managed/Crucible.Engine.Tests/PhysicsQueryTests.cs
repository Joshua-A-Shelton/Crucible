using System.Numerics;
using Crucible.Ecs;
using Crucible.Loop;
using Crucible.Physics;
using Crucible.Scenes;

namespace Crucible.Tests;

// Physics queries from C# (ADR 0090), under JIT and NativeAOT: a world
// played a few ticks by the engine's own physics systems, then asked. One at
// a time: physics is main-thread only, and steps on the job system.
[NotInParallel("jobs")]
public class PhysicsQueryTests
{
    [Before(Class)]
    public static void Register() => EngineComponents.Register();

    private static Entity Block(World world, Vector3 at, bool trigger = false)
    {
        Entity entity = world.CreateEntity(Components.Of<Collider>(), Components.Of<LocalTransform>(),
            Components.Of<WorldTransform>());
        world.Set(entity, new Collider { Size = Vector3.One, IsTrigger = trigger });
        world.Set(entity, LocalTransform.Identity with { Position = at });
        return entity;
    }

    // Three frames a tick apart, so the physics syncs and steps.
    private static void Play(World world)
    {
        ScheduleBuilder builder = ScheduleBuilder.CreateDefault();
        builder.IncludeGameplaySystems = true;
        ulong now = 1_000_000_000;
        FrameLoop loop = new(builder.Build()) { Clock = () => now += 16_666_667 };
        loop.Run(new FrameContext { World = world }, frameLimit: 3);
    }

    [Test]
    public async Task TheDefaultFilterHitsEveryLayer()
    {
        await Assert.That(QueryFilter.Default.Layers).IsEqualTo(uint.MaxValue);
        await Assert.That(QueryFilter.Default.Ignore.IsNull).IsTrue();
        await Assert.That(QueryFilter.Default.Triggers).IsFalse();
    }

    [Test]
    public async Task RaysCastsAndOverlapsFindTheBlock()
    {
        using World world = new();
        Entity block = Block(world, new Vector3(0, 2, 0));
        Play(world);
        // Not normalized: the call does it.
        RayHit ray = Crucible.Physics.Physics.Raycast(world, new Vector3(0, 10, 0), new Vector3(0, -5, 0), 100);
        await Assert.That(ray.Hit).IsTrue();
        await Assert.That(ray.Entity).IsEqualTo(block);
        await Assert.That(ray.Distance).IsEqualTo(7.5f).Within(0.01f);
        RayHit cast = Crucible.Physics.Physics.ShapeCast(world, new QueryShape { Kind = ShapeKind.Sphere, Size = new Vector3(0.5f, 0, 0) },
            new Vector3(0, 10, 0), Quaternion.Identity, -Vector3.UnitY, 100);
        await Assert.That(cast.Distance).IsEqualTo(7.0f).Within(0.02f);
        Span<Entity> found = stackalloc Entity[4];
        int count = Crucible.Physics.Physics.Overlap(world, new QueryShape { Kind = ShapeKind.Box, Size = new Vector3(4) },
            new Vector3(0, 2, 0), Quaternion.Identity, found);
        await Assert.That(count).IsEqualTo(1);
        await Assert.That(Crucible.Physics.Physics.Raycast(world, new Vector3(0, 10, 0), -Vector3.UnitY, 100,
            QueryFilter.Default with { Ignore = block }).Hit).IsFalse();
    }

    [Test]
    public async Task TriggersAreHitOnlyWhenAsked()
    {
        using World world = new();
        Entity zone = Block(world, Vector3.Zero, trigger: true);
        Play(world);
        await Assert.That(Crucible.Physics.Physics.Raycast(world, new Vector3(0, 10, 0), -Vector3.UnitY, 100).Hit).IsFalse();
        RayHit hit = Crucible.Physics.Physics.Raycast(world, new Vector3(0, 10, 0), -Vector3.UnitY, 100,
            QueryFilter.Default with { Triggers = true });
        await Assert.That(hit.Entity).IsEqualTo(zone);
    }
}
