using System.Numerics;
using Crucible.UI;

namespace Crucible.Editor;

/// <summary>What the handles do (W, E, R).</summary>
public enum HandleMode
{
    Move,
    Rotate,
    Scale,
}

/// <summary>Whether moving and rotating follow the world's axes or the entity's own; scaling is always its own.</summary>
public enum HandleSpace
{
    Local,
    World,
}

// One part of the handles, hit by the pointer or dragged.
internal enum HandlePart
{
    None,
    AxisX,
    AxisY,
    AxisZ,
    PlaneYZ,
    PlaneXZ,
    PlaneXY,
    RingX,
    RingY,
    RingZ,
    Uniform,
}

/// <summary>
/// Move, rotate, and scale handles over a viewport (ADR 0067): drawn by the
/// UI from the entity's projected position, a constant size on screen,
/// always on top; hit tested on screen; dragged by intersecting the pointer's
/// ray with the dragged axis, plane, or ring. Snapping (Ctrl) moves by 0.5 m,
/// turns by 15°, and scales by 0.1. Points and sizes are the viewport's own
/// units, from its top left.
/// </summary>
public sealed class TransformHandles
{
    internal const float SizePixels = 90; // an axis's length on screen
    private const float HitPixels = 8;
    private const int RingSegments = 64;
    private const float MoveSnap = 0.5f, TurnSnap = MathF.PI / 12, ScaleSnap = 0.1f;

    private static readonly Vector4 _red = new(0.92f, 0.27f, 0.25f, 1), _green = new(0.42f, 0.82f, 0.3f, 1);
    private static readonly Vector4 _blue = new(0.32f, 0.52f, 0.98f, 1), _hot = new(1f, 0.85f, 0.2f, 1);
    private static readonly Vector4 _white = new(0.92f, 0.92f, 0.94f, 1);

    // The drag: what's dragged, and the start it's measured from.
    private HandlePart _part;
    private Vector3 _position, _scale, _axis, _planeNormal, _startHit;
    private Quaternion _rotation;
    private float _startAlong;
    private Vector2 _startPoint, _screenAxis;
    private float _screenLength;

    public HandleMode Mode { get; set; } = HandleMode.Move;

    public HandleSpace Space { get; set; } = HandleSpace.World;

    internal HandlePart Dragging => _part;

    // The entity's position, rotation, and scale, and the axes the handles show.
    private (Vector3 Origin, Quaternion Rotation, Vector3 Scale, Vector3[] Axes) Frame(Matrix4x4 world)
    {
        Matrix4x4.Decompose(world, out Vector3 scale, out Quaternion rotation, out Vector3 origin);
        bool local = Space == HandleSpace.Local || Mode == HandleMode.Scale;
        Vector3[] axes = local
            ? [Vector3.Transform(Vector3.UnitX, rotation), Vector3.Transform(Vector3.UnitY, rotation), Vector3.Transform(Vector3.UnitZ, rotation)]
            : [Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ];
        return (origin, rotation, scale, axes);
    }

