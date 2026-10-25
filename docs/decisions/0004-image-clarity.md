# 0004 — Image clarity

**Status:** Accepted (2026-10-24).

## Context

Goal 5: prefer techniques that keep the image sharp over ones that trade
clarity for temporal stability.

The industry's answer to aliasing became temporal accumulation: jitter each
frame and blend it with reprojected previous frames (TAA; DLSS, FSR 2+,
and XeSS are smarter versions of it). Engines then built other effects that
only look right after accumulation: noisy AO and reflections, dithered hair
and transparency, one-sample-per-pixel ray tracing. The results are blur,
ghosting, and smearing in motion, and once an engine depends on TAA,
turning it off breaks the image.

Aliasing sources and the clarity-preserving fix for each:

| Source | Fix |
|---|---|
| Geometry edges | MSAA |
| Shading (specular sparkle) | Specular AA: raise roughness where normals vary within a pixel |
| Alpha-tested foliage, fences | Alpha-to-coverage (with MSAA) |
| What's left | SMAA (post-process) |
| Everything, at a cost | Supersampling |

MSAA works well with forward rendering and is expensive and awkward with
deferred rendering.

## Options

- **Temporal-first** (TAA or an upscaler always on; Unreal, Unity HDRP):
  cheap, stable, soft.
- **Spatial-first** (MSAA + shader-level AA, temporal effects opt-in):
  sharp, more expensive per pixel, pushes toward forward rendering.

## Decision

- **The rule: every effect enabled by default must look correct in a
  single frame, with no temporal accumulation.** Temporal effects are
  allowed only when a game opts in.
- **Spatial filtering within one frame is allowed** (e.g. an edge-aware
  blur on AO): it softens slightly but never ghosts.
- **Default AA:** MSAA + specular AA + alpha-to-coverage, with SMAA
  optional.
- **The renderer leans clustered forward**, which MSAA needs. The renderer
  architecture gets its own ADR; this one only sets the direction.
- **Native resolution by default.** Upscalers and DLAA (DLSS, FSR, XeSS)
  are player-chosen options a game can offer; each one's license is
  checked when its ADR is written.
- **Clarity-reducing post effects are off by default** (motion blur, depth
  of field, film grain, chromatic aberration), available for games to
  enable.

## Why

The single-frame rule is what stops the slide into TAA dependence: if
defaults never need accumulation, turning temporal techniques off can
never break the image. Clustered forward with MSAA is a proven route
(Doom 2016/Eternal, many VR titles). Motion blur in particular works
against goal 4: a high, well-paced frame rate already gives motion
clarity.

Given up: the cheapest path to a stable image, and effects that only exist
in temporal form (denoised one-sample ray tracing, for example) as
defaults.
