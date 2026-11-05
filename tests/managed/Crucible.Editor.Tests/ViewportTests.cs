using System.Numerics;

namespace Crucible.Editor.Tests;

// The viewport's camera and handles (ADR 0067): pure math, no GPU.
public class ViewportTests
{
    private static readonly Vector2 _size = new(800, 600);

    private static bool Near(Vector3 a, Vector3 b, float tolerance = 1e-3f) => Vector3.Distance(a, b) <= tolerance;

    [Test]
    public async Task TheCameraLooksAtItsPivotAndProjectsLikeTheRenderer()
    {
        EditorCamera camera = new() { Pivot = new Vector3(1, 0, 2), Distance = 10, Yaw = 0.3f, Pitch = 0.4f };
        // The pivot is in the middle of the view; positive pitch looks down.
        Vector2 middle = camera.Project(camera.Pivot, _size)!.Value;
        await Assert.That(Vector2.Distance(middle, _size / 2)).IsLessThan(0.01f);
        await Assert.That(camera.Forward.Y).IsLessThan(0f);
        await Assert.That(Vector3.Distance(camera.Position, camera.Pivot)).IsEqualTo(10f).Within(1e-4f);
        // A point and the ray through where it's drawn agree.
        Vector3 point = new(3, 1, 4);
        Vector2 drawn = camera.Project(point, _size)!.Value;
        (Vector3 origin, Vector3 direction) = camera.Ray(drawn, _size);
        Vector3 toPoint = Vector3.Normalize(point - origin);
        await Assert.That(Vector3.Dot(direction, toPoint)).IsGreaterThan(0.99999f);
        // Behind the camera: nowhere.
        await Assert.That(camera.Project(camera.Position - camera.Forward, _size)).IsNull();
        // The projection is the renderer's: reverse-Z (the near plane at depth 1), no far plane.
        Vector4 near = Vector4.Transform(new Vector4(0, 0, camera.NearPlane, 1), camera.Projection(1));
        await Assert.That(near.Z / near.W).IsEqualTo(1f).Within(1e-5f);
    }

    [Test]
    public async Task OrbitingKeepsThePivotAndLookingKeepsThePosition()
    {
        EditorCamera camera = new() { Pivot = new Vector3(0, 1, 0), Distance = 8 };
        camera.Orbit(new Vector2(100, -40));
        await Assert.That(Near(camera.Pivot, new Vector3(0, 1, 0))).IsTrue();
        Vector3 standing = camera.Position;
        camera.Look(new Vector2(-60, 30));
        await Assert.That(Near(camera.Position, standing)).IsTrue();
        // Pitch stops short of straight down.
        camera.Orbit(new Vector2(0, 10000));
        await Assert.That(camera.Pitch).IsLessThan(MathF.PI / 2);
        // Dollying moves closer; flying moves pivot and camera together.
        camera.Dolly(2);
        await Assert.That(camera.Distance).IsEqualTo(8 * 0.85f * 0.85f).Within(1e-4f);
        Vector3 before = camera.Position;
        camera.Fly(new Vector3(0, 0, 1), 0.5f);
        await Assert.That(Near(camera.Position, before + camera.Forward * camera.FlySpeed * 0.5f)).IsTrue();
    }

    [Test]
    public async Task PanningKeepsThePivotUnderThePointer()
    {
        EditorCamera camera = new() { Distance = 10 };
        Vector2 before = camera.Project(Vector3.Zero, _size)!.Value;
        camera.Pan(new Vector2(50, 20), _size.Y);
        Vector2 after = camera.Project(Vector3.Zero, _size)!.Value;
        // The world point that was in the middle moved with the pointer.
        await Assert.That(Vector2.Distance(after - before, new Vector2(50, 20))).IsLessThan(0.5f);
    }