    /// <summary>The part under a point; None if the pointer isn't on them.</summary>
    internal HandlePart HitTest(EditorCamera camera, Vector2 size, Matrix4x4 world, Vector2 point)
    {
        (Vector3 origin, _, _, Vector3[] axes) = Frame(world);
        if (camera.Project(origin, size) is not { } center)
        {
            return HandlePart.None;
        }
        float length = camera.WorldSize(origin, SizePixels, size.Y);
        HandlePart best = HandlePart.None;
        float bestDistance = HitPixels;
        void Consider(HandlePart part, float distance)
        {
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = part;
            }
        }
        switch (Mode)
        {
            case HandleMode.Move:
                for (int a = 0; a < 3; ++a)
                {
                    // A plane's square first: it sits between two axes, where it should win.
                    Vector3[] square = PlaneSquare(origin, axes[(a + 1) % 3], axes[(a + 2) % 3], length);
                    if (Inside(camera, size, square, point))
                    {
                        return HandlePart.PlaneYZ + a;
                    }
                }
                for (int a = 0; a < 3; ++a)
                {
                    Consider(HandlePart.AxisX + a, SegmentDistance(camera, size, origin, origin + axes[a] * length, point));
                }
                break;
            case HandleMode.Rotate:
                for (int a = 0; a < 3; ++a)
                {
                    Vector3[] ring = Ring(origin, axes[a], length);
                    for (int i = 0; i < RingSegments; ++i)
                    {
                        Consider(HandlePart.RingX + a, SegmentDistance(camera, size, ring[i], ring[(i + 1) % RingSegments], point));
                    }
                }
                break;
            case HandleMode.Scale:
                if (MathF.Max(MathF.Abs(point.X - center.X), MathF.Abs(point.Y - center.Y)) <= HitPixels)
                {
                    return HandlePart.Uniform;
                }
                for (int a = 0; a < 3; ++a)
                {
                    Consider(HandlePart.AxisX + a, SegmentDistance(camera, size, origin, origin + axes[a] * length, point));
                }
                break;
        }
        return best;
    }

    /// <summary>Starts dragging a part from a point.</summary>
    internal void BeginDrag(HandlePart part, EditorCamera camera, Vector2 size, Matrix4x4 world, Vector2 point)
    {
        (Vector3 origin, Quaternion rotation, Vector3 scale, Vector3[] axes) = Frame(world);
        _part = part;
        _position = origin;
        _rotation = rotation;
        _scale = scale;
        _startPoint = point;
        (Vector3 rayOrigin, Vector3 rayDirection) = camera.Ray(point, size);
        int axis = AxisOf(part);
        if (axis >= 0)
        {
            _axis = axes[axis];
        }
        switch (part)
        {
            case >= HandlePart.AxisX and <= HandlePart.AxisZ when Mode == HandleMode.Move:
                _startAlong = AlongAxis(rayOrigin, rayDirection, origin, _axis);
                break;
            case >= HandlePart.AxisX and <= HandlePart.AxisZ:
                {
                    // Scaling: by how far the pointer moves along the axis's
                    // handle on screen; its whole length doubles the scale.
                    float length = camera.WorldSize(origin, SizePixels, size.Y);
                    Vector2 a = camera.Project(origin, size) ?? point;
                    Vector2 b = camera.Project(origin + _axis * length, size) ?? point + Vector2.UnitX;
                    _screenLength = MathF.Max((b - a).Length(), 1);
                    _screenAxis = (b - a) / _screenLength;
                    break;
                }
            case >= HandlePart.PlaneYZ and <= HandlePart.PlaneXY:
                _planeNormal = axes[part - HandlePart.PlaneYZ];
                _startHit = PlaneHit(rayOrigin, rayDirection, origin, _planeNormal) ?? origin;
                break;
            case >= HandlePart.RingX and <= HandlePart.RingZ:
                _planeNormal = _axis;
                _startHit = PlaneHit(rayOrigin, rayDirection, origin, _axis) ?? origin + Perpendicular(_axis);
                break;
        }
    }

    /// <summary>The entity's world matrix with the drag to a point applied.</summary>
    internal Matrix4x4 Drag(EditorCamera camera, Vector2 size, Vector2 point, bool snap)
    {
        (Vector3 rayOrigin, Vector3 rayDirection) = camera.Ray(point, size);
        Vector3 position = _position, scale = _scale;
        Quaternion rotation = _rotation;
        switch (_part)
        {
            case >= HandlePart.AxisX and <= HandlePart.AxisZ when Mode == HandleMode.Move:
                {
                    float moved = AlongAxis(rayOrigin, rayDirection, _position, _axis) - _startAlong;
                    position += _axis * (snap ? MathF.Round(moved / MoveSnap) * MoveSnap : moved);
                    break;
                }
            case >= HandlePart.PlaneYZ and <= HandlePart.PlaneXY:
                if (PlaneHit(rayOrigin, rayDirection, _position, _planeNormal) is { } hit)
                {
                    Vector3 moved = hit - _startHit;
                    position += snap ? new Vector3(Round(moved.X, MoveSnap), Round(moved.Y, MoveSnap), Round(moved.Z, MoveSnap)) : moved;
                }
                break;
            case >= HandlePart.RingX and <= HandlePart.RingZ:
                if (PlaneHit(rayOrigin, rayDirection, _position, _axis) is { } turned)
                {
                    Vector3 from = _startHit - _position, to = turned - _position;
                    float angle = MathF.Atan2(Vector3.Dot(Vector3.Cross(from, to), _axis), Vector3.Dot(from, to));
                    if (snap)
                    {
                        angle = Round(angle, TurnSnap);
                    }
                    rotation = Quaternion.Normalize(Quaternion.Concatenate(_rotation, Quaternion.CreateFromAxisAngle(_axis, angle)));
                }
                break;
            case >= HandlePart.AxisX and <= HandlePart.AxisZ:
                {
                    float factor = MathF.Max(1 + Vector2.Dot(point - _startPoint, _screenAxis) / _screenLength, 0.01f);
                    int axis = _part - HandlePart.AxisX;
                    float value = (axis == 0 ? _scale.X : axis == 1 ? _scale.Y : _scale.Z) * factor;
                    value = snap ? MathF.Max(Round(value, ScaleSnap), ScaleSnap) : value;
                    scale = axis == 0 ? scale with { X = value } : axis == 1 ? scale with { Y = value } : scale with { Z = value };
                    break;
                }
            case HandlePart.Uniform:
                {
                    Vector2 moved = point - _startPoint;
                    float factor = MathF.Max(1 + (moved.X - moved.Y) / (SizePixels * 2), 0.01f);
                    scale = _scale * factor;
                    if (snap)
                    {
                        scale = new Vector3(MathF.Max(Round(scale.X, ScaleSnap), ScaleSnap), MathF.Max(Round(scale.Y, ScaleSnap), ScaleSnap),
                            MathF.Max(Round(scale.Z, ScaleSnap), ScaleSnap));
                    }
                    break;
                }
        }
        return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(position);
    }

    internal void EndDrag() => _part = HandlePart.None;

    /// <summary>Draws the handles over an entity, the hovered (or dragged) part highlighted.</summary>
    internal void Draw(Canvas canvas, EditorCamera camera, Vector2 size, Matrix4x4 world, HandlePart hot)
    {
        (Vector3 origin, _, _, Vector3[] axes) = Frame(world);
        if (camera.Project(origin, size) is not { } center)
        {
            return;
        }
        float length = camera.WorldSize(origin, SizePixels, size.Y);
        Vector4[] colors = [_red, _green, _blue];
        HandlePart active = _part != HandlePart.None ? _part : hot;
        switch (Mode)
        {
            case HandleMode.Move:
                for (int a = 0; a < 3; ++a)
                {
                    Vector3[] square = PlaneSquare(origin, axes[(a + 1) % 3], axes[(a + 2) % 3], length);
                    if (ProjectAll(camera, size, square) is { } corners && Area(corners) >= MinimumSquareArea)
                    {
                        Vector4 color = active == HandlePart.PlaneYZ + a ? _hot : colors[a];
                        canvas.DrawPolyline([corners[0], corners[1], corners[2], corners[3], corners[0]], 1.5f, color);
                    }
                }
                for (int a = 0; a < 3; ++a)
                {
                    if (camera.Project(origin + axes[a] * length, size) is { } tip)
                    {
                        Vector4 color = active == HandlePart.AxisX + a ? _hot : colors[a];
                        canvas.DrawLine(center, tip, 2.5f, color);
                        canvas.FillRect(tip - new Vector2(4), new Vector2(8), color, 1);
                    }
                }
                break;
            case HandleMode.Rotate:
                for (int a = 0; a < 3; ++a)
                {
                    if (ProjectAll(camera, size, Ring(origin, axes[a], length)) is { } ring)
                    {
                        Vector2[] closed = [.. ring, ring[0]];
                        canvas.DrawPolyline(closed, 2.5f, active == HandlePart.RingX + a ? _hot : colors[a]);
                    }
                }
                break;
            case HandleMode.Scale:
                for (int a = 0; a < 3; ++a)
                {
                    if (camera.Project(origin + axes[a] * length, size) is { } tip)
                    {
                        Vector4 color = active == HandlePart.AxisX + a ? _hot : colors[a];
                        canvas.DrawLine(center, tip, 2.5f, color);
                        canvas.FillRect(tip - new Vector2(5), new Vector2(10), color);
                    }
                }
                canvas.FillRect(center - new Vector2(6), new Vector2(12), active == HandlePart.Uniform ? _hot : _white);
                break;
        }
    }

    private static int AxisOf(HandlePart part) => part switch
    {
        >= HandlePart.AxisX and <= HandlePart.AxisZ => part - HandlePart.AxisX,
        >= HandlePart.RingX and <= HandlePart.RingZ => part - HandlePart.RingX,
        _ => -1,
    };

    private static float Round(float value, float step) => MathF.Round(value / step) * step;

    // A square between two axes, a quarter to almost half the axes' length out.
    private static Vector3[] PlaneSquare(Vector3 origin, Vector3 a, Vector3 b, float length)
    {
        float near = length * 0.25f, far = length * 0.45f;
        return [origin + a * near + b * near, origin + a * far + b * near, origin + a * far + b * far, origin + a * near + b * far];
    }

    private static Vector3[] Ring(Vector3 origin, Vector3 axis, float radius)
    {
        Vector3 u = Perpendicular(axis), v = Vector3.Cross(axis, u);
        Vector3[] points = new Vector3[RingSegments];
        for (int i = 0; i < RingSegments; ++i)
        {
            float angle = i * MathF.Tau / RingSegments;
            points[i] = origin + (u * MathF.Cos(angle) + v * MathF.Sin(angle)) * radius;
        }
        return points;
    }

    private static Vector3 Perpendicular(Vector3 axis)
    {
        Vector3 other = MathF.Abs(axis.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
        return Vector3.Normalize(Vector3.Cross(other, axis));
    }

    private static Vector2[]? ProjectAll(EditorCamera camera, Vector2 size, Vector3[] points)
    {
        Vector2[] projected = new Vector2[points.Length];
        for (int i = 0; i < points.Length; ++i)
        {
            if (camera.Project(points[i], size) is not { } p)
            {
                return null;
            }
            projected[i] = p;
        }
        return projected;
    }

    // A plane's square seen nearly edge-on is too thin to aim at: neither hit nor drawn.
    private const float MinimumSquareArea = 40; // square pixels

    private static float Area(Vector2[] corners)
    {
        float twice = 0;
        for (int i = 0; i < corners.Length; ++i)
        {
            Vector2 a = corners[i], b = corners[(i + 1) % corners.Length];
            twice += a.X * b.Y - b.X * a.Y;
        }
        return MathF.Abs(twice) * 0.5f;
    }

    private static bool Inside(EditorCamera camera, Vector2 size, Vector3[] square, Vector2 point)
    {
        if (ProjectAll(camera, size, square) is not { } corners || Area(corners) < MinimumSquareArea)
        {
            return false;
        }
        // Inside a convex quad: on the same side of all four edges.
        float sign = 0;
        for (int i = 0; i < 4; ++i)
        {
            Vector2 edge = corners[(i + 1) % 4] - corners[i], to = point - corners[i];
            float cross = edge.X * to.Y - edge.Y * to.X;
            if (sign != 0 && MathF.Sign(cross) != sign)
            {
                return false;
            }
            sign = cross != 0 ? MathF.Sign(cross) : sign;
        }
        return true;
    }

    private static float SegmentDistance(EditorCamera camera, Vector2 size, Vector3 from, Vector3 to, Vector2 point)
    {
        if (camera.Project(from, size) is not { } a || camera.Project(to, size) is not { } b)
        {
            return float.PositiveInfinity;
        }
        Vector2 ab = b - a;
        float t = ab.LengthSquared() > 0 ? Math.Clamp(Vector2.Dot(point - a, ab) / ab.LengthSquared(), 0, 1) : 0;
        return Vector2.Distance(point, a + ab * t);
    }

    // How far along an axis line (from origin) the point nearest a ray is.
    private static float AlongAxis(Vector3 rayOrigin, Vector3 rayDirection, Vector3 origin, Vector3 axis)
    {
        Vector3 w = origin - rayOrigin;
        float b = Vector3.Dot(axis, rayDirection), d = Vector3.Dot(axis, w), e = Vector3.Dot(rayDirection, w);
        float denominator = 1 - b * b;
        return MathF.Abs(denominator) < 1e-6f ? 0 : (b * e - d) / denominator;
    }

    private static Vector3? PlaneHit(Vector3 rayOrigin, Vector3 rayDirection, Vector3 origin, Vector3 normal)
    {
        float facing = Vector3.Dot(rayDirection, normal);
        if (MathF.Abs(facing) < 1e-6f)
        {
            return null;
        }
        float t = Vector3.Dot(origin - rayOrigin, normal) / facing;
        return t > 0 ? rayOrigin + rayDirection * t : null;
    }
}
