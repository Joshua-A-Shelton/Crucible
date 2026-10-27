// Math storage types and scalar math (ADR 0024): properties a correct
// library must have. The C# suite (MathTests.cs) compares every operation
// with System.Numerics.

#include <crucible/Math.h>

#include <doctest/doctest.h>

#include <cmath>
#include <numbers>
#include <random>

using namespace crucible;

namespace {

constexpr float tolerance = 1e-5f;

bool near(float a, float b)
{
    return std::fabs(a - b) <= tolerance * (1.0f + std::fabs(a) + std::fabs(b));
}

bool near(Vector3 a, Vector3 b)
{
    return near(a.x, b.x) && near(a.y, b.y) && near(a.z, b.z);
}

bool near(const Matrix4x4& a, const Matrix4x4& b)
{
    for (int row = 0; row < 4; ++row)
    {
        for (int column = 0; column < 4; ++column)
        {
            if (!near(a.rows[row][column], b.rows[row][column]))
            {
                return false;
            }
        }
    }
    return true;
}

struct Random
{
    std::minstd_rand engine{1234};

    float value(float low = -1.0f, float high = 1.0f)
    {
        return std::uniform_real_distribution<float>(low, high)(engine);
    }

    Vector3 vector()
    {
        return {value(), value(), value()};
    }

    Quaternion rotation()
    {
        return Quaternion::fromAxisAngle(normalize(vector()), value(-3.0f, 3.0f));
    }
};

constexpr float pi = std::numbers::pi_v<float>;

} // namespace

TEST_CASE("Space is left-handed with Y up and +Z forward")
{
    CHECK(cross(Vector3{1, 0, 0}, Vector3{0, 1, 0}) == Vector3{0, 0, 1});
    // A positive yaw turns forward (+Z) toward right (+X).
    CHECK(near(transformDirection({0, 0, 1}, Matrix4x4::rotationY(pi / 2)), {1, 0, 0}));
    CHECK(near(rotate({0, 0, 1}, Quaternion::fromYawPitchRoll(pi / 2, 0, 0)), {1, 0, 0}));
}

TEST_CASE("Points are row vectors and composition reads in order")
{
    // Scale, then rotate, then translate: S * R * T.
    const Matrix4x4 s = Matrix4x4::scale({2, 2, 2});
    const Matrix4x4 r = Matrix4x4::rotationY(pi / 2);
    const Matrix4x4 t = Matrix4x4::translation({10, 0, 0});
    CHECK(near(transformPoint({0, 0, 1}, s * r * t), {12, 0, 0}));
    CHECK(transform(Vector4{1, 2, 3, 0}, t) == Vector4{1, 2, 3, 0}); // directions don't move
}

TEST_CASE("Matrix4x4 is four Vector4 rows, byte-compatible with System.Numerics")
{
    static_assert(sizeof(Vector2) == 8 && sizeof(Vector3) == 12 && sizeof(Vector4) == 16);
    static_assert(sizeof(Quaternion) == 16 && sizeof(Matrix4x4) == 64);
    static_assert(alignof(Vector4) == 4 && alignof(Matrix4x4) == 4); // storage types, not SIMD
    CHECK(Matrix4x4::translation({1, 2, 3}).rows[3] == Vector4{1, 2, 3, 1});
}

TEST_CASE("A matrix times its inverse is the identity")
{
    Random random;
    for (int i = 0; i < 100; ++i)
    {
        const Matrix4x4 m = Matrix4x4::scale({random.value(0.5f, 2.0f), random.value(0.5f, 2.0f), 1.5f}) *
                            Matrix4x4::rotation(random.rotation()) * Matrix4x4::translation(random.vector());
        Matrix4x4 inverted{};
        REQUIRE(invert(m, inverted));
        CHECK(near(m * inverted, Matrix4x4::identity()));
        CHECK(near(determinant(m) * determinant(inverted), 1.0f));
        CHECK(transpose(transpose(m)) == m);
    }

    Matrix4x4 singular = Matrix4x4::scale({1, 0, 1});
    Matrix4x4 result{};
    CHECK_FALSE(invert(singular, result));
    CHECK(std::isnan(result.rows[0].x));
}

