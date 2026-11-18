using Crucible.Assets;
using Crucible.Ecs;
using Crucible.Scenes;

namespace Crucible.Tests;

// Loading a scene while frames go on (ADR 0099), under JIT and NativeAOT.
// Without job workers every load is done when it starts, so a request
// finishes the first time SceneLoadSystem moves it on; the waiting itself is
// checked by AsyncLoadTests (the counts) and launcher.jit.scene-loading.
[NotInParallel("asset-source")]
public class SceneLoadAsyncTests
{
    private static readonly AssetId _sceneId = AssetId.Parse("5e1f0c2a-9b3d-4e6f-8a7b-0c1d2e3f4a77");

    [Before(Class)]
    public static void Register() => new Crucible.Generated.ComponentRegistration().Register();

    // Three entities, two at the top.
    private static byte[] SampleScene()
    {
        using World world = new();
        Entity parent = world.CreateEntity(Components.Of<SceneHealth>());
        world.Set(parent, new SceneHealth { Value = 10 });
        Entity child = world.CreateEntity(Components.Of<SceneHealth>());
        world.Set(child, new SceneHealth { Value = 3 });
        world.SetParent(child, parent);
        world.CreateEntity(Components.Of<SceneMarker>());
        return Scene.Save(world, _sceneId);
    }

    [Test]
    public async Task TheScenesEntitiesAppearTogetherOnceMovedOn()
    {
        byte[] scene = SampleScene();
        AssetSource.Install(id => id == _sceneId ? scene : null);
        try
        {
            using World world = new();
            SceneLoadRequest request = Scene.LoadAsync(world, _sceneId);
            // Nothing in the world yet, and no result to read.
            await Assert.That(world.EntityCount).IsEqualTo(0);
            await Assert.That(request.IsDone).IsFalse();
            await Assert.That(request.Progress).IsEqualTo(0f);
            await Assert.That(() => request.Result).Throws<InvalidOperationException>();

            SceneLoadSystem.Advance(world);
            await Assert.That(request.IsDone).IsTrue();
            await Assert.That(request.Progress).IsEqualTo(1f);
            await Assert.That(world.EntityCount).IsEqualTo(3);
            await Assert.That(request.Result.Id).IsEqualTo(_sceneId);
            await Assert.That(request.Result.Roots.Count).IsEqualTo(2);
            // Roots come in file order, which follows the entities' random scene IDs.
            Entity parent = request.Result.Roots.Single(root => world.Has(root, Components.Of<SceneHealth>()));
            await Assert.That(world.Read<SceneHealth>(parent).Value).IsEqualTo(10);

            // Done ones are forgotten: moving on again adds nothing.
            SceneLoadSystem.Advance(world);
            await Assert.That(world.EntityCount).IsEqualTo(3);
        }
        finally
        {
            AssetSource.Install(null);
        }
    }

    [Test]
    public async Task ASceneTheSourceDoesntHaveIsNotFound()
    {
        AssetSource.Install(_ => null);
        try
        {
            using World world = new();
            CrucibleException? thrown = null;
            try
            {
                Scene.LoadAsync(world, _sceneId);
            }
            catch (CrucibleException exception)
            {
                thrown = exception;
            }
            await Assert.That(thrown?.Status).IsEqualTo(Status.NotFound);
        }
        finally
        {
            AssetSource.Install(null);
        }
    }
}
