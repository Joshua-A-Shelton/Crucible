#pragma once

// Crucible's register compute types (ADR 0024): SimdVector holds four floats
// in one 128-bit SIMD register, for math on one thing at a time; SimdMatrix
// is four of them as rows. Load from the storage types (crucible/Math.h),
// compute, store back. Same conventions as Math.h, and the same results to
// within rounding (fused multiply-adds round once). Names are placeholders
// (docs/naming.md).
//
// Each operation has an SSE path (x64; VEX-encoded, with FMA, on x86-64-v3),
// a NEON path (ARM64), and a scalar path, chosen at compile time. Defining
// CRUCIBLE_SIMD_SCALAR (CMake: CRUCIBLE_FORCE_SCALAR_SIMD) forces the scalar
// path, to test it.

#include <crucible/Math.h>

#include <cmath>

#if defined(CRUCIBLE_SIMD_SCALAR)
#define CRUCIBLE_SIMD_PATH_SCALAR 1
#elif defined(__x86_64__) || defined(_M_X64)
#define CRUCIBLE_SIMD_PATH_SSE 1
#if defined(__AVX2__)
#define CRUCIBLE_SIMD_PATH_AVX2 1
#endif
#include <immintrin.h>
#elif defined(__aarch64__) || defined(_M_ARM64)
#define CRUCIBLE_SIMD_PATH_NEON 1
#include <arm_neon.h>
#else
#define CRUCIBLE_SIMD_PATH_SCALAR 1
#endif

namespace crucible {

// The path this build uses, for logs and benchmarks.
inline constexpr const char* simdPathName =
#if defined(CRUCIBLE_SIMD_PATH_AVX2)
    "AVX2";
#elif defined(CRUCIBLE_SIMD_PATH_SSE)
    "SSE";
#elif defined(CRUCIBLE_SIMD_PATH_NEON)
    "NEON";
#else
    "scalar";
#endif

struct alignas(16) SimdVector
{
#if defined(CRUCIBLE_SIMD_PATH_SSE)
    __m128 v;
#elif defined(CRUCIBLE_SIMD_PATH_NEON)
    float32x4_t v;
#else
    float v[4];
#endif

    static SimdVector set(float x, float y, float z, float w)
    {
#if defined(CRUCIBLE_SIMD_PATH_SSE)
        return {_mm_set_ps(w, z, y, x)};
#elif defined(CRUCIBLE_SIMD_PATH_NEON)
        const float values[4] = {x, y, z, w};
        return {vld1q_f32(values)};
#else
        return {{x, y, z, w}};
#endif
    }

    static SimdVector splat(float value)
    {
#if defined(CRUCIBLE_SIMD_PATH_SSE)
        return {_mm_set1_ps(value)};
#elif defined(CRUCIBLE_SIMD_PATH_NEON)
        return {vdupq_n_f32(value)};
#else
        return {{value, value, value, value}};
#endif
    }

    static SimdVector zero()
    {
        return splat(0.0f);
    }

    // The storage types aren't 16-byte aligned, so loads and stores are
    // unaligned; on current CPUs that costs nothing when the data happens to
    // be aligned.
    static SimdVector load(const Vector4& value)
    {
#if defined(CRUCIBLE_SIMD_PATH_SSE)
        return {_mm_loadu_ps(&value.x)};
#elif defined(CRUCIBLE_SIMD_PATH_NEON)
        return {vld1q_f32(&value.x)};
#else
        return {{value.x, value.y, value.z, value.w}};
#endif
    }

    static SimdVector load(const Quaternion& value)
    {
        return load(reinterpret_cast<const Vector4&>(value));
    }

    // A Vector3 is 12 bytes; reading 16 could cross into memory that isn't
    // there, so it's assembled from its three floats.
    static SimdVector load(const Vector3& value, float w = 0.0f)
    {
        return set(value.x, value.y, value.z, w);
    }

    float lane(int index) const
    {
        alignas(16) float values[4];
        store(values);
        return values[index];
    }

    Vector4 toVector4() const
    {
        Vector4 result;
        store(&result.x);
        return result;
    }

    Quaternion toQuaternion() const
    {
        const Vector4 r = toVector4();
        return {r.x, r.y, r.z, r.w};
    }

    Vector3 toVector3() const
    {
        const Vector4 r = toVector4();
        return {r.x, r.y, r.z};
    }

