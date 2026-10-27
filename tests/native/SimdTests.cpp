// The SIMD compute types (ADR 0024) against the scalar math in Math.h, on
// whichever path this build uses (SimdTests run once per build: the default,
// CRUCIBLE_FORCE_SCALAR_SIMD, and CRUCIBLE_X86_LEVEL=v2 builds cover the
// rest). The "benchmark" suite is run by hand:
//     crucible_native_tests --no-skip --test-suite=benchmark

#include <crucible/Math.h>
#include <crucible/Simd.h>
#include <crucible/SimdWide.h>

#include <doctest/doctest.h>

#include <chrono>
#if defined(_MSC_VER) && !defined(__clang__)
#include <intrin.h>
#endif
#include <cmath>
#include <random>
#include <string>
#include <vector>

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

bool near(Vector4 a, Vector4 b)
{
    return near(a.x, b.x) && near(a.y, b.y) && near(a.z, b.z) && near(a.w, b.w);
}

bool near(Quaternion a, Quaternion b)
{
    return near(Vector4{a.x, a.y, a.z, a.w}, Vector4{b.x, b.y, b.z, b.w});
}

bool near(const Matrix4x4& a, const Matrix4x4& b)
{
    return near(a.rows[0], b.rows[0]) && near(a.rows[1], b.rows[1]) && near(a.rows[2], b.rows[2]) &&
           near(a.rows[3], b.rows[3]);
}

struct Random
{
    std::minstd_rand engine{4321};

    float value(float low = -1.0f, float high = 1.0f)
    {
        return std::uniform_real_distribution<float>(low, high)(engine);
    }

    Vector3 vector3()
    {
        return {value(), value(), value()};
    }

    Vector4 vector4()
    {
        return {value(), value(), value(), value()};
    }

    Quaternion rotation()
    {
        return Quaternion::fromAxisAngle(normalize(vector3()), value(-3.0f, 3.0f));
    }

    Matrix4x4 matrix()
    {
        return {{vector4(), vector4(), vector4(), vector4()}};
    }
};

} // namespace

TEST_CASE("SimdVector arithmetic matches the scalar math")
{
    MESSAGE("SIMD path: " << std::string(simdPathName) << ", wide lanes: " << wideLanes);
    Random random;
    for (int i = 0; i < 200; ++i)
    {
        const Vector4 a = random.vector4();
        const Vector4 b = random.vector4();
        const Vector4 c = random.vector4();
        const SimdVector va = SimdVector::load(a);
        const SimdVector vb = SimdVector::load(b);
        CHECK(near((va + vb).toVector4(), a + b));
        CHECK(near((va - vb).toVector4(), a - b));
        CHECK(near((va * vb).toVector4(), a * b));
        CHECK(near((va * 3.0f).toVector4(), a * 3.0f));
        CHECK(near(multiplyAdd(va, vb, SimdVector::load(c)).toVector4(), a * b + c));
        CHECK(near(dot4(va, vb), dot(a, b)));

        const Vector3 p = random.vector3();
        const Vector3 q = random.vector3();
        CHECK(near(dot3(SimdVector::load(p), SimdVector::load(q)), dot(p, q)));
        CHECK(near(cross(SimdVector::load(p), SimdVector::load(q)).toVector3(), cross(p, q)));
        CHECK(near(normalize3(SimdVector::load(p)).toVector3(), normalize(p)));
        CHECK(near(length3(SimdVector::load(p)), length(p)));
    }
    CHECK(SimdVector::set(1, 2, 3, 4).lane(2) == 3.0f);
    CHECK(shuffle<3, 2, 1, 0>(SimdVector::set(1, 2, 3, 4)).toVector4() == Vector4{4, 3, 2, 1});
}

TEST_CASE("SimdVector quaternion product and rotation match the scalar math")
{
    Random random;
    for (int i = 0; i < 200; ++i)
    {
        const Quaternion a = random.rotation();
        const Quaternion b = random.rotation();
        const Vector3 v = random.vector3();
        CHECK(near(multiplyQuaternions(SimdVector::load(a), SimdVector::load(b)).toQuaternion(), a * b));
        CHECK(near(rotate(SimdVector::load(v), SimdVector::load(a)).toVector3(), rotate(v, a)));
    }
}

