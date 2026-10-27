#include <crucible/Math.h>

#include <cmath>
#include <limits>

namespace crucible {

Matrix4x4 Matrix4x4::translation(Vector3 offset)
{
    Matrix4x4 m = identity();
    m.rows[3] = {offset.x, offset.y, offset.z, 1};
    return m;
}

Matrix4x4 Matrix4x4::scale(Vector3 factors)
{
    return {{{factors.x, 0, 0, 0}, {0, factors.y, 0, 0}, {0, 0, factors.z, 0}, {0, 0, 0, 1}}};
}

Matrix4x4 Matrix4x4::rotationX(float angle)
{
    const float c = std::cos(angle);
    const float s = std::sin(angle);
    return {{{1, 0, 0, 0}, {0, c, s, 0}, {0, -s, c, 0}, {0, 0, 0, 1}}};
}

// A positive angle turns +Z (forward) toward +X (right).
Matrix4x4 Matrix4x4::rotationY(float angle)
{
    const float c = std::cos(angle);
    const float s = std::sin(angle);
    return {{{c, 0, -s, 0}, {0, 1, 0, 0}, {s, 0, c, 0}, {0, 0, 0, 1}}};
}

Matrix4x4 Matrix4x4::rotationZ(float angle)
{
    const float c = std::cos(angle);
    const float s = std::sin(angle);
    return {{{c, s, 0, 0}, {-s, c, 0, 0}, {0, 0, 1, 0}, {0, 0, 0, 1}}};
}

Matrix4x4 Matrix4x4::rotation(Quaternion q)
{
    const float xx = q.x * q.x;
    const float yy = q.y * q.y;
    const float zz = q.z * q.z;
    const float xy = q.x * q.y;
    const float wz = q.z * q.w;
    const float xz = q.z * q.x;
    const float wy = q.y * q.w;
    const float yz = q.y * q.z;
    const float wx = q.x * q.w;
    return {{{1.0f - 2.0f * (yy + zz), 2.0f * (xy + wz), 2.0f * (xz - wy), 0},
             {2.0f * (xy - wz), 1.0f - 2.0f * (zz + xx), 2.0f * (yz + wx), 0},
             {2.0f * (xz + wy), 2.0f * (yz - wx), 1.0f - 2.0f * (yy + xx), 0},
             {0, 0, 0, 1}}};
}

// View-space z in [near, far] maps to depth [0, 1] after the divide by w = z.
Matrix4x4 Matrix4x4::perspectiveFieldOfView(float fieldOfViewY, float aspectRatio, float nearPlane, float farPlane)
{
    const float height = 1.0f / std::tan(fieldOfViewY * 0.5f);
    const float width = height / aspectRatio;
    const float range = std::isinf(farPlane) ? 1.0f : farPlane / (farPlane - nearPlane);
    return {{{width, 0, 0, 0}, {0, height, 0, 0}, {0, 0, range, 1}, {0, 0, -range * nearPlane, 0}}};
}

Matrix4x4 Matrix4x4::orthographic(float width, float height, float nearPlane, float farPlane)
{
    const float range = 1.0f / (farPlane - nearPlane);
    return {{{2.0f / width, 0, 0, 0}, {0, 2.0f / height, 0, 0}, {0, 0, range, 0}, {0, 0, -range * nearPlane, 1}}};
}

// The camera's axes become the matrix's columns: +Z toward the target, +X to
// the right of it, +Y up.
Matrix4x4 Matrix4x4::lookAt(Vector3 eye, Vector3 target, Vector3 up)
{
    const Vector3 axisZ = normalize(target - eye);
    const Vector3 axisX = normalize(cross(up, axisZ));
    const Vector3 axisY = cross(axisZ, axisX);
    return {{{axisX.x, axisY.x, axisZ.x, 0},
             {axisX.y, axisY.y, axisZ.y, 0},
             {axisX.z, axisY.z, axisZ.z, 0},
             {-dot(axisX, eye), -dot(axisY, eye), -dot(axisZ, eye), 1}}};
}

namespace {

// The cofactor expansion System.Numerics' scalar path uses, so results
// match it to the last bits where the hardware rounds the same way.
struct Cofactors
{
    float a, b, c, d, e, f, g, h, i, j, k, l, m, n, o, p;
    float kpLo, jpLn, joKn, ipLm, ioKm, inJm;
    float a11, a12, a13, a14;
    float det;