    void store(float* destination) const
    {
#if defined(CRUCIBLE_SIMD_PATH_SSE)
        _mm_storeu_ps(destination, v);
#elif defined(CRUCIBLE_SIMD_PATH_NEON)
        vst1q_f32(destination, v);
#else
        for (int i = 0; i < 4; ++i)
        {
            destination[i] = v[i];
        }
#endif
    }
};

// Lanes rearranged: result lane k is v's lane Ik.
template <int I0, int I1, int I2, int I3>
inline SimdVector shuffle(SimdVector a)
{
#if defined(CRUCIBLE_SIMD_PATH_SSE)
    return {_mm_shuffle_ps(a.v, a.v, _MM_SHUFFLE(I3, I2, I1, I0))};
#elif defined(CRUCIBLE_SIMD_PATH_NEON)
    alignas(16) float values[4];
    vst1q_f32(values, a.v);
    const float result[4] = {values[I0], values[I1], values[I2], values[I3]};
    return {vld1q_f32(result)};
#else
    return {{a.v[I0], a.v[I1], a.v[I2], a.v[I3]}};
#endif
}

// Every lane set to v's lane I.
template <int I>
inline SimdVector broadcast(SimdVector a)
{
    return shuffle<I, I, I, I>(a);
}

#if defined(CRUCIBLE_SIMD_PATH_SCALAR)
#define CRUCIBLE_SIMD_LANEWISE(expression)                                                                             \
    SimdVector r;                                                                                                      \
    for (int i = 0; i < 4; ++i)                                                                                        \
    {                                                                                                                  \
        r.v[i] = expression;                                                                                           \
    }                                                                                                                  \
    return r
#endif

inline SimdVector operator+(SimdVector a, SimdVector b)
{
#if defined(CRUCIBLE_SIMD_PATH_SSE)
    return {_mm_add_ps(a.v, b.v)};
#elif defined(CRUCIBLE_SIMD_PATH_NEON)
    return {vaddq_f32(a.v, b.v)};
#else
    CRUCIBLE_SIMD_LANEWISE(a.v[i] + b.v[i]);
#endif
}

inline SimdVector operator-(SimdVector a, SimdVector b)
{
#if defined(CRUCIBLE_SIMD_PATH_SSE)
    return {_mm_sub_ps(a.v, b.v)};
#elif defined(CRUCIBLE_SIMD_PATH_NEON)
    return {vsubq_f32(a.v, b.v)};
#else
    CRUCIBLE_SIMD_LANEWISE(a.v[i] - b.v[i]);
#endif
}

inline SimdVector operator*(SimdVector a, SimdVector b)
{
#if defined(CRUCIBLE_SIMD_PATH_SSE)
    return {_mm_mul_ps(a.v, b.v)};
#elif defined(CRUCIBLE_SIMD_PATH_NEON)
    return {vmulq_f32(a.v, b.v)};
#else
    CRUCIBLE_SIMD_LANEWISE(a.v[i] * b.v[i]);
#endif
}

inline SimdVector operator/(SimdVector a, SimdVector b)
{
#if defined(CRUCIBLE_SIMD_PATH_SSE)
    return {_mm_div_ps(a.v, b.v)};
#elif defined(CRUCIBLE_SIMD_PATH_NEON)
    return {vdivq_f32(a.v, b.v)};
#else
    CRUCIBLE_SIMD_LANEWISE(a.v[i] / b.v[i]);
#endif
}

inline SimdVector operator*(SimdVector a, float s)
{
    return a * SimdVector::splat(s);
}

inline SimdVector operator-(SimdVector a)
{
    return SimdVector::zero() - a;
}

// a * b + c, fused (one rounding) where the hardware has it.
inline SimdVector multiplyAdd(SimdVector a, SimdVector b, SimdVector c)
{
#if defined(CRUCIBLE_SIMD_PATH_AVX2)
    return {_mm_fmadd_ps(a.v, b.v, c.v)};
#elif defined(CRUCIBLE_SIMD_PATH_SSE)
    return {_mm_add_ps(_mm_mul_ps(a.v, b.v), c.v)};
#elif defined(CRUCIBLE_SIMD_PATH_NEON)
    return {vfmaq_f32(c.v, a.v, b.v)};
#else
    CRUCIBLE_SIMD_LANEWISE(a.v[i] * b.v[i] + c.v[i]);
#endif
}

inline SimdVector min(SimdVector a, SimdVector b)
{
#if defined(CRUCIBLE_SIMD_PATH_SSE)
    return {_mm_min_ps(a.v, b.v)};
#elif defined(CRUCIBLE_SIMD_PATH_NEON)
    return {vminq_f32(a.v, b.v)};
#else
    CRUCIBLE_SIMD_LANEWISE(a.v[i] < b.v[i] ? a.v[i] : b.v[i]);
#endif
}

inline SimdVector max(SimdVector a, SimdVector b)
{
#if defined(CRUCIBLE_SIMD_PATH_SSE)
    return {_mm_max_ps(a.v, b.v)};
#elif defined(CRUCIBLE_SIMD_PATH_NEON)
    return {vmaxq_f32(a.v, b.v)};
#else
    CRUCIBLE_SIMD_LANEWISE(a.v[i] > b.v[i] ? a.v[i] : b.v[i]);
#endif
}

inline SimdVector sqrt(SimdVector a)
{
#if defined(CRUCIBLE_SIMD_PATH_SSE)
    return {_mm_sqrt_ps(a.v)};
#elif defined(CRUCIBLE_SIMD_PATH_NEON)
    return {vsqrtq_f32(a.v)};
#else
    CRUCIBLE_SIMD_LANEWISE(std::sqrt(a.v[i]));
#endif
}

#if defined(CRUCIBLE_SIMD_PATH_SCALAR)
#undef CRUCIBLE_SIMD_LANEWISE
#endif

// The sum of all four lanes, in every lane.
inline SimdVector horizontalSum(SimdVector a)
{
    const SimdVector pairs = a + shuffle<1, 0, 3, 2>(a);
    return pairs + shuffle<2, 3, 0, 1>(pairs);
}

inline float dot4(SimdVector a, SimdVector b)
{
    return horizontalSum(a * b).lane(0);
}

// Ignores w.
inline float dot3(SimdVector a, SimdVector b)
{
    const SimdVector product = a * b;
    return product.lane(0) + product.lane(1) + product.lane(2);
}

// The cross product of the xyz parts; w becomes 0.
inline SimdVector cross(SimdVector a, SimdVector b)
{
    // a.yzx * b.zxy - a.zxy * b.yzx; the w lanes cancel.
    return shuffle<1, 2, 0, 3>(a) * shuffle<2, 0, 1, 3>(b) - shuffle<2, 0, 1, 3>(a) * shuffle<1, 2, 0, 3>(b);
}

inline float length3(SimdVector a)
{
    return std::sqrt(dot3(a, a));
}

// The xyz part scaled to unit length; w is scaled too.
inline SimdVector normalize3(SimdVector a)
{
    return a * (1.0f / length3(a));
}

// The Hamilton product, as Math.h's Quaternion operator* (with row vectors,
// a * b means "b, then a"). Each line adds one term of all four components:
// a.w * b, a.x * (b.w, -b.z, b.y, -b.x), a.y * (b.z, b.w, -b.x, -b.y), and
// a.z * (-b.y, b.x, b.w, -b.z).
inline SimdVector multiplyQuaternions(SimdVector a, SimdVector b)
{
    SimdVector result = broadcast<3>(a) * b;
    result = multiplyAdd(broadcast<0>(a), shuffle<3, 2, 1, 0>(b) * SimdVector::set(1, -1, 1, -1), result);
    result = multiplyAdd(broadcast<1>(a), shuffle<2, 3, 0, 1>(b) * SimdVector::set(1, 1, -1, -1), result);
    result = multiplyAdd(broadcast<2>(a), shuffle<1, 0, 3, 2>(b) * SimdVector::set(-1, 1, 1, -1), result);
    return result;
}

// Rotates the xyz part of v by the unit quaternion q:
// t = 2 cross(q.xyz, v); v + q.w t + cross(q.xyz, t).
inline SimdVector rotate(SimdVector v, SimdVector q)
{
    const SimdVector t = cross(q, v) * 2.0f;
    return v + broadcast<3>(q) * t + cross(q, t);
}

struct alignas(16) SimdMatrix
{
    SimdVector rows[4];