TEST_CASE("SimdMatrix products and transforms match the scalar math")
{
    Random random;
    for (int i = 0; i < 200; ++i)
    {
        const Matrix4x4 a = random.matrix();
        const Matrix4x4 b = random.matrix();
        const SimdMatrix sa = SimdMatrix::load(a);
        CHECK(near((sa * SimdMatrix::load(b)).toMatrix4x4(), a * b));
        CHECK(sa.toMatrix4x4() == a);

        const Vector3 p = random.vector3();
        const Vector4 v = random.vector4();
        CHECK(near(transformPoint(SimdVector::load(p), sa).toVector3(), transformPoint(p, a)));
        CHECK(near(transformDirection(SimdVector::load(p), sa).toVector3(), transformDirection(p, a)));
        CHECK(near(transform(SimdVector::load(v), sa).toVector4(), transform(v, a)));
    }
}

TEST_CASE("Wide types compute each lane as the scalar math does")
{
    Random random;
    for (int batch = 0; batch < 50; ++batch)
    {
        Vector3 a[wideLanes];
        Vector3 b[wideLanes];
        Quaternion q[wideLanes];
        Quaternion r[wideLanes];
        for (int i = 0; i < wideLanes; ++i)
        {
            a[i] = random.vector3();
            b[i] = random.vector3();
            q[i] = random.rotation();
            r[i] = random.rotation();
        }
        const Matrix4x4 m = random.matrix();
        const Vector3Wide wa = Vector3Wide::load(a);
        const Vector3Wide wb = Vector3Wide::load(b);
        const QuaternionWide wq = QuaternionWide::load(q);

        Vector3 sum[wideLanes], crossed[wideLanes], normalized[wideLanes], rotated[wideLanes], moved[wideLanes];
        float dots[wideLanes];
        Quaternion products[wideLanes];
        (wa + wb).store(sum);
        cross(wa, wb).store(crossed);
        normalize(wa).store(normalized);
        rotate(wa, wq).store(rotated);
        transformPoint(wa, m).store(moved);
        dot(wa, wb).store(dots);
        (wq * QuaternionWide::load(r)).store(products);
        for (int i = 0; i < wideLanes; ++i)
        {
            CHECK(near(sum[i], a[i] + b[i]));
            CHECK(near(crossed[i], cross(a[i], b[i])));
            CHECK(near(normalized[i], normalize(a[i])));
            CHECK(near(rotated[i], rotate(a[i], q[i])));
            CHECK(near(moved[i], transformPoint(a[i], m)));
            CHECK(near(dots[i], dot(a[i], b[i])));
            CHECK(near(products[i], q[i] * r[i]));
        }
    }
}

TEST_CASE("A partial batch loads neutral lanes and stores only its count")
{
    const Vector3 source[3] = {{1, 2, 3}, {4, 5, 6}, {7, 8, 9}};
    const Vector3Wide wide = Vector3Wide::load(source, 3);
    float xs[wideLanes];
    wide.x.store(xs);
    CHECK(xs[2] == 7.0f);
    CHECK(xs[3] == 0.0f); // past the count

    const Quaternion rotations[1] = {Quaternion::fromAxisAngle({0, 1, 0}, 1.0f)};
    float ws[wideLanes];
    QuaternionWide::load(rotations, 1).w.store(ws);
    CHECK(ws[wideLanes - 1] == 1.0f); // the identity past the count

    Vector3 destination[wideLanes];
    for (Vector3& d : destination)
    {
        d = {-1, -1, -1};
    }
    wide.store(destination, 3);
    CHECK(destination[2] == Vector3{7, 8, 9});
    CHECK(destination[3] == Vector3{-1, -1, -1}); // untouched
}

namespace {

// Tells the compiler the memory behind `p` may have been read and changed,
// so repeated work can't be merged or skipped. Without it, repeating the
// same loop into the same output is optimized down to one run.
inline void clobberMemory(void* p)
{
#if defined(_MSC_VER) && !defined(__clang__)
    static void* volatile sink;
    sink = p;
    _ReadWriteBarrier();
#else
    asm volatile("" : : "g"(p) : "memory");
#endif
}

// Nanoseconds per element of `work` over `count` elements, best of five runs.
template <class Work>
double timePerElement(size_t count, Work&& work)
{
    double best = 1e30;
    for (int run = 0; run < 5; ++run)
    {
        const auto start = std::chrono::steady_clock::now();
        work();
        const auto end = std::chrono::steady_clock::now();
        best = std::min(best, std::chrono::duration<double, std::nano>(end - start).count() / count);
    }
    return best;
}

} // namespace

