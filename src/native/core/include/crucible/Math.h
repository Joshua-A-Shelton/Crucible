#pragma once

// Crucible's math storage types and scalar math (ADR 0024 and its
// amendment). Plain structs, byte-identical to System.Numerics, which is
// what they are in C#: Vector2/3/4, Quaternion (x, y, z, w), and Matrix4x4
// (row-major rows of row vectors). Names are placeholders (docs/naming.md).
//
// Conventions:
// - Row vectors: points transform as p * M, and composition reads left to
//   right in the order things happen (scale * rotation * translation).
// - Left-handed, Y up, +Z forward. Rotations follow the left-hand rule: a
//   positive yaw turns +Z (forward) toward +X (right).
// - Angles are radians. Euler angles are yaw (around Y), pitch (around X),
//   and roll (around Z), applied roll, then pitch, then yaw, as in
//   System.Numerics' Quaternion.CreateFromYawPitchRoll.
// - Projections are left-handed, with depth from 0 (near) to 1 (far): Slag
//   gives both backends D3D12's clip space (ADR 0024's amendment).
//
// Each operation computes what its System.Numerics counterpart does, so the
// two can be compared directly (tests/managed: MathTests).
//
// SIMD comes in M3 step 3, as separate compute types; these stay plain data.

#include <crucible/Expose.h>

#include <cmath>

namespace crucible {

struct CRUCIBLE_EXPOSE_AS("System.Numerics.Vector2") Vector2
{
    float x;
    float y;

    float& operator[](int index)
    {
        return (&x)[index];
    }
    float operator[](int index) const
    {
        return (&x)[index];
    }
};

struct CRUCIBLE_EXPOSE_AS("System.Numerics.Vector3") Vector3
{
    float x;
    float y;
    float z;

    float& operator[](int index)
    {
        return (&x)[index];
    }
    float operator[](int index) const
    {
        return (&x)[index];
    }
};

struct CRUCIBLE_EXPOSE_AS("System.Numerics.Vector4") Vector4
{
    float x;
    float y;
    float z;
    float w;

    float& operator[](int index)
    {
        return (&x)[index];
    }
    float operator[](int index) const
    {
        return (&x)[index];
    }
};

// A rotation. (x, y, z) is the axis times sin(angle / 2), w is cos(angle / 2).
struct CRUCIBLE_EXPOSE_AS("System.Numerics.Quaternion") Quaternion
{
    float x;
    float y;
    float z;
    float w;

    static constexpr Quaternion identity()
    {
        return {0, 0, 0, 1};
    }

    // `axis` must be normalized.
    static Quaternion fromAxisAngle(Vector3 axis, float angle);
    static Quaternion fromYawPitchRoll(float yaw, float pitch, float roll);
};

// Four rows; with row vectors, rows[3] holds the translation.
struct CRUCIBLE_EXPOSE_AS("System.Numerics.Matrix4x4") Matrix4x4
{
    Vector4 rows[4];

    static constexpr Matrix4x4 identity()
    {
        return {{{1, 0, 0, 0}, {0, 1, 0, 0}, {0, 0, 1, 0}, {0, 0, 0, 1}}};
    }

    static Matrix4x4 translation(Vector3 offset);
    static Matrix4x4 scale(Vector3 factors);
    static Matrix4x4 rotationX(float angle);
    static Matrix4x4 rotationY(float angle);
    static Matrix4x4 rotationZ(float angle);
    static Matrix4x4 rotation(Quaternion rotation);