    [Test]
    public async Task FramingFitsASphere()
    {
        EditorCamera camera = new();
        camera.Frame(new Vector3(5, 0, 5), 2);
        await Assert.That(Near(camera.Pivot, new Vector3(5, 0, 5))).IsTrue();
        // The sphere's edge is inside the view, a little in from its top.
        Vector2 top = camera.Project(camera.Pivot + camera.Up * 2, _size)!.Value;
        await Assert.That(top.Y).IsGreaterThan(0f);
        await Assert.That(top.Y).IsLessThan(_size.Y * 0.15f);
    }

    // A camera looking straight down -Z at the origin from +Z... looking along +Z from z = -10.
    private static EditorCamera Front() => new() { Pivot = Vector3.Zero, Distance = 10, Yaw = 0, Pitch = 0 };

    [Test]
    public async Task MoveHandlesAreHitAndDraggedAlongTheirAxis()
    {
        EditorCamera camera = Front();
        TransformHandles handles = new() { Mode = HandleMode.Move };
        Matrix4x4 world = Matrix4x4.Identity;
        Vector2 origin = camera.Project(Vector3.Zero, _size)!.Value;
        float length = camera.WorldSize(Vector3.Zero, TransformHandles.SizePixels, _size.Y);
        Vector2 xTip = camera.Project(Vector3.UnitX * length, _size)!.Value;
        Vector2 onX = origin + (xTip - origin) * 0.8f;
        await Assert.That(handles.HitTest(camera, _size, world, onX)).IsEqualTo(HandlePart.AxisX);
        Vector2 onY = camera.Project(Vector3.UnitY * length * 0.8f, _size)!.Value;
        await Assert.That(handles.HitTest(camera, _size, world, onY)).IsEqualTo(HandlePart.AxisY);
        // The square between X and Y.
        Vector2 between = camera.Project(new Vector3(0.35f, 0.35f, 0) * length, _size)!.Value;
        await Assert.That(handles.HitTest(camera, _size, world, between)).IsEqualTo(HandlePart.PlaneXY);
        await Assert.That(handles.HitTest(camera, _size, world, origin + new Vector2(-200, 150))).IsEqualTo(HandlePart.None);

        // Dragged 40 pixels right (and some off the axis): moves by about what
        // 40 pixels are at that depth (the nearest point on the axis to a ray
        // a little off it), along X only.
        handles.BeginDrag(HandlePart.AxisX, camera, _size, world, onX);
        Matrix4x4 moved = handles.Drag(camera, _size, onX + new Vector2(40, 25), snap: false);
        float metersPer40 = camera.WorldSize(Vector3.Zero, 40, _size.Y);
        await Assert.That(moved.Translation.X).IsEqualTo(metersPer40).Within(0.01f);
        await Assert.That(moved.Translation.Y).IsEqualTo(0f).Within(1e-5f);
        // Snapped: to half meters.
        Matrix4x4 snapped = handles.Drag(camera, _size, onX + new Vector2(130, 0), snap: true);
        await Assert.That(snapped.Translation.X % 0.5f).IsEqualTo(0f).Within(1e-4f);
        handles.EndDrag();

        // The plane between X and Y moves in both.
        handles.BeginDrag(HandlePart.PlaneXY, camera, _size, world, between);
        Matrix4x4 slid = handles.Drag(camera, _size, between + new Vector2(30, -30), snap: false);
        await Assert.That(slid.Translation.X).IsGreaterThan(0.1f);
        await Assert.That(slid.Translation.Y).IsGreaterThan(0.1f);
        await Assert.That(slid.Translation.Z).IsEqualTo(0f).Within(1e-4f);
    }

    [Test]
    public async Task LocalSpaceFollowsTheEntitysRotation()
    {
        EditorCamera camera = Front();
        TransformHandles handles = new() { Mode = HandleMode.Move, Space = HandleSpace.Local };
        // Turned a quarter around Z: its X axis points up.
        Matrix4x4 world = Matrix4x4.CreateRotationZ(MathF.PI / 2);
        float length = camera.WorldSize(Vector3.Zero, TransformHandles.SizePixels, _size.Y);
        Vector2 up = camera.Project(Vector3.UnitY * length * 0.8f, _size)!.Value;
        await Assert.That(handles.HitTest(camera, _size, world, up)).IsEqualTo(HandlePart.AxisX);
        handles.Space = HandleSpace.World;
        await Assert.That(handles.HitTest(camera, _size, world, up)).IsEqualTo(HandlePart.AxisY);
    }

