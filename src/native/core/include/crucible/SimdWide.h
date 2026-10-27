#pragma once

// Crucible's wide compute types (ADR 0024): one component of `wideLanes`
// objects side by side (structure of arrays), so each instruction works on
// that many objects at once. wideLanes is 8 with AVX2 (256-bit registers) and
// 4 with SSE, NEON, and the scalar path. For batches over ECS chunks: load a
// run of storage types (crucible/Math.h), compute lane-wise, store back.
// Results match Math.h to within rounding. Names are placeholders
// (docs/naming.md).

#include <crucible/Math.h>
#include <crucible/Simd.h>

#include <cmath>

namespace crucible {

#if defined(CRUCIBLE_SIMD_PATH_AVX2)
inline constexpr int wideLanes = 8;
#else
inline constexpr int wideLanes = 4;
#endif

struct alignas(wideLanes * 4) FloatWide
{
#if defined(CRUCIBLE_SIMD_PATH_AVX2)
    __m256 v;
#elif defined(CRUCIBLE_SIMD_PATH_SSE)
    __m128 v;
#elif defined(CRUCIBLE_SIMD_PATH_NEON)
    float32x4_t v;
#else
    float v[4];
#endif

    static FloatWide splat(float value)
    {
#if defined(CRUCIBLE_SIMD_PATH_AVX2)
        return {_mm256_set1_ps(value)};
#elif defined(CRUCIBLE_SIMD_PATH_SSE)
        return {_mm_set1_ps(value)};
#elif defined(CRUCIBLE_SIMD_PATH_NEON)
        return {vdupq_n_f32(value)};
#else
        return {{value, value, value, value}};
#endif
    }

    // wideLanes floats; no alignment needed.
    static FloatWide load(const float* source)
    {
#if defined(CRUCIBLE_SIMD_PATH_AVX2)
        return {_mm256_loadu_ps(source)};
#elif defined(CRUCIBLE_SIMD_PATH_SSE)
        return {_mm_loadu_ps(source)};
#elif defined(CRUCIBLE_SIMD_PATH_NEON)
        return {vld1q_f32(source)};
#else
        return {{source[0], source[1], source[2], source[3]}};
#endif
    }

