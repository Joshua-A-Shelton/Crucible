# Storage types vs. SIMD types

A learning note for the math library decision (ADR 0024). Read this when
you're wondering why Crucible has more than one kind of vector, or which
one to use.

## The decision, in one paragraph

Crucible's math library has **two kinds of types**. **Storage types**
(`Vec3`, `Quat`, `Mat4`, ...) are plain structs that hold data in memory:
in components, in files, and across the C#/C++ boundary. They are laid out
byte for byte like C#'s `System.Numerics` types. **Compute types** live in
SIMD registers and do the math: a register vector type for general math,
and wide types that process 4 or 8 entities at once. Data is loaded from
storage types into compute types, computed on, and stored back. Every SIMD
path has a plain scalar fallback so ARM64 builds work. (Type names are
placeholders.)

## Why: the short version

1. A SIMD register holds **4 floats** (8 with AVX2). A `Vec3` has **3**.
2. If `Vec3` itself were a SIMD type, it would be **16 bytes**, not 12.
   C#'s `Vector3` is 12 bytes, and C++ and C# read the same component
   memory (ADR 0012), so the layouts must match exactly. Every component
   with a position would also waste 4 bytes.
3. The big SIMD speedups don't come from putting one vector in one
   register anyway. They come from processing **the same field of several
   entities at once**, which ECS chunks already store side by side.

So: plain types for memory, SIMD types for math.

## What SIMD is

Normally, one CPU instruction does one operation. A SIMD (single
instruction, multiple data) register holds several values, and one
instruction operates on all of them:

```
scalar:   a + b                      → 1 addition
SIMD:     [a0 a1 a2 a3]
        + [b0 b1 b2 b3]
        = [a0+b0 a1+b1 a2+b2 a3+b3]  → 4 additions, one instruction
```

| Instruction set | Register size | Floats per register | Where |
|---|---|---|---|
| SSE / SSE2 | 128 bits | 4 | Every x64 CPU |
| AVX / AVX2 | 256 bits | 8 | Most x64 CPUs since about 2013, Steam Deck included |
| NEON | 128 bits | 4 | ARM64 (must compile, not supported in v1) |

SIMD loads also prefer **16-byte-aligned** addresses, and reading 16 bytes
from a 12-byte value can run past the end of an array.

## Two ways to use SIMD

### (a) One vector per register (AoS, "array of structures")

```
register = [x y z w]     ← one position
```

Convenient, but the gains are modest. Many operations work *across* the
lanes of one register. A dot product multiplies lane by lane and then has
to add `x + y + z` together *inside* the register, which SIMD is bad at.

This is what the **register vector type** is for: general math where
convenience matters more than peak speed.

### (b) One field of several vectors per register (SoA, "structure of arrays")

```
register X = [x0 x1 x2 x3]   ← x of 4 different entities
register Y = [y0 y1 y2 y3]
register Z = [z0 z1 z2 z3]
```

Now a dot product of 4 pairs of vectors is just lane-by-lane work:

```
dot = X_a * X_b + Y_a * Y_b + Z_a * Z_b     → 4 dot products at once
```

No work inside a register, so it's close to **4× faster** (8× with AVX2).
This is where real SIMD speed comes from; Unity's Burst and Intel's ISPC
are built around it.

This is what the **wide types** are for.

## How this connects to the ECS

An archetype chunk (ADR 0023) stores each component in its own column.
Inside a chunk, entity 0's `Transform`, entity 1's `Transform`, and so on
sit one after another. A system loops over a chunk, loads 4 or 8 entities'
worth of a field into wide registers, does the math for all of them at
once, and stores the results back. Choosing archetypes already gave us
data laid out for this; the wide types are how we use it.

A sketch (pseudocode, names placeholder):

```cpp
// Move every entity by its velocity, 4 at a time.
for (size_t i = 0; i < count; i += 4)
{
    Wide3 p = loadWide3(positions + i);   // 4 positions, as X/Y/Z registers
    Wide3 v = loadWide3(velocities + i);
    storeWide3(positions + i, p + v * dt);
}
// (plus a scalar loop for the leftover entities when count isn't a multiple of 4)
```

`positions` and `velocities` are arrays of plain `Vec3` storage types.
The wide type exists only during the math.

## Which type to use

| You're... | Use |
|---|---|
| Declaring a component field, a struct that crosses to C#, or anything saved to a file | Storage type (`Vec3`, `Quat`, `Mat4`) |
| Doing a few vector operations in ordinary code | Storage types, or the register vector type if it's hot |
| Processing many entities in a system's inner loop | Wide types (SoA) |
| Writing C# | `System.Numerics`. The .NET JIT already compiles its operations to SIMD; `System.Runtime.Intrinsics` exists for explicit SIMD |

## Things to remember

- **Storage types must stay byte-identical to `System.Numerics`.** The
  binding generator's layout checks (ADR 0017) fail the build or startup
  if they ever drift.
- **Every SIMD path needs a scalar fallback.** Write the scalar version
  first; it's also the reference the SIMD versions are tested against.
- **Leftovers:** when the entity count isn't a multiple of 4 or 8, the
  last few are handled by a scalar loop (or a masked load).
- **Still open:** whether x64 builds require AVX2 (ADR 0024).