    // A left-handed perspective projection, depth 0 at `nearPlane` and 1 at
    // `farPlane`. `fieldOfViewY` is the full vertical angle.
    static Matrix4x4 perspectiveFieldOfView(float fieldOfViewY, float aspectRatio, float nearPlane, float farPlane);
    // A left-handed orthographic projection centered on the view axis.
    static Matrix4x4 orthographic(float width, float height, float nearPlane, float farPlane);
    // A left-handed view matrix: the camera at `eye`, looking at `target`.
    static Matrix4x4 lookAt(Vector3 eye, Vector3 target, Vector3 up);
};

// ---------------------------------------------------------------------------
// Vectors
// ---------------------------------------------------------------------------

// Written out per type rather than generated: a few lines each, and they
// stay readable in a debugger.

constexpr Vector2 operator+(Vector2 a, Vector2 b)
{
    return {a.x + b.x, a.y + b.y};
}
constexpr Vector2 operator-(Vector2 a, Vector2 b)
{
    return {a.x - b.x, a.y - b.y};
}
constexpr Vector2 operator-(Vector2 a)
{
    return {-a.x, -a.y};
}
constexpr Vector2 operator*(Vector2 a, Vector2 b)
{
    return {a.x * b.x, a.y * b.y};
}
constexpr Vector2 operator*(Vector2 a, float s)
{
    return {a.x * s, a.y * s};
}
constexpr Vector2 operator*(float s, Vector2 a)
{
    return a * s;
}
constexpr Vector2 operator/(Vector2 a, float s)
{
    return {a.x / s, a.y / s};
}
constexpr bool operator==(Vector2 a, Vector2 b)
{
    return a.x == b.x && a.y == b.y;
}

constexpr Vector3 operator+(Vector3 a, Vector3 b)
{
    return {a.x + b.x, a.y + b.y, a.z + b.z};
}
constexpr Vector3 operator-(Vector3 a, Vector3 b)
{
    return {a.x - b.x, a.y - b.y, a.z - b.z};
}
constexpr Vector3 operator-(Vector3 a)
{
    return {-a.x, -a.y, -a.z};
}
constexpr Vector3 operator*(Vector3 a, Vector3 b)
{
    return {a.x * b.x, a.y * b.y, a.z * b.z};
}
constexpr Vector3 operator*(Vector3 a, float s)
{
    return {a.x * s, a.y * s, a.z * s};
}
constexpr Vector3 operator*(float s, Vector3 a)
{
    return a * s;
}
constexpr Vector3 operator/(Vector3 a, float s)
{
    return {a.x / s, a.y / s, a.z / s};
}
constexpr bool operator==(Vector3 a, Vector3 b)
{
    return a.x == b.x && a.y == b.y && a.z == b.z;
}

constexpr Vector4 operator+(Vector4 a, Vector4 b)
{
    return {a.x + b.x, a.y + b.y, a.z + b.z, a.w + b.w};
}
constexpr Vector4 operator-(Vector4 a, Vector4 b)
{
    return {a.x - b.x, a.y - b.y, a.z - b.z, a.w - b.w};
}
constexpr Vector4 operator-(Vector4 a)
{
    return {-a.x, -a.y, -a.z, -a.w};
}
constexpr Vector4 operator*(Vector4 a, Vector4 b)
{
    return {a.x * b.x, a.y * b.y, a.z * b.z, a.w * b.w};
}
constexpr Vector4 operator*(Vector4 a, float s)
{
    return {a.x * s, a.y * s, a.z * s, a.w * s};
}
constexpr Vector4 operator*(float s, Vector4 a)
{
    return a * s;
}
constexpr Vector4 operator/(Vector4 a, float s)
{
    return {a.x / s, a.y / s, a.z / s, a.w / s};
}
constexpr bool operator==(Vector4 a, Vector4 b)
{
    return a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;
}

constexpr float dot(Vector2 a, Vector2 b)
{
    return a.x * b.x + a.y * b.y;
}
constexpr float dot(Vector3 a, Vector3 b)
{
    return a.x * b.x + a.y * b.y + a.z * b.z;
}
constexpr float dot(Vector4 a, Vector4 b)
{
    return a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w;
}

// In left-handed space, cross(+X, +Y) is +Z: the left-hand rule.
constexpr Vector3 cross(Vector3 a, Vector3 b)
{
    return {a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x};
}

template <class V>
constexpr float lengthSquared(V v)
{
    return dot(v, v);
}

template <class V>
float length(V v)
{
    return std::sqrt(dot(v, v));
}

template <class V>
float distance(V a, V b)
{
    return length(a - b);
}

// Unit length in the same direction; a zero vector gives NaNs, as in
// System.Numerics.
template <class V>
V normalize(V v)
{
    return v / length(v);
}

// a at t = 0, b at t = 1.
template <class V>
constexpr V lerp(V a, V b, float t)
{
    return a * (1.0f - t) + b * t;
}

constexpr Vector3 min(Vector3 a, Vector3 b)
{
    return {a.x < b.x ? a.x : b.x, a.y < b.y ? a.y : b.y, a.z < b.z ? a.z : b.z};
}
constexpr Vector3 max(Vector3 a, Vector3 b)
{
    return {a.x > b.x ? a.x : b.x, a.y > b.y ? a.y : b.y, a.z > b.z ? a.z : b.z};
}

// ---------------------------------------------------------------------------
// Quaternions
// ---------------------------------------------------------------------------

// The Hamilton product, as System.Numerics' operator*. With row vectors,
// a * b means "b, then a"; concatenate(a, b) reads in the order things happen.
constexpr Quaternion operator*(Quaternion a, Quaternion b)
{
    const float cx = a.y * b.z - a.z * b.y;
    const float cy = a.z * b.x - a.x * b.z;
    const float cz = a.x * b.y - a.y * b.x;
    const float d = a.x * b.x + a.y * b.y + a.z * b.z;
    return {a.x * b.w + b.x * a.w + cx, a.y * b.w + b.y * a.w + cy, a.z * b.w + b.z * a.w + cz, a.w * b.w - d};
}

// Rotation `first`, then `second`.
constexpr Quaternion concatenate(Quaternion first, Quaternion second)
{
    return second * first;
}

constexpr bool operator==(Quaternion a, Quaternion b)
{
    return a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;
}

constexpr float dot(Quaternion a, Quaternion b)
{
    return a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w;
}

constexpr Quaternion conjugate(Quaternion q)
{
    return {-q.x, -q.y, -q.z, q.w};
}

// The opposite rotation; for unit quaternions it equals the conjugate.
constexpr Quaternion inverse(Quaternion q)
{
    const float inverseLengthSquared = 1.0f / dot(q, q);
    return {-q.x * inverseLengthSquared, -q.y * inverseLengthSquared, -q.z * inverseLengthSquared,
            q.w * inverseLengthSquared};
}

inline Quaternion normalize(Quaternion q)
{
    const float inverseLength = 1.0f / std::sqrt(dot(q, q));
    return {q.x * inverseLength, q.y * inverseLength, q.z * inverseLength, q.w * inverseLength};
}

// Spherical interpolation along the shorter arc.
inline Quaternion slerp(Quaternion a, Quaternion b, float t)
{
    constexpr float epsilon = 1e-6f;
    float cosOmega = dot(a, b);
    const bool flip = cosOmega < 0.0f;
    if (flip)
    {
        cosOmega = -cosOmega;
    }
    float s1;
    float s2;
    if (cosOmega > 1.0f - epsilon)
    {
        // Nearly the same rotation: linear interpolation is accurate and
        // avoids dividing by sin(omega) near zero.
        s1 = 1.0f - t;
        s2 = flip ? -t : t;
    }
    else
    {
        const float omega = std::acos(cosOmega);
        const float inverseSinOmega = 1.0f / std::sin(omega);
        s1 = std::sin((1.0f - t) * omega) * inverseSinOmega;
        s2 = flip ? -std::sin(t * omega) * inverseSinOmega : std::sin(t * omega) * inverseSinOmega;
    }
    return {s1 * a.x + s2 * b.x, s1 * a.y + s2 * b.y, s1 * a.z + s2 * b.z, s1 * a.w + s2 * b.w};
}

// Rotates v by q (System.Numerics' Vector3.Transform(Vector3, Quaternion)).
constexpr Vector3 rotate(Vector3 v, Quaternion q)
{
    const float x2 = q.x + q.x;
    const float y2 = q.y + q.y;
    const float z2 = q.z + q.z;
    const float wx2 = q.w * x2;
    const float wy2 = q.w * y2;
    const float wz2 = q.w * z2;
    const float xx2 = q.x * x2;
    const float xy2 = q.x * y2;
    const float xz2 = q.x * z2;
    const float yy2 = q.y * y2;
    const float yz2 = q.y * z2;
    const float zz2 = q.z * z2;
    return {v.x * (1.0f - yy2 - zz2) + v.y * (xy2 - wz2) + v.z * (xz2 + wy2),
            v.x * (xy2 + wz2) + v.y * (1.0f - xx2 - zz2) + v.z * (yz2 - wx2),
            v.x * (xz2 - wy2) + v.y * (yz2 + wx2) + v.z * (1.0f - xx2 - yy2)};
}

inline Quaternion Quaternion::fromAxisAngle(Vector3 axis, float angle)
{
    const float half = angle * 0.5f;
    const float s = std::sin(half);
    return {axis.x * s, axis.y * s, axis.z * s, std::cos(half)};
}

inline Quaternion Quaternion::fromYawPitchRoll(float yaw, float pitch, float roll)
{
    const float sr = std::sin(roll * 0.5f);
    const float cr = std::cos(roll * 0.5f);
    const float sp = std::sin(pitch * 0.5f);
    const float cp = std::cos(pitch * 0.5f);
    const float sy = std::sin(yaw * 0.5f);
    const float cy = std::cos(yaw * 0.5f);
    return {cy * sp * cr + sy * cp * sr, sy * cp * cr - cy * sp * sr, cy * cp * sr - sy * sp * cr,
            cy * cp * cr + sy * sp * sr};
}

// ---------------------------------------------------------------------------
// Matrices
// ---------------------------------------------------------------------------

constexpr Matrix4x4 operator*(const Matrix4x4& a, const Matrix4x4& b)
{
    Matrix4x4 result{};
    for (int row = 0; row < 4; ++row)
    {
        const Vector4 r = a.rows[row];
        result.rows[row] = b.rows[0] * r.x + b.rows[1] * r.y + b.rows[2] * r.z + b.rows[3] * r.w;
    }
    return result;
}

constexpr bool operator==(const Matrix4x4& a, const Matrix4x4& b)
{
    return a.rows[0] == b.rows[0] && a.rows[1] == b.rows[1] && a.rows[2] == b.rows[2] && a.rows[3] == b.rows[3];
}

inline Matrix4x4 transpose(const Matrix4x4& m)
{
    Matrix4x4 result{};
    for (int row = 0; row < 4; ++row)
    {
        for (int column = 0; column < 4; ++column)
        {
            result.rows[row][column] = m.rows[column][row];
        }
    }
    return result;
}

float determinant(const Matrix4x4& m);

// Writes the inverse to `result` and returns true, or returns false (and
// writes NaNs, as System.Numerics does) if `m` isn't invertible.
bool invert(const Matrix4x4& m, Matrix4x4& result);

// p * M with w = 1: rotates, scales, and translates a point.
constexpr Vector3 transformPoint(Vector3 p, const Matrix4x4& m)
{
    const Vector4 r = m.rows[0] * p.x + m.rows[1] * p.y + m.rows[2] * p.z + m.rows[3];
    return {r.x, r.y, r.z};
}

// v * M with w = 0: rotates and scales a direction, without translation.
constexpr Vector3 transformDirection(Vector3 v, const Matrix4x4& m)
{
    const Vector4 r = m.rows[0] * v.x + m.rows[1] * v.y + m.rows[2] * v.z;
    return {r.x, r.y, r.z};
}

constexpr Vector4 transform(Vector4 v, const Matrix4x4& m)
{
    return m.rows[0] * v.x + m.rows[1] * v.y + m.rows[2] * v.z + m.rows[3] * v.w;
}

} // namespace crucible
