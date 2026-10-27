# 0024 — Math library and coordinate conventions

**Status:** Accepted (2026-10-25). Amended 2026-10-26 (M2): clip space
settled. Amended again (M3): the storage types as built.

## Context

C++, C#, and shaders all read the same vectors and matrices: blittable
components in native memory (ADR 0012), struct mirrors with layout checks
(ADR 0017), and GPU uploads. A mismatch between them doesn't crash; it
silently rotates, mirrors, or garbles geometry. C#'s `System.Numerics` is
fixed: `Vector3` is 12 bytes, `Quaternion` is (x, y, z, w), and
`Matrix4x4` uses row vectors stored row-major. Slag doesn't set a shader
matrix layout, so Crucible chooses one (a Slang flag).

## Options

**Library:**

| Option | License | Notes |
|---|---|---|
| Our own | zlib | Small; shaped to the rules below |
| glm | MIT | Mirrors GLSL; template-heavy; no storage/compute split by default |
| DirectXMath | MIT | Strong SIMD with a storage/compute split; row vectors |
| HandmadeMath, linalg.h | Public domain | Single headers, few SIMD options |

**Coordinates:** (A) right-handed, Y-up, -Z forward (Godot, glTF cameras;
glTF imports unmirrored, but forward is -Z); (B) left-handed, Y-up, +Z
forward (Unity; importers mirror right-handed sources).

**Matrices:** (i) column vectors in C++ (textbook style; same bytes as C#,
but multiplication order reversed between the languages); (ii) row vectors
stored row-major everywhere (same bytes and same order in C++, C#, and
shaders; textbook formulas are transposed when read); (iii) our own C#
types with column vectors (loses `System.Numerics`' JIT acceleration).

## Decision

1. **Crucible writes its own C++ math library.**
2. **Storage types and compute types are separate.** Read
   [`docs/learning/storage-and-simd-types.md`](../learning/storage-and-simd-types.md)
   for the full explanation.
   - **Storage types** (`Vec2`, `Vec3`, `Vec4`, `Quat`, `Mat4`; names
     placeholder) are plain structs with natural layout, byte-identical to
     `System.Numerics`. They are used in components, files, and the
     boundary.
   - **Compute types:** a register vector type (one 4-lane SIMD register)
     for general math, and wide types (4 or 8 lanes of one component, SoA)
     for batch processing over ECS chunks.
   - **Every SIMD path has a scalar fallback,** so ARM64 compiles; SSE,
     AVX2, and NEON paths sit behind the same API.
3. **Row vectors, stored row-major, everywhere:** C++, C#
   (`System.Numerics`), and shaders (Slang's matrix layout flag set to
   match). Points transform as `p * M`; composition reads left to right in
   the order things happen (`S * R * T`, `world = local * parent`).
   Quaternions are (x, y, z, w).
4. **Left-handed, Y-up, +Z forward, meters,** in object, world, and view
   space, for objects and cameras alike. Clip space is the API's; the
   projection matrix bridges to it. Importers mirror right-handed sources
   (glTF, most FBX): flip Z in positions, normals, tangents, quaternions,
   and animation keys, and reverse triangle winding.

## Why

**Own library:** a contained, testable learning project (testable against
`System.Numerics`), and the only option shaped exactly to these rules.

**Storage vs. compute types:** a SIMD register holds 4 floats and prefers
16-byte alignment, but `Vec3` has 3. Making `Vec3` a SIMD type would pad
it to 16 bytes, breaking the byte match with C# and wasting memory in
every component. Separating them keeps memory layouts exact, and puts SIMD
where it pays: SoA processing of 4 or 8 entities at once, which ECS
columns (ADR 0023) already lay out.

**Row vectors everywhere:** gameplay is written in C# every day, and its
convention can't change. Matching it in C++ and shaders gives one mental
model ("multiply left to right in the order things happen") instead of a
multiplication order that flips between files. The cost, transposing
textbook formulas when reading them, is paid once per formula, not every
time languages change.

**Left-handed +Z forward:** it matches how the project owner already
thinks (X right, Y up, Z forward). The mirror conversion lives in one
place, the editor-only importers, where it can be tested exhaustively; a
convention that fought intuition would cause bugs in every line of
gameplay, camera, and animation code.

Given up: unmirrored glTF import, and textbook-convention matrices.

## Open

- Whether x64 builds require AVX2 (every target from about the last
  decade has it, Steam Deck included).
- ~~How Slag maps Vulkan's clip space, whose Y points down, against
  D3D12's.~~ Settled; see the amendment below.

## Amendment (2026-10-26): clip space

Settled with the project owner at the end of M2. Slag gives both backends
D3D12's clip space (Slag ADR 0037): clip-space +Y is up, viewports and
textures have their origin at the top left (row 0 is the top), and depth
runs 0 to 1. On Vulkan, whose clip-space Y points down, Slag flips the
viewport (a negative height, core since Vulkan 1.1), so the same shader and
viewport produce the same image on both.

Evidence: Slag's backend test "a triangle renders with +Y up into a cleared
target" draws vertices at clip (−1, 1), (0, 1), (−1, 0) and checks the
triangle lands in the top left of the read-back image; it runs on every
backend (rerun on RADV 2026-10-26: passes).

So Crucible's projection matrices are the standard left-handed D3D ones,
depth 0 to 1, with no per-backend Y flip. Crucible gets its own draw test
when it first draws geometry (M5).

## Amendment (2026-10-26): the storage types (M3 step 2)

Settled with the project owner as decision B of M3.

- **C# sees `System.Numerics`.** The C++ types are exposed as
  `System.Numerics.Vector2/3/4`, `Quaternion`, and `Matrix4x4` (ADR 0017's
  `CRUCIBLE_EXPOSE_AS`), so C# code uses .NET's SIMD-accelerated types
  everywhere, with no second set of types and no conversions at the
  boundary. Engines that predate accelerated `System.Numerics` (Unity,
  Godot, Stride) have their own; a new engine on .NET 10 doesn't need to.
- **The C++ names match:** `crucible::Vector2/3/4`, `Quaternion`,
  `Matrix4x4` (placeholders), one vocabulary in both languages, in
  `crucible/Math.h`.
- **`Matrix4x4` is four `Vector4` rows** (`rows[3]` is the translation),
  row-major and byte-identical to `System.Numerics.Matrix4x4`. The storage
  types are 4-byte aligned, as in C#; SIMD alignment belongs to the compute
  types (step 3).
- **Radians, and `System.Numerics`' Euler angles:** yaw (around Y), pitch
  (X), roll (Z), applied roll, then pitch, then yaw.
- **Conventions written down:** rotations follow the left-hand rule (a
  positive yaw turns +Z toward +X); the quaternion `*` is the Hamilton
  product, as in `System.Numerics`, so with row vectors `a * b` means "b,
  then a", and `concatenate(a, b)` reads in order; projections are
  left-handed with depth 0 to 1 (`perspectiveFieldOfView`, `orthographic`,
  `lookAt`, matching .NET's `...LeftHanded` functions).
- **Every operation computes what its `System.Numerics` counterpart does,**
  and a C# suite compares them over seeded random inputs (tolerance 1e-5);
  a deliberately flipped sign fails both the C++ and the C# tests.
