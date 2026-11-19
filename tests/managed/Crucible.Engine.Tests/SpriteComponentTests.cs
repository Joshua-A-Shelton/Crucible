using System.Numerics;
using Crucible.Assets;
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
                                {"Name": "Bridge"}, {"Name": "Bridge characters", "YSort": true}]
            }
            """u8);
        await Assert.That(settings.SortingLayer("Bridge characters")).IsEqualTo(3);
        try
        {
            RuntimeSettings.Install(settings);
            await Assert.That(Crucible.Render2D.Api.YSortLayers()).IsEqualTo(0b1010u);
            await Assert.That(RuntimeSettings.Current.SortingLayers.Count).IsEqualTo(4);

            // None listed: "Default" alone, nothing y-sorted.
            RuntimeSettings.Install(RuntimeSettings.FromJson("""{"Name": "Plain"}"""u8));
            await Assert.That(RuntimeSettings.Current.SortingLayers.Single().Name).IsEqualTo("Default");
            await Assert.That(Crucible.Render2D.Api.YSortLayers()).IsEqualTo(0u);
        }
        finally
        {
            RuntimeSettings.Install(new RuntimeSettings());
        }
    }
}
