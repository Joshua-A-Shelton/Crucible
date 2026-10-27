# SIMD and data layout

M3 step 3 built Crucible's SIMD compute types and benchmarked them. The
results surprised us twice, and both surprises are worth remembering: they
are about where speed really comes from.

## The two kinds of SIMD type

A SIMD register holds several floats and one instruction works on all of
them: 4 floats in a 128-bit register (SSE, NEON), 8 in a 256-bit one
(AVX2). There are two ways to use those lanes:

- **One thing per register** (`SimdVector`): a vector's x, y, z, w in the
  four lanes. Convenient (it's one object), but a 3D vector leaves a lane
  idle, and operations that mix components (dot products, cross products)
  need shuffles to move values between lanes.
- **One component of many things per register** (`Vector3Wide`): lane k
  holds object k's x, another register their y, another their z. This is
  the *structure of arrays* layout (SoA). Every lane does useful work and
  nothing needs shuffling: a dot product of 8 pairs is three multiplies and
  two adds, for all 8 at once.

The second kind is what makes batch work fast. The catch is that data
usually isn't stored that way.

## AoS and SoA

An array of `Vector3` is an *array of structures* (AoS): x0 y0 z0 x1 y1 z1
and so on. To use the wide types, a batch of 8 has to be *transposed* into
x0..x7, y0..y7, z0..z7, and transposed back afterwards.

## What the benchmark showed

Transforming points by a matrix and rotating vectors by quaternions, in
nanoseconds per element on an AMD machine with AVX2, with the data in cache:

| | scalar | register | wide, transposed via the stack | wide, transposed in registers | wide on SoA data |
|---|---|---|---|---|---|
| transformPoint | 0.53 | 0.42 | 0.76 | 0.26 | 0.17–0.24 |
| rotate | 0.98 | 1.59 | 1.19 | 0.46 | — |

The same benchmark built with MSVC on the Windows machine (a different CPU,
so compare within the table, not across):

| | scalar | register | wide, transposed in registers | wide on SoA data |
|---|---|---|---|---|
| transformPoint | 2.88 | 1.97 | 0.63 | 0.59 |
| rotate | 4.99 | 3.10 | 1.06 | — |

**Surprise 1: "scalar" code is already vectorized.** Built for x86-64-v3,
the compiler turns a plain loop over `Vector3`s into AVX2 code by itself.
The baseline to beat isn't naive. Under MSVC the wide types win by 4.6×
instead of 2×; the timings suggest MSVC doesn't vectorize that loop the way
GCC does (not yet checked in the disassembly). Either way, hand-written SIMD
gives the same speed whichever compiler builds it.

**Surprise 2: the transpose can cost more than the math.** The first wide
version copied each batch through small arrays on the stack. The math on 8
points is about ten fused multiply-adds; the copying was far more, so the
"fast" version lost to plain scalar code. Transposing in registers (three
loads and five shuffles for 8 `Vector3`s, a known Intel technique) made the
same math 2× faster than scalar. On data that is already SoA there is
nothing to transpose at all.

**Also: in streaming runs, memory sets the speed.** A million `Vector3`s is
12 MB in and 12 MB out. At about half a nanosecond per element that is near
this machine's memory bandwidth, so no arithmetic can help much; the fair
comparison of the math is with data that fits in cache.

**And a benchmarking trap:** the first cache-resident numbers said 0.009
ns per element, a few hundredths of a cycle. The compiler had noticed that
repeating the same loop into the same output was pointless and ran it once.
A compiler barrier between repeats (`clobberMemory`) fixed it. Numbers that
seem too good usually are.

## What it means for the engine

- Use the wide types for batches, and the register types for one-off math.
- **Data layout decides more than instruction choice.** How the ECS stores
  components (step 6) determines whether systems pay for transposes. That's
  an input to decision E: storing hot components so batches load as SoA, or
  keeping them AoS and paying a cheap register transpose.

Run the benchmark (release build) with:

```
crucible_native_tests --no-skip --test-suite=benchmark
```