TEST_SUITE("benchmark" * doctest::skip())
{
    // Runs each variant over `count` elements, `repeats` times. Streaming
    // (a million elements) is bound by memory bandwidth; cache-resident (8192,
    // which fit in L2) shows the arithmetic.
    void benchmark(size_t count, int repeats)
    {
        Random random;
        std::vector<Vector3> points(count);
        std::vector<Quaternion> rotations(count);
        // The same points as separate x, y, z arrays: SoA storage, so the wide
        // path needs no transposing.
        std::vector<float> xs(count), ys(count), zs(count);
        for (size_t i = 0; i < count; ++i)
        {
            points[i] = random.vector3();
            rotations[i] = random.rotation();
            xs[i] = points[i].x;
            ys[i] = points[i].y;
            zs[i] = points[i].z;
        }
        const Matrix4x4 m = Matrix4x4::rotation(random.rotation()) * Matrix4x4::translation({1, 2, 3});
        std::vector<Vector3> out(count);
        std::vector<float> outX(count), outY(count), outZ(count);
        const size_t total = count * static_cast<size_t>(repeats);
        auto repeat = [&](auto&& body) {
            return timePerElement(total, [&] {
                for (int r = 0; r < repeats; ++r)
                {
                    body();
                    clobberMemory(out.data());
                    clobberMemory(outX.data());
                }
            });
        };

        const SimdMatrix sm = SimdMatrix::load(m);
        const double transformScalar = repeat([&] {
            for (size_t i = 0; i < count; ++i)
            {
                out[i] = transformPoint(points[i], m);
            }
        });
        const double transformRegister = repeat([&] {
            for (size_t i = 0; i < count; ++i)
            {
                out[i] = transformPoint(SimdVector::load(points[i]), sm).toVector3();
            }
        });
        const double transformWide = repeat([&] {
            for (size_t i = 0; i < count; i += wideLanes)
            {
                transformPoint(Vector3Wide::load(&points[i]), m).store(&out[i]);
            }
        });
        const double transformSoa = repeat([&] {
            for (size_t i = 0; i < count; i += wideLanes)
            {
                const Vector3Wide p{FloatWide::load(&xs[i]), FloatWide::load(&ys[i]), FloatWide::load(&zs[i])};
                const Vector3Wide r = transformPoint(p, m);
                r.x.store(&outX[i]);
                r.y.store(&outY[i]);
                r.z.store(&outZ[i]);
            }
        });

        const double rotateScalar = repeat([&] {
            for (size_t i = 0; i < count; ++i)
            {
                out[i] = rotate(points[i], rotations[i]);
            }
        });
        const double rotateRegister = repeat([&] {
            for (size_t i = 0; i < count; ++i)
            {
                out[i] = rotate(SimdVector::load(points[i]), SimdVector::load(rotations[i])).toVector3();
            }
        });
        const double rotateWide = repeat([&] {
            for (size_t i = 0; i < count; i += wideLanes)
            {
                rotate(Vector3Wide::load(&points[i]), QuaternionWide::load(&rotations[i])).store(&out[i]);
            }
        });

        MESSAGE(count << " elements x " << repeats << ", ns per element (best of 5), path " << std::string(simdPathName)
                      << " (" << wideLanes << " lanes):");
        MESSAGE("  transformPoint: scalar " << transformScalar << ", register " << transformRegister << ", wide "
                                            << transformWide << ", wide on SoA data " << transformSoa);
        MESSAGE("  rotate:         scalar " << rotateScalar << ", register " << rotateRegister << ", wide "
                                            << rotateWide);
        CHECK(out[0].x == out[0].x); // keeps the work from being optimized away
        CHECK(outX[0] == outX[0]);
    }

    TEST_CASE("Streaming: a million vectors, bound by memory bandwidth")
    {
        benchmark(1'000'000, 1);
    }

    TEST_CASE("Cache-resident: 8192 vectors, 128 times")
    {
        benchmark(8192, 128);
    }
}