    void store(float* destination) const
    {
#if defined(CRUCIBLE_SIMD_PATH_AVX2)
        _mm256_storeu_ps(destination, v);
#elif defined(CRUCIBLE_SIMD_PATH_SSE)
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

#if defined(CRUCIBLE_SIMD_PATH_AVX2)
#define CRUCIBLE_WIDE_OPERATION(avx, sse, neon, scalar)                                                                \
    return                                                                                                             \
    {                                                                                                                  \
        avx                                                                                                            \
    }
#elif defined(CRUCIBLE_SIMD_PATH_SSE)
#define CRUCIBLE_WIDE_OPERATION(avx, sse, neon, scalar)                                                                \
    return                                                                                                             \
    {                                                                                                                  \
        sse                                                                                                            \
    }
#elif defined(CRUCIBLE_SIMD_PATH_NEON)
#define CRUCIBLE_WIDE_OPERATION(avx, sse, neon, scalar)                                                                \
    return                                                                                                             \
    {                                                                                                                  \
        neon                                                                                                           \
    }
#else
#define CRUCIBLE_WIDE_OPERATION(avx, sse, neon, scalar)                                                                \
    FloatWide r;                                                                                                       \
    for (int i = 0; i < 4; ++i)                                                                                        \
    {                                                                                                                  \
        r.v[i] = scalar;                                                                                               \
    }                                                                                                                  \
    return r
#endif

inline FloatWide operator+(FloatWide a, FloatWide b)
{
    CRUCIBLE_WIDE_OPERATION(_mm256_add_ps(a.v, b.v), _mm_add_ps(a.v, b.v), vaddq_f32(a.v, b.v), a.v[i] + b.v[i]);
}

inline FloatWide operator-(FloatWide a, FloatWide b)
{
    CRUCIBLE_WIDE_OPERATION(_mm256_sub_ps(a.v, b.v), _mm_sub_ps(a.v, b.v), vsubq_f32(a.v, b.v), a.v[i] - b.v[i]);
}

inline FloatWide operator*(FloatWide a, FloatWide b)
{
    CRUCIBLE_WIDE_OPERATION(_mm256_mul_ps(a.v, b.v), _mm_mul_ps(a.v, b.v), vmulq_f32(a.v, b.v), a.v[i] * b.v[i]);
}

inline FloatWide operator/(FloatWide a, FloatWide b)
{
    CRUCIBLE_WIDE_OPERATION(_mm256_div_ps(a.v, b.v), _mm_div_ps(a.v, b.v), vdivq_f32(a.v, b.v), a.v[i] / b.v[i]);
}

// a * b + c, fused where the hardware has it.
inline FloatWide multiplyAdd(FloatWide a, FloatWide b, FloatWide c)
{
    CRUCIBLE_WIDE_OPERATION(_mm256_fmadd_ps(a.v, b.v, c.v), _mm_add_ps(_mm_mul_ps(a.v, b.v), c.v),
                            vfmaq_f32(c.v, a.v, b.v), a.v[i] * b.v[i] + c.v[i]);
}

inline FloatWide sqrt(FloatWide a)
{
    CRUCIBLE_WIDE_OPERATION(_mm256_sqrt_ps(a.v), _mm_sqrt_ps(a.v), vsqrtq_f32(a.v), std::sqrt(a.v[i]));
}

inline FloatWide min(FloatWide a, FloatWide b)
{
    CRUCIBLE_WIDE_OPERATION(_mm256_min_ps(a.v, b.v), _mm_min_ps(a.v, b.v), vminq_f32(a.v, b.v),
                            a.v[i] < b.v[i] ? a.v[i] : b.v[i]);
}

inline FloatWide max(FloatWide a, FloatWide b)
{
    CRUCIBLE_WIDE_OPERATION(_mm256_max_ps(a.v, b.v), _mm_max_ps(a.v, b.v), vmaxq_f32(a.v, b.v),
                            a.v[i] > b.v[i] ? a.v[i] : b.v[i]);
}

#undef CRUCIBLE_WIDE_OPERATION

inline FloatWide operator*(FloatWide a, float s)
{
    return a * FloatWide::splat(s);
}

inline FloatWide operator-(FloatWide a)
{
    return FloatWide::splat(0.0f) - a;
}

struct Vector3Wide
{
    FloatWide x;
    FloatWide y;
    FloatWide z;

    static Vector3Wide splat(Vector3 value)
    {
        return {FloatWide::splat(value.x), FloatWide::splat(value.y), FloatWide::splat(value.z)};
    }

    // `count` (up to wideLanes) consecutive Vector3s, as ECS components are
    // stored; lanes past count are zero. Transposes through the stack: simple,
    // and cheap next to the math done on the batch.
    static Vector3Wide load(const Vector3* source, int count = wideLanes)
    {
#if defined(CRUCIBLE_SIMD_PATH_AVX2)
        if (count == wideLanes)
        {
            // A full batch transposes in registers (Intel's AoS-to-SoA for
            // 3-vectors): each 128-bit half holds 4 of the 8 vectors, and
            // three in-lane shuffles per component pick them out in order.
            const float* f = &source[0].x;
            const __m256 m03 = _mm256_insertf128_ps(_mm256_castps128_ps256(_mm_loadu_ps(f)), _mm_loadu_ps(f + 12), 1);
            const __m256 m14 =
                _mm256_insertf128_ps(_mm256_castps128_ps256(_mm_loadu_ps(f + 4)), _mm_loadu_ps(f + 16), 1);
            const __m256 m25 =
                _mm256_insertf128_ps(_mm256_castps128_ps256(_mm_loadu_ps(f + 8)), _mm_loadu_ps(f + 20), 1);
            const __m256 xy = _mm256_shuffle_ps(m14, m25, _MM_SHUFFLE(2, 1, 3, 2));
            const __m256 yz = _mm256_shuffle_ps(m03, m14, _MM_SHUFFLE(1, 0, 2, 1));
            return {{_mm256_shuffle_ps(m03, xy, _MM_SHUFFLE(2, 0, 3, 0))},
                    {_mm256_shuffle_ps(yz, xy, _MM_SHUFFLE(3, 1, 2, 0))},
                    {_mm256_shuffle_ps(yz, m25, _MM_SHUFFLE(3, 0, 3, 1))}};
        }
#endif
        alignas(32) float xs[wideLanes] = {};
        alignas(32) float ys[wideLanes] = {};
        alignas(32) float zs[wideLanes] = {};
        for (int i = 0; i < count; ++i)
        {
            xs[i] = source[i].x;
            ys[i] = source[i].y;
            zs[i] = source[i].z;
        }
        return {FloatWide::load(xs), FloatWide::load(ys), FloatWide::load(zs)};
    }

    // Writes the first `count` lanes back as consecutive Vector3s.
    void store(Vector3* destination, int count = wideLanes) const
    {
#if defined(CRUCIBLE_SIMD_PATH_AVX2)
        if (count == wideLanes)
        {
            // The load's transpose, reversed.
            const __m256 rxy = _mm256_shuffle_ps(x.v, y.v, _MM_SHUFFLE(2, 0, 2, 0));
            const __m256 ryz = _mm256_shuffle_ps(y.v, z.v, _MM_SHUFFLE(3, 1, 3, 1));
            const __m256 rzx = _mm256_shuffle_ps(z.v, x.v, _MM_SHUFFLE(3, 1, 2, 0));
            const __m256 r03 = _mm256_shuffle_ps(rxy, rzx, _MM_SHUFFLE(2, 0, 2, 0));
            const __m256 r14 = _mm256_shuffle_ps(ryz, rxy, _MM_SHUFFLE(3, 1, 2, 0));
            const __m256 r25 = _mm256_shuffle_ps(rzx, ryz, _MM_SHUFFLE(3, 1, 3, 1));
            float* f = &destination[0].x;
            _mm_storeu_ps(f, _mm256_castps256_ps128(r03));
            _mm_storeu_ps(f + 4, _mm256_castps256_ps128(r14));
            _mm_storeu_ps(f + 8, _mm256_castps256_ps128(r25));
            _mm_storeu_ps(f + 12, _mm256_extractf128_ps(r03, 1));
            _mm_storeu_ps(f + 16, _mm256_extractf128_ps(r14, 1));
            _mm_storeu_ps(f + 20, _mm256_extractf128_ps(r25, 1));
            return;
        }
#endif
        alignas(32) float xs[wideLanes];
        alignas(32) float ys[wideLanes];
        alignas(32) float zs[wideLanes];
        x.store(xs);
        y.store(ys);
        z.store(zs);
        for (int i = 0; i < count; ++i)
        {
            destination[i] = {xs[i], ys[i], zs[i]};
        }
    }
};

inline Vector3Wide operator+(const Vector3Wide& a, const Vector3Wide& b)
{
    return {a.x + b.x, a.y + b.y, a.z + b.z};
}

inline Vector3Wide operator-(const Vector3Wide& a, const Vector3Wide& b)
{
    return {a.x - b.x, a.y - b.y, a.z - b.z};
}

inline Vector3Wide operator*(const Vector3Wide& a, FloatWide s)
{
    return {a.x * s, a.y * s, a.z * s};
}

inline Vector3Wide operator*(const Vector3Wide& a, float s)
{
    return a * FloatWide::splat(s);
}

inline FloatWide dot(const Vector3Wide& a, const Vector3Wide& b)
{
    return multiplyAdd(a.x, b.x, multiplyAdd(a.y, b.y, a.z * b.z));
}

inline Vector3Wide cross(const Vector3Wide& a, const Vector3Wide& b)
{
    return {a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x};
}

inline FloatWide length(const Vector3Wide& a)
{
    return sqrt(dot(a, a));
}

inline Vector3Wide normalize(const Vector3Wide& a)
{
    return a * (FloatWide::splat(1.0f) / length(a));
}

inline Vector3Wide lerp(const Vector3Wide& a, const Vector3Wide& b, FloatWide t)
{
    return a + (b - a) * t;
}

// Each lane's point times m, with w = 1.
inline Vector3Wide transformPoint(const Vector3Wide& p, const Matrix4x4& m)
{
    Vector3Wide result;
    FloatWide* components[3] = {&result.x, &result.y, &result.z};
    for (int c = 0; c < 3; ++c)
    {
        *components[c] =
            multiplyAdd(p.x, FloatWide::splat(m.rows[0][c]),
                        multiplyAdd(p.y, FloatWide::splat(m.rows[1][c]),
                                    multiplyAdd(p.z, FloatWide::splat(m.rows[2][c]), FloatWide::splat(m.rows[3][c]))));
    }
    return result;
}

struct QuaternionWide
{
    FloatWide x;
    FloatWide y;
    FloatWide z;
    FloatWide w;

    static QuaternionWide splat(Quaternion value)
    {
        return {FloatWide::splat(value.x), FloatWide::splat(value.y), FloatWide::splat(value.z),
                FloatWide::splat(value.w)};
    }

    // `count` consecutive Quaternions; lanes past count are the identity.
    static QuaternionWide load(const Quaternion* source, int count = wideLanes)
    {
#if defined(CRUCIBLE_SIMD_PATH_AVX2)
        if (count == wideLanes)
        {
            // A 4x4 transpose in each 128-bit half: row k holds quaternions k
            // and k + 4.
            const float* f = &source[0].x;
            auto row = [f](int k) {
                return _mm256_insertf128_ps(_mm256_castps128_ps256(_mm_loadu_ps(f + 4 * k)),
                                            _mm_loadu_ps(f + 4 * (k + 4)), 1);
            };
            const __m256 r0 = row(0), r1 = row(1), r2 = row(2), r3 = row(3);
            const __m256 t0 = _mm256_unpacklo_ps(r0, r1); // x0 x1 y0 y1
            const __m256 t1 = _mm256_unpacklo_ps(r2, r3); // x2 x3 y2 y3
            const __m256 t2 = _mm256_unpackhi_ps(r0, r1); // z0 z1 w0 w1
            const __m256 t3 = _mm256_unpackhi_ps(r2, r3); // z2 z3 w2 w3
            return {{_mm256_shuffle_ps(t0, t1, _MM_SHUFFLE(1, 0, 1, 0))},
                    {_mm256_shuffle_ps(t0, t1, _MM_SHUFFLE(3, 2, 3, 2))},
                    {_mm256_shuffle_ps(t2, t3, _MM_SHUFFLE(1, 0, 1, 0))},
                    {_mm256_shuffle_ps(t2, t3, _MM_SHUFFLE(3, 2, 3, 2))}};
        }
#endif
        alignas(32) float xs[wideLanes] = {};
        alignas(32) float ys[wideLanes] = {};
        alignas(32) float zs[wideLanes] = {};
        alignas(32) float ws[wideLanes];
        for (int i = 0; i < wideLanes; ++i)
        {
            ws[i] = 1.0f;
        }
        for (int i = 0; i < count; ++i)
        {
            xs[i] = source[i].x;
            ys[i] = source[i].y;
            zs[i] = source[i].z;
            ws[i] = source[i].w;
        }
        return {FloatWide::load(xs), FloatWide::load(ys), FloatWide::load(zs), FloatWide::load(ws)};
    }

    void store(Quaternion* destination, int count = wideLanes) const
    {
        alignas(32) float xs[wideLanes];
        alignas(32) float ys[wideLanes];
        alignas(32) float zs[wideLanes];
        alignas(32) float ws[wideLanes];
        x.store(xs);
        y.store(ys);
        z.store(zs);
        w.store(ws);
        for (int i = 0; i < count; ++i)
        {
            destination[i] = {xs[i], ys[i], zs[i], ws[i]};
        }
    }
};

// The Hamilton product in each lane, as Math.h's Quaternion operator*.
inline QuaternionWide operator*(const QuaternionWide& a, const QuaternionWide& b)
{
    return {multiplyAdd(a.w, b.x, multiplyAdd(a.x, b.w, a.y * b.z - a.z * b.y)),
            multiplyAdd(a.w, b.y, multiplyAdd(a.y, b.w, a.z * b.x - a.x * b.z)),
            multiplyAdd(a.w, b.z, multiplyAdd(a.z, b.w, a.x * b.y - a.y * b.x)),
            a.w * b.w - multiplyAdd(a.x, b.x, multiplyAdd(a.y, b.y, a.z * b.z))};
}

// Rotates each lane's vector by that lane's unit quaternion:
// t = 2 cross(q.xyz, v); v + q.w t + cross(q.xyz, t).
inline Vector3Wide rotate(const Vector3Wide& v, const QuaternionWide& q)
{
    const Vector3Wide axis{q.x, q.y, q.z};
    const Vector3Wide t = cross(axis, v) * 2.0f;
    return v + t * q.w + cross(axis, t);
}

} // namespace crucible
