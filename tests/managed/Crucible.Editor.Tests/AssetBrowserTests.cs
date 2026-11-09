using System.Numerics;
using Crucible.Render;

namespace Crucible.Editor.Tests;

// Where a dragged model lands (ADR 0071): on the surface the pick saw, else
// the ground, else ten meters ahead. The browser itself runs in
// launcher.jit.assets.
public class AssetBrowserTests
{
    private static readonly Vector2 _size = new(800, 600);

    [Test]
    public async Task PlacementLandsOnThePickedSurfaceTheGroundOrAhead()
    {
        // Looking down at 30 degrees from 10 m up.
        EditorCamera camera = new() { Pivot = Vector3.Zero, Distance = 20, Yaw = 0, Pitch = MathF.PI / 6 };
        Vector2 middle = _size / 2;
        // A surface 12 m along the view axis: near / depth is the distance.
        PickResult hit = new() { Hit = true, Depth = camera.NearPlane / 12, Frame = 1 };
        Vector3 onSurface = Shell.PlacementPoint(camera, _size, middle, hit);
        await Assert.That(Vector3.Distance(onSurface, camera.Position + camera.Forward * 12)).IsLessThan(1e-3f);

        // Nothing there: where the ray meets the ground, which here is the pivot.
        Vector3 onGround = Shell.PlacementPoint(camera, _size, middle, default);
        await Assert.That(Vector3.Distance(onGround, Vector3.Zero)).IsLessThan(1e-3f);

        // Looking up, with nothing there: ten meters ahead.
        EditorCamera up = new() { Pivot = new Vector3(0, 5, 0), Distance = 5, Pitch = -0.5f };
        Vector3 ahead = Shell.PlacementPoint(up, _size, middle, default);
        await Assert.That(Vector3.Distance(ahead, up.Position + up.Forward * 10)).IsLessThan(1e-3f);
    }
}