    [Test]
    public async Task RingsTurnAroundTheirAxisAndSnapTo15Degrees()
    {
        EditorCamera camera = Front();
        TransformHandles handles = new() { Mode = HandleMode.Rotate };
        Matrix4x4 world = Matrix4x4.Identity;
        float length = camera.WorldSize(Vector3.Zero, TransformHandles.SizePixels, _size.Y);
        // Z's ring faces the camera: a circle on screen (aimed at between the
        // edge-on X and Y rings, which cross it at its axes' ends).
        Vector2 right = camera.Project(Vector3.Normalize(new Vector3(1, -1, 0)) * length, _size)!.Value;
        HandlePart part = handles.HitTest(camera, _size, world, right);
        await Assert.That(part).IsEqualTo(HandlePart.RingZ);
        Vector2 top = camera.Project(Vector3.Normalize(new Vector3(1, 1, 0)) * length, _size)!.Value;
        handles.BeginDrag(part, camera, _size, world, right);
        Matrix4x4 turned = handles.Drag(camera, _size, top, snap: false);
        // From 45° below X to 45° above it: a quarter turn around Z, so X now points up.
        Vector3 x = Vector3.TransformNormal(Vector3.UnitX, turned);
        await Assert.That(Near(x, Vector3.UnitY, 1e-2f)).IsTrue();
        Matrix4x4 snapped = handles.Drag(camera, _size, Vector2.Lerp(right, top, 0.27f), snap: true);
        Vector3 snappedX = Vector3.TransformNormal(Vector3.UnitX, snapped);
        float degrees = MathF.Atan2(snappedX.Y, snappedX.X) * 180 / MathF.PI;
        await Assert.That(MathF.Abs(degrees / 15 - MathF.Round(degrees / 15))).IsLessThan(1e-3f);
    }

    [Test]
    public async Task ScaleHandlesStretchOneAxisOrAll()
    {
        EditorCamera camera = Front();
        TransformHandles handles = new() { Mode = HandleMode.Scale };
        Matrix4x4 world = Matrix4x4.Identity;
        Vector2 origin = camera.Project(Vector3.Zero, _size)!.Value;
        await Assert.That(handles.HitTest(camera, _size, world, origin)).IsEqualTo(HandlePart.Uniform);
        float length = camera.WorldSize(Vector3.Zero, TransformHandles.SizePixels, _size.Y);
        Vector2 xTip = camera.Project(Vector3.UnitX * length, _size)!.Value;
        Vector2 onX = origin + (xTip - origin) * 0.8f;
        handles.BeginDrag(HandlePart.AxisX, camera, _size, world, onX);
        // The handle's whole length further out doubles X.
        Matrix4x4 stretched = handles.Drag(camera, _size, onX + (xTip - origin), snap: false);
        Matrix4x4.Decompose(stretched, out Vector3 scale, out _, out _);
        await Assert.That(scale.X).IsEqualTo(2f).Within(0.02f);
        await Assert.That(scale.Y).IsEqualTo(1f).Within(1e-4f);
        handles.EndDrag();
        handles.BeginDrag(HandlePart.Uniform, camera, _size, world, origin);
        Matrix4x4 grown = handles.Drag(camera, _size, origin + new Vector2(45, -45), snap: true);
        Matrix4x4.Decompose(grown, out Vector3 all, out _, out _);
        await Assert.That(all.X).IsEqualTo(all.Y).Within(1e-5f);
        await Assert.That(all.X).IsEqualTo(1.5f).Within(1e-4f);
    }
}
