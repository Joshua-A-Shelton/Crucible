# 0026 — Skeletal animation

**Status:** Accepted (2026-10-25).

## Context

Both target games need skeletal animation (ADR 0009), and the action RPG
depends on tightly tuned parry and cancel windows (ADR 0010).

Data: a **skeleton** (joint hierarchy and bind pose), a **skinned mesh**
(per-vertex joint influences and weights), and **clips** (per-joint
translation, rotation, and scale tracks; the main memory cost, so usually
compressed). Each frame, per character: sample clips into a local pose,
blend poses (crossfades, blend trees, layers, additive), apply procedural
adjustments (IK, look-at), convert local to model space through the
hierarchy (`local * parent`, ADR 0024), build skinning matrices (model
joint × inverse bind), and skin on the GPU. A **state machine** (states,
transitions with blend times, parameters) drives it, with **events**
marked on clip timelines.

## Options

| Option | License | Notes |
|---|---|---|
| ozz-animation | MIT | Complete data-oriented runtime with SoA SIMD; its own math and offline importers overlap ours |
| ACL | MIT | State-of-the-art clip compression only |
| Our own | zlib | Runtime on our math library (ADR 0024) |

Skinning in the vertex shader repeats in every pass (depth prepass,
shadows, forward); compute pre-skinning does it once per frame.

## Decision

1. **Compute pre-skinning:** one compute pass per frame skins every
   character into a buffer; later passes treat the result like a static
   mesh.
2. **Poses live in native pose buffers outside the ECS columns,**
   referenced from a blittable component by index (joint counts vary, so a
   pose can't be a fixed-size component; same pattern as ADR 0012's
   pools). The Animation phase (ADR 0013) animates characters in parallel,
   one job per character (ADR 0014).
3. **Crucible's own runtime:** sampling, blending, the hierarchy pass, IK,
   and state machines. Clips use simple quantization in v1; **ACL** is
   added for compression when clip memory matters. **ozz-animation** is
   the reference and fallback.
4. **State machines are data assets** with a C# API (set parameters,
   trigger transitions, receive events). A **visual graph editor** (a
   custom-draw tool, ADR 0019) is built when the action RPG starts; the
   turn-based RPG doesn't need it.
5. **Events carry exact clip times,** so gameplay compares them with
   timestamped input (ADR 0010) and windows stay frame-rate independent.
   **Windows are time ranges** on the clip timeline (e.g. a parry window).
   **Root motion** can be extracted and handed to the character
   controller.
6. **Four influences per vertex by default;** eight as an import option.

## Why

With a depth prepass and cascaded shadows, a skinned character is drawn
several times per frame, so skinning once is cheaper, and it removes
skinned variants of every pass (ADR 0025). Pose sampling and blending is
the textbook case for SoA wide types (ADR 0024) and practice for
quaternion and hierarchy math, which suits the learning goal; compression
is genuinely hard, which is why ACL exists.

Exact event times plus input timestamps carry frame-rate independence from
input all the way to animation, which is what tight parry windows need.

Given up, for now: a complete animation library on day one, and the graph
editor before the action RPG.