TEST_CASE("Quaternions and their matrices rotate alike")
{
    Random random;
    for (int i = 0; i < 100; ++i)
    {
        const Quaternion a = random.rotation();
        const Quaternion b = random.rotation();
        const Vector3 v = random.vector();
        CHECK(near(rotate(v, a), transformDirection(v, Matrix4x4::rotation(a))));
        // concatenate(a, b): a first, then b; the matrices in the same order.
        CHECK(near(rotate(v, concatenate(a, b)), rotate(rotate(v, a), b)));
        CHECK(near(Matrix4x4::rotation(concatenate(a, b)), Matrix4x4::rotation(a) * Matrix4x4::rotation(b)));
        CHECK(near(rotate(rotate(v, a), inverse(a)), v));
    }
}

TEST_CASE("Euler angles match axis rotations in roll, pitch, yaw order")
{
    const float yaw = 0.7f;
    const float pitch = -0.3f;
    const float roll = 1.1f;
    const Quaternion euler = Quaternion::fromYawPitchRoll(yaw, pitch, roll);
    const Quaternion composed = concatenate(
        concatenate(Quaternion::fromAxisAngle({0, 0, 1}, roll), Quaternion::fromAxisAngle({1, 0, 0}, pitch)),
        Quaternion::fromAxisAngle({0, 1, 0}, yaw));
    const Vector3 v{0.2f, -0.5f, 0.9f};
    CHECK(near(rotate(v, euler), rotate(v, composed)));
}

TEST_CASE("slerp runs from one rotation to the other along the shorter arc")
{
    const Quaternion a = Quaternion::fromAxisAngle({0, 1, 0}, 0.2f);
    const Quaternion b = Quaternion::fromAxisAngle({0, 1, 0}, 1.4f);
    const Vector3 forward{0, 0, 1};
    CHECK(near(rotate(forward, slerp(a, b, 0.0f)), rotate(forward, a)));
    CHECK(near(rotate(forward, slerp(a, b, 1.0f)), rotate(forward, b)));
    CHECK(near(rotate(forward, slerp(a, b, 0.5f)), rotate(forward, Quaternion::fromAxisAngle({0, 1, 0}, 0.8f))));
    // -b is the same rotation as b; slerp takes the short way either way.
    const Quaternion negated{-b.x, -b.y, -b.z, -b.w};
    CHECK(near(rotate(forward, slerp(a, negated, 0.5f)), rotate(forward, slerp(a, b, 0.5f))));
}

TEST_CASE("Projections map near to depth 0 and far to depth 1")
{
    const Matrix4x4 perspective = Matrix4x4::perspectiveFieldOfView(pi / 3, 16.0f / 9.0f, 0.1f, 100.0f);
    const Vector4 nearPoint = transform({0, 0, 0.1f, 1}, perspective);
    const Vector4 farPoint = transform({0, 0, 100.0f, 1}, perspective);
    CHECK(near(nearPoint.z / nearPoint.w, 0.0f));
    CHECK(near(farPoint.z / farPoint.w, 1.0f));
    // The top of the view at distance 1 lands on clip-space +Y = 1 (up).
    const Vector4 top = transform({0, std::tan(pi / 6), 1, 1}, perspective);
    CHECK(near(top.y / top.w, 1.0f));

    const Matrix4x4 ortho = Matrix4x4::orthographic(20, 10, 1, 11);
    CHECK(near(transformPoint({10, 5, 1}, ortho), {1, 1, 0}));
    CHECK(near(transformPoint({-10, -5, 11}, ortho), {-1, -1, 1}));
}

TEST_CASE("lookAt puts the target straight ahead")
{
    const Vector3 eye{3, 2, -5};
    const Vector3 target{-1, 0, 4};
    const Matrix4x4 view = Matrix4x4::lookAt(eye, target, {0, 1, 0});
    CHECK(near(transformPoint(eye, view), {0, 0, 0}));
    const Vector3 ahead = transformPoint(target, view);
    CHECK(near(ahead.x, 0.0f));
    CHECK(near(ahead.y, 0.0f));
    CHECK(near(ahead.z, distance(eye, target)));
}