    explicit Cofactors(const Matrix4x4& matrix)
        : a(matrix.rows[0].x)
        , b(matrix.rows[0].y)
        , c(matrix.rows[0].z)
        , d(matrix.rows[0].w)
        , e(matrix.rows[1].x)
        , f(matrix.rows[1].y)
        , g(matrix.rows[1].z)
        , h(matrix.rows[1].w)
        , i(matrix.rows[2].x)
        , j(matrix.rows[2].y)
        , k(matrix.rows[2].z)
        , l(matrix.rows[2].w)
        , m(matrix.rows[3].x)
        , n(matrix.rows[3].y)
        , o(matrix.rows[3].z)
        , p(matrix.rows[3].w)
    {
        kpLo = k * p - l * o;
        jpLn = j * p - l * n;
        joKn = j * o - k * n;
        ipLm = i * p - l * m;
        ioKm = i * o - k * m;
        inJm = i * n - j * m;
        a11 = +(f * kpLo - g * jpLn + h * joKn);
        a12 = -(e * kpLo - g * ipLm + h * ioKm);
        a13 = +(e * jpLn - f * ipLm + h * inJm);
        a14 = -(e * joKn - f * ioKm + g * inJm);
        det = a * a11 + b * a12 + c * a13 + d * a14;
    }
};

} // namespace

float determinant(const Matrix4x4& m)
{
    return Cofactors(m).det;
}

bool invert(const Matrix4x4& matrix, Matrix4x4& result)
{
    const Cofactors x(matrix);
    if (std::fabs(x.det) < std::numeric_limits<float>::denorm_min())
    {
        const float nan = std::numeric_limits<float>::quiet_NaN();
        result = {{{nan, nan, nan, nan}, {nan, nan, nan, nan}, {nan, nan, nan, nan}, {nan, nan, nan, nan}}};
        return false;
    }
    const float invDet = 1.0f / x.det;
    const float a = x.a, b = x.b, c = x.c, d = x.d, e = x.e, f = x.f, g = x.g, h = x.h;
    const float i = x.i, j = x.j, k = x.k, l = x.l, m = x.m, n = x.n, o = x.o, p = x.p;

    result.rows[0].x = x.a11 * invDet;
    result.rows[1].x = x.a12 * invDet;
    result.rows[2].x = x.a13 * invDet;
    result.rows[3].x = x.a14 * invDet;

    result.rows[0].y = -(b * x.kpLo - c * x.jpLn + d * x.joKn) * invDet;
    result.rows[1].y = +(a * x.kpLo - c * x.ipLm + d * x.ioKm) * invDet;
    result.rows[2].y = -(a * x.jpLn - b * x.ipLm + d * x.inJm) * invDet;
    result.rows[3].y = +(a * x.joKn - b * x.ioKm + c * x.inJm) * invDet;

    const float gpHo = g * p - h * o;
    const float fpHn = f * p - h * n;
    const float foGn = f * o - g * n;
    const float epHm = e * p - h * m;
    const float eoGm = e * o - g * m;
    const float enFm = e * n - f * m;
    result.rows[0].z = +(b * gpHo - c * fpHn + d * foGn) * invDet;
    result.rows[1].z = -(a * gpHo - c * epHm + d * eoGm) * invDet;
    result.rows[2].z = +(a * fpHn - b * epHm + d * enFm) * invDet;
    result.rows[3].z = -(a * foGn - b * eoGm + c * enFm) * invDet;

    const float glHk = g * l - h * k;
    const float flHj = f * l - h * j;
    const float fkGj = f * k - g * j;
    const float elHi = e * l - h * i;
    const float ekGi = e * k - g * i;
    const float ejFi = e * j - f * i;
    result.rows[0].w = -(b * glHk - c * flHj + d * fkGj) * invDet;
    result.rows[1].w = +(a * glHk - c * elHi + d * ekGi) * invDet;
    result.rows[2].w = -(a * flHj - b * elHi + d * ejFi) * invDet;
    result.rows[3].w = +(a * fkGj - b * ekGi + c * ejFi) * invDet;
    return true;
}

} // namespace crucible
