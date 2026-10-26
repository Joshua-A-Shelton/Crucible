# 0025 — Materials and shaders

**Status:** Accepted (2026-10-25).

## Context

Slag is bindless only, with one fixed pipeline layout (resource heap,
sampler heap, 128 bytes of push constants, one constant buffer), and takes
precompiled DXIL/SPIR-V from Slang. Toon and PBR shading are per material
and replaceable (ADR 0010); specular AA and alpha-to-coverage are defaults
(ADR 0004); the renderer is clustered forward with a depth prepass
(ADR 0015).

Slang's license is Apache 2.0 with the LLVM exception (checked
2026-10-25). The compiler runs only in the editor and at build time, and
the exception covers compiled output, so nothing reaches shipped games
unless a game ships the compiler (e.g. for mod shaders, not decided).

## Options

- **Shader authoring:** surface functions in Slang code, or a node-graph
  editor (Unreal's material editor, Unity's Shader Graph).
- **Feature switches:** compiled variants per feature keyword (Unity's
  permutation explosion: long builds, large downloads, stutter on first
  use), or material data read with draw-uniform branches.

## Decision

1. **Shaders are Slang.** Shading models are Slang interfaces
   (`IShadingModel`, placeholder) that the engine's lighting loop is
   written against; Slang specializes the loop per model at compile time,
   with no runtime dispatch. Crucible ships toon and PBR models; games add
   their own by implementing the interface.
2. **Shaders and materials are separate.** A shader defines what a surface
   computes and its parameters; a material is one set of values. In v1,
   shaders are authored as **surface functions** in Slang that fill in
   surface data (albedo, normal, roughness, emissive, alpha, ...); the
   engine supplies vertex processing, the lighting loop, and the shading
   model. A node-graph editor comes after v1.
3. **Variant policy:**
   - Variants only where unavoidable: different pipeline state, vertex
     layout (skinned or static), shading model, or pass (depth-only,
     shadow, forward).
   - Everything else is material data, read with draw-uniform branches.
   - All variants are compiled at import or build time; shipped games never
     compile shaders.
   - Pipelines are created while loading, on background jobs (ADR 0014),
     never during gameplay frames.
4. **Bindless material data:** each material's parameters are a struct in
   one large GPU buffer, indexed by a material index passed per draw (push
   constants or per-instance data). Textures are descriptor-slot indices
   inside that struct. The parameter layout comes from Slang reflection,
   which generates the material's C# type and its layout checks.
5. **Shaders hot-reload in the editor:** saving a shader recompiles the
   affected variants and swaps their pipelines.

## Why

Interfaces with compile-time specialization make shading models
replaceable (goal 7) at no runtime cost. Surface functions keep material
authors out of lighting code. The variant policy keeps build times and
shader counts small, and creating pipelines only while loading removes
the main cause of shader-compilation stutter, which would undermine goal 4.
Generating the C# parameter layout from Slang reflection applies ADR
0017's layout-check idea to the GPU.

Given up, for now: a visual node-graph editor, and runtime shader
compilation in shipped games.
