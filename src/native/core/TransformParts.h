#pragma once

// A world matrix taken apart into a place, a turn, and a scale, and put back
// together: for code that works with rigid poses (physics, sockets,
// hitboxes) on matrices made by the transform hierarchy (ADR 0050). Row
// vectors, as Matrix4x4 everywhere: scale, then rotation, then translation.

#include <crucible/Math.h>

#include <cmath>

namespace crucible {

struct TransformParts
{
    Vector3 position{};
    Quaternion rotation = Quaternion::identity();
    Vector3 scale{1, 1, 1};
};

// A rotation matrix's quaternion, for row vectors (Matrix4x4::rotation's
// inverse; System.Numerics' CreateFromRotationMatrix).
inline Quaternion rotationOf(const Vector3 (&r)[3])
{
    const float trace = r[0].x + r[1].y + r[2].z;
    if (trace > 0)
    {
        const float s = std::sqrt(trace + 1);
        const float i = 0.5f / s;
        return {(r[1].z - r[2].y) * i, (r[2].x - r[0].z) * i, (r[0].y - r[1].x) * i, s * 0.5f};
    }
    if (r[0].x >= r[1].y && r[0].x >= r[2].z)
    {
        const float s = std::sqrt(1 + r[0].x - r[1].y - r[2].z);
        const float i = 0.5f / s;
        return {0.5f * s, (r[0].y + r[1].x) * i, (r[0].z + r[2].x) * i, (r[1].z - r[2].y) * i};
    }
    if (r[1].y > r[2].z)
    {
        const float s = std::sqrt(1 + r[1].y - r[0].x - r[2].z);
        const float i = 0.5f / s;
        return {(r[1].x + r[0].y) * i, 0.5f * s, (r[2].y + r[1].z) * i, (r[2].x - r[0].z) * i};
    }
    const float s = std::sqrt(1 + r[2].z - r[0].x - r[1].y);
    const float i = 0.5f / s;
    return {(r[2].x + r[0].z) * i, (r[2].y + r[1].z) * i, 0.5f * s, (r[0].y - r[1].x) * i};
}

// Each row's length is its axis's scale; a mirror (negative determinant)
// shows as a turn, as it has no place in a quaternion.
inline TransformParts decompose(const Matrix4x4& m)
{
    TransformParts parts;
    parts.position = {m.rows[3].x, m.rows[3].y, m.rows[3].z};
    Vector3 axes[3];
    for (int i = 0; i < 3; ++i)
    {
        const Vector3 row{m.rows[i].x, m.rows[i].y, m.rows[i].z};
        const float scale = length(row);
        (i == 0 ? parts.scale.x : i == 1 ? parts.scale.y : parts.scale.z) = scale;
        axes[i] =
            scale > 1e-12f ? row * (1.0f / scale) : Vector3{i == 0 ? 1.0f : 0, i == 1 ? 1.0f : 0, i == 2 ? 1.0f : 0};
    }
    parts.rotation = normalize(rotationOf(axes));
    return parts;
}

inline Matrix4x4 compose(Vector3 position, Quaternion rotation, Vector3 scale)
{
    Matrix4x4 m = Matrix4x4::rotation(rotation);
    m.rows[0] = m.rows[0] * scale.x;
    m.rows[1] = m.rows[1] * scale.y;
    m.rows[2] = m.rows[2] * scale.z;
    m.rows[3] = {position.x, position.y, position.z, 1};
    return m;
}

} // namespace crucible
