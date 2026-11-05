using System.Numerics;
using Crucible.Render;

namespace Crucible.Editor;

/// <summary>
/// A viewport's camera (ADR 0067): it belongs to the viewport, not the world,
/// so scenes never save it. It looks at a <see cref="Pivot"/> from
/// <see cref="Distance"/> away, turned by <see cref="Yaw"/> and
/// <see cref="Pitch"/>; orbiting, panning, dollying, looking around, flying,
/// and framing move it as Unity's scene view does. Its matrices match the
/// renderer's (left-handed, Y up, reverse-Z with no far plane).
/// </summary>
public sealed class EditorCamera
{
    private const float TurnPerPixel = 0.005f; // radians
    private const float MaxPitch = 1.55f;      // just short of straight up or down

    /// <summary>What it orbits and looks at.</summary>
    public Vector3 Pivot { get; set; }

    public float Distance { get; set; } = 14;

    /// <summary>Radians around Y; 0 looks along +Z.</summary>
    public float Yaw { get; set; }

    /// <summary>Radians; above 0 looks down.</summary>
    public float Pitch { get; set; } = 0.5f;

    public float FieldOfViewY { get; set; } = MathF.PI / 3;

    public float NearPlane { get; set; } = 0.05f;

    /// <summary>Meters a second while flying.</summary>
    public float FlySpeed { get; set; } = 6;

    public Quaternion Rotation => Quaternion.CreateFromYawPitchRoll(Yaw, Pitch, 0);

    public Vector3 Forward => Vector3.Transform(Vector3.UnitZ, Rotation);

    public Vector3 Right => Vector3.Transform(Vector3.UnitX, Rotation);

    public Vector3 Up => Vector3.Transform(Vector3.UnitY, Rotation);

    public Vector3 Position => Pivot - Forward * Distance;

    /// <summary>The camera settings the renderer draws with.</summary>
    public Camera Camera => new()
    {
        Projection = CameraProjection.Perspective,
        FieldOfViewY = FieldOfViewY,
        NearPlane = NearPlane,
    };

    /// <summary>Turns around the pivot (Alt and the left button), by pointer movement in pixels.</summary>
    public void Orbit(Vector2 pixels)
    {
        Yaw += pixels.X * TurnPerPixel;
        Pitch = Math.Clamp(Pitch + pixels.Y * TurnPerPixel, -MaxPitch, MaxPitch);
    }

    /// <summary>Turns where it stands (the right button held), the pivot moving with the view.</summary>
    public void Look(Vector2 pixels)
    {
        Vector3 position = Position;
        Orbit(pixels);
        Pivot = position + Forward * Distance;
    }

    /// <summary>Slides sideways and up (the middle button), so what's under the pointer stays under it at the pivot's depth.</summary>
    public void Pan(Vector2 pixels, float viewportHeight)
    {
        float metersPerPixel = 2 * Distance * MathF.Tan(FieldOfViewY * 0.5f) / MathF.Max(viewportHeight, 1);
        Pivot += (-Right * pixels.X + Up * pixels.Y) * metersPerPixel;
    }

    /// <summary>Moves toward the pivot (the wheel): each notch 15% closer.</summary>
    public void Dolly(float notches) => Distance = Math.Clamp(Distance * MathF.Pow(0.85f, notches), 0.1f, 10000);

    /// <summary>Moves with WASD, Q and E (x right, y up, z forward), for so many seconds.</summary>
    public void Fly(Vector3 input, float seconds) =>
        Pivot += (Right * input.X + Up * input.Y + Forward * input.Z) * FlySpeed * seconds;

    /// <summary>Looks at a sphere from just far enough to see all of it (F).</summary>
    public void Frame(Vector3 center, float radius)
    {
        Pivot = center;
        Distance = MathF.Max(radius, 0.1f) / MathF.Sin(FieldOfViewY * 0.5f) * 1.1f;
    }

    public Matrix4x4 View
    {
        get
        {
            Matrix4x4.Invert(Matrix4x4.CreateFromQuaternion(Rotation) * Matrix4x4.CreateTranslation(Position), out Matrix4x4 view);
            return view;
        }
    }

    /// <summary>As the renderer's: reverse-Z, no far plane.</summary>
    public Matrix4x4 Projection(float aspectRatio)
    {
        float height = 1 / MathF.Tan(FieldOfViewY * 0.5f), width = height / aspectRatio;
        return new Matrix4x4(width, 0, 0, 0, 0, height, 0, 0, 0, 0, 0, 1, 0, 0, NearPlane, 0);
    }

    /// <summary>Where a world point is drawn in a viewport of <paramref name="size"/> (from the top left); null behind the camera.</summary>
    public Vector2? Project(Vector3 point, Vector2 size)
    {
        Vector4 clip = Vector4.Transform(new Vector4(point, 1), View * Projection(size.X / MathF.Max(size.Y, 1)));
        if (clip.W <= 1e-5f)
        {
            return null;
        }
        return new Vector2((clip.X / clip.W + 1) * 0.5f * size.X, (1 - clip.Y / clip.W) * 0.5f * size.Y);
    }

    /// <summary>The ray from the camera through a point of a viewport of <paramref name="size"/>.</summary>
    public (Vector3 Origin, Vector3 Direction) Ray(Vector2 point, Vector2 size)
    {
        float tan = MathF.Tan(FieldOfViewY * 0.5f), aspect = size.X / MathF.Max(size.Y, 1);
        float x = (point.X / MathF.Max(size.X, 1) * 2 - 1) * tan * aspect;
        float y = (1 - point.Y / MathF.Max(size.Y, 1) * 2) * tan;
        Vector3 direction = Vector3.Normalize(Forward + Right * x + Up * y);
        return (Position, direction);
    }

    /// <summary>World meters that look <paramref name="pixels"/> tall at a point, in a viewport <paramref name="height"/> pixels tall.</summary>
    public float WorldSize(Vector3 point, float pixels, float height)
    {
        float depth = MathF.Max(Vector3.Dot(point - Position, Forward), NearPlane);
        return pixels * 2 * depth * MathF.Tan(FieldOfViewY * 0.5f) / MathF.Max(height, 1);
    }
}
