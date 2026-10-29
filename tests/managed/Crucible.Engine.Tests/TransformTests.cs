using System.Numerics;
using System.Text;
using Crucible.Assets;
using Crucible.Ecs;
using Crucible.Render;
using Crucible.Scenes;

namespace Crucible.Tests;

// Transforms and cameras (ADR 0050) from C#: the C++-declared components
// work like C# ones, scenes save them, and world matrices update on demand.

public class TransformTests
{
    private static readonly AssetId _sceneId = AssetId.Parse("0a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d");

    [Test]
    public async Task UpdatingPlacesChildrenUnderTheirParents()
    {
        using World world = new();
        Entity parent = world.CreateEntity(Components.Of<LocalTransform>(), Components.Of<WorldTransform>());
        world.Set(parent, LocalTransform.Identity with { Position = new Vector3(0, 10, 0), Scale = new Vector3(2) });
        Entity child = world.CreateEntity(Components.Of<LocalTransform>(), Components.Of<WorldTransform>());
        world.Set(child, LocalTransform.Identity with { Position = new Vector3(1, 0, 0) });
        world.SetParent(child, parent);

        world.UpdateWorldTransforms();

        // System.Numerics uses row vectors too: the translation is row 4.
        Matrix4x4 placed = world.Read<WorldTransform>(child).Matrix;
        await Assert.That(placed.Translation).IsEqualTo(new Vector3(2, 10, 0));
        await Assert.That(placed.M11).IsEqualTo(2f);
    }

    [Test]
    public async Task ScenesSaveTransformsAndCamerasButNotWorldMatrices()
    {
        using World world = new();
        Entity camera = world.CreateEntity(Components.Of<LocalTransform>(), Components.Of<WorldTransform>(),
            Components.Of<Camera>());
        world.Set(camera, LocalTransform.Identity with { Position = new Vector3(0, 2, -5) });
        world.Set(camera, new Camera
        {
            Projection = CameraProjection.Orthographic,
            FieldOfViewY = 1,
            OrthographicHeight = 12,
            NearPlane = 0.5f,
            FarPlane = 50,
            Priority = 3,
        });
        world.UpdateWorldTransforms();

        byte[] json = Scene.Save(world, _sceneId);
        string text = Encoding.UTF8.GetString(json);
        await Assert.That(text).Contains("Crucible.Render.Camera");
        await Assert.That(text).DoesNotContain("WorldTransform");

        using World loaded = new();
        SceneLoad load = Scene.Load(loaded, json);
        Entity root = load.Roots.Single();
        await Assert.That(loaded.Read<LocalTransform>(root).Position).IsEqualTo(new Vector3(0, 2, -5));
        Camera read = loaded.Read<Camera>(root);
        await Assert.That(read.Projection).IsEqualTo(CameraProjection.Orthographic);
        await Assert.That(read.OrthographicHeight).IsEqualTo(12f);
        await Assert.That(read.Priority).IsEqualTo(3);
    }
}
