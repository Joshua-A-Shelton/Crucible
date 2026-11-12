using System.Numerics;
using Crucible.Ecs;
using Crucible.Loop;
using Crucible.Physics2D;
using Crucible.Scenes;
using Crucible.Serialization;

namespace Crucible.Tests;

// 2D physics from C# (ADR 0093), under JIT and NativeAOT: the components'
// defaults, saving (a polygon's points as a fixed array), and a world played
// by the engine's 2D systems, queried.
[NotInParallel("jobs")]
public class Physics2DTests
{
    [Before(Class)]
    public static void Register() => EngineComponents.Register();

    [Test]
    public async Task ComponentsStartUsable()
    {
        await Assert.That(new Transform2D().Scale).IsEqualTo(Vector2.One);
        await Assert.That(Json.Deserialize<Transform2D>("{}").Scale).IsEqualTo(Vector2.One);
        RigidBody2D body = new();
        await Assert.That(body.Kind).IsEqualTo(Crucible.Physics.BodyKind.Dynamic);
        await Assert.That(body.Mask).IsEqualTo(uint.MaxValue);
        await Assert.That(new Collider2D().Size).IsEqualTo(Vector2.One);
    }

    [Test]
    public async Task APolygonsPointsSaveAsAFixedArray()
    {
        Collider2D triangle = new() { Shape = ShapeKind2D.Polygon, PointCount = 3 };
        triangle.Points[0] = new Vector2(-1, 0);
        triangle.Points[1] = new Vector2(1, 0);
        triangle.Points[2] = new Vector2(0, 2);
        string json = Json.SerializeToString(triangle);
        Collider2D read = Json.Deserialize<Collider2D>(json);
        await Assert.That(read.Points[2]).IsEqualTo(new Vector2(0, 2));
        await Assert.That(read.PointCount).IsEqualTo(3);
        // All eight, always; a longer list is cut to them.
        await Assert.That(System.Text.Json.Nodes.JsonNode.Parse(json)!["Points"]!.AsArray().Count).IsEqualTo(8);
        SerializedField points = Collider2D.SerializedFields.Single(field => field.Name == "Points");
        await Assert.That(points.Kind).IsEqualTo(FieldKind.FixedArray);
        await Assert.That(points.FixedLength).IsEqualTo(8);
    }

    [Test]
    public async Task ABlockFallsOntoTheGroundAndRaysFindIt()
    {
        using World world = new();
        Entity Make(Vector2 at, Vector2 size, bool moves)
        {
            Entity entity = world.CreateEntity(Components.Of<Collider2D>(), Components.Of<Transform2D>(),
                Components.Of<WorldTransform>());
            world.Set(entity, new Collider2D { Size = size });
            world.Set(entity, new Transform2D { Position = at });
            if (moves)
            {
                world.Set(entity, new RigidBody2D());
            }
            return entity;
        }
        Make(new Vector2(0, -0.5f), new Vector2(20, 1), moves: false);
        Entity block = Make(new Vector2(0, 3), Vector2.One, moves: true);
        ScheduleBuilder builder = ScheduleBuilder.CreateDefault();
        builder.IncludeGameplaySystems = true;
        ulong now = 1_000_000_000;
        new FrameLoop(builder.Build()) { Clock = () => now += 16_666_667 }.Run(new FrameContext { World = world }, frameLimit: 150);
        await Assert.That(world.Read<Transform2D>(block).Position.Y).IsEqualTo(0.5f).Within(0.02f);
        await Assert.That(RigidBody2D.Pose(world, block).Found).IsTrue();
        RayHit2D hit = Crucible.Physics2D.Physics2D.Raycast(world, new Vector2(0, 10), new Vector2(0, -2), 20);
        await Assert.That(hit.Entity).IsEqualTo(block);
        await Assert.That(hit.Distance).IsEqualTo(9f).Within(0.02f);
    }
}
