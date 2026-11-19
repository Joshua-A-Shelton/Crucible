using System.Numerics;
using Crucible.Assets;
using Crucible.Ecs;
using Crucible.Render2D;
using Crucible.Serialization;

namespace Crucible.Tests;

// The sprite component's C# half and the runtime settings (ADR 0104), under
// JIT and NativeAOT.
[NotInParallel("runtime-settings")]
public class SpriteComponentTests
{
    [Test]
    public async Task ANewSpriteHasACenteredPivotAndAWhiteTint()
    {
        Sprite sprite = new();
        await Assert.That(sprite.Pivot).IsEqualTo(new Vector2(0.5f, 0.5f));
        await Assert.That(sprite.Tint).IsEqualTo(Vector4.One);
        await Assert.That(sprite.SortingLayer).IsEqualTo(0u);
    }

    [Test]
    public async Task ANewPixelCameraIs320By180At16PixelsAMeterFittedSharply()
    {
        PixelCamera camera = new();
        await Assert.That((camera.Width, camera.Height, camera.PixelsPerUnit, camera.Fit))
            .IsEqualTo((320u, 180u, 16f, PixelFit.Sharp));
    }

    // Frame animation (ADR 0107).
    [Test]
    public async Task ANewSpriteAnimatorPlaysAtSpeedOneAndPicksSpriteClipsAndStateMachines()
    {
        await Assert.That(new SpriteAnimator().Speed).IsEqualTo(1f);
        SerializedField? clip = SpriteAnimator.SerializedFields.FirstOrDefault(field => field.Name == "Clip");
        await Assert.That(clip?.AssetKinds.SequenceEqual([".crspriteclip"])).IsTrue();
        SerializedField? machine = SpriteAnimator.SerializedFields.FirstOrDefault(field => field.Name == "StateMachine");
        await Assert.That(machine?.AssetKinds.SequenceEqual([".crstatemachine"])).IsTrue();
    }

    // Tilemaps (ADR 0108).
    [Test]
    public async Task ANewTilemapIsWhiteAndPicksImportedMaps()
    {
        await Assert.That(new Tilemap().Tint).IsEqualTo(Vector4.One);
        SerializedField? map = Tilemap.SerializedFields.FirstOrDefault(field => field.Name == "Map");
        await Assert.That(map?.AssetKinds.SequenceEqual([".crtmap"])).IsTrue();
    }

    [Test]
    public async Task The2DComponentsAreSavedInScenes()
    {
        Crucible.Scenes.EngineComponents.Register();
        foreach (string name in new[]
                 {
                     "Crucible.Render2D.Sprite", "Crucible.Render2D.PixelCamera", "Crucible.Render2D.SpriteAnimator",
                     "Crucible.Render2D.Tilemap",
                 })
        {
            await Assert.That(SavedComponents.Find(name)).IsNotNull().Because(name);
        }
    }

    [Test]
    public async Task ItsSortingLayerIsPickedByName()
    {
        SerializedField? layer = Sprite.SerializedFields.FirstOrDefault(field => field.Name == "SortingLayer");
        await Assert.That(layer?.IsSortingLayer).IsTrue();
        SerializedField? texture = Sprite.SerializedFields.FirstOrDefault(field => field.Name == "Texture");
        await Assert.That(texture?.AssetKinds.SequenceEqual([".crtexture"])).IsTrue();
    }

    [Test]
    public async Task SettingsComeFromAProjectFileAndTellTheRendererWhichLayersYSort()
    {
        RuntimeSettings settings = RuntimeSettings.FromJson("""
            {
              "Name": "Bridges",
              "PhysicsLayers": ["Default"],
              "Build": {"Scenes": []},
              "SortingLayers": [{"Name": "Ground"}, {"Name": "Characters", "YSort": true},
                                {"Name": "Bridge"}, {"Name": "Bridge characters", "YSort": true},
                                {"Name": "Sky", "Behind3D": true}]
            }
            """u8);
        await Assert.That(settings.SortingLayer("Bridge characters")).IsEqualTo(3);
        try
        {
            RuntimeSettings.Install(settings);
            await Assert.That(Crucible.Render2D.Api.YSortLayers()).IsEqualTo(0b1010u);
            await Assert.That(Crucible.Render2D.Api.Behind3DLayers()).IsEqualTo(0b10000u);
            await Assert.That(RuntimeSettings.Current.SortingLayers.Count).IsEqualTo(5);

            // None listed: "Default" alone, nothing y-sorted.
            RuntimeSettings.Install(RuntimeSettings.FromJson("""{"Name": "Plain"}"""u8));
            await Assert.That(RuntimeSettings.Current.SortingLayers.Single().Name).IsEqualTo("Default");
            await Assert.That(Crucible.Render2D.Api.YSortLayers()).IsEqualTo(0u);
            await Assert.That(Crucible.Render2D.Api.Behind3DLayers()).IsEqualTo(0u);
        }
        finally
        {
            RuntimeSettings.Install(new RuntimeSettings());
        }
    }
}