    static SimdMatrix load(const Matrix4x4& m)
    {
        return {{SimdVector::load(m.rows[0]), SimdVector::load(m.rows[1]), SimdVector::load(m.rows[2]),
                 SimdVector::load(m.rows[3])}};
    }

    Matrix4x4 toMatrix4x4() const
    {
        return {{rows[0].toVector4(), rows[1].toVector4(), rows[2].toVector4(), rows[3].toVector4()}};
    }
};

// The row vector v times m.
inline SimdVector transform(SimdVector v, const SimdMatrix& m)
{
    SimdVector result = broadcast<0>(v) * m.rows[0];
    result = multiplyAdd(broadcast<1>(v), m.rows[1], result);
    result = multiplyAdd(broadcast<2>(v), m.rows[2], result);
    return multiplyAdd(broadcast<3>(v), m.rows[3], result);
}

// p's xyz times m with w = 1.
inline SimdVector transformPoint(SimdVector p, const SimdMatrix& m)
{
    SimdVector result = multiplyAdd(broadcast<0>(p), m.rows[0], m.rows[3]);
    result = multiplyAdd(broadcast<1>(p), m.rows[1], result);
    return multiplyAdd(broadcast<2>(p), m.rows[2], result);
}

// v's xyz times m with w = 0.
inline SimdVector transformDirection(SimdVector v, const SimdMatrix& m)
{
    SimdVector result = broadcast<0>(v) * m.rows[0];
    result = multiplyAdd(broadcast<1>(v), m.rows[1], result);
    return multiplyAdd(broadcast<2>(v), m.rows[2], result);
}

inline SimdMatrix operator*(const SimdMatrix& a, const SimdMatrix& b)
{
    return {{transform(a.rows[0], b), transform(a.rows[1], b), transform(a.rows[2], b), transform(a.rows[3], b)}};
}

} // namespace crucible
