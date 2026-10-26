# 0027 — Physics

**Status:** Accepted (2026-10-25). The 3D engine choice has a checkpoint
before the action RPG starts (below).

## Context

The turn-based RPG needs little physics (ray casts, triggers, simple
character movement); the action RPG needs a character controller, robust
collision, and frame-rate-independent hit detection for tightly tuned
combat (ADR 0010, 0026). Physics steps in the fixed tick (ADR 0003, 0013),
and every library must run on Crucible's job system (ADR 0014). A robust
physics engine is years of specialist work and not what Crucible is about,
so building our own was not considered further.

## Options

**3D:**

| Option | License | Notes |
|---|---|---|
| Box3D | MIT | By Erin Catto (Box2D). v0.1.0 released 2026-06-30, single maintainer, PRs disabled. C17; CCD including fast rotations; hulls, capsules, spheres, meshes, height fields; ray and shape casts, overlaps, sensors; a character mover; revolute, prismatic, distance, motor, weld, wheel joints; task callbacks for an external job system; SIMD; determinism; recording and replay |
| Jolt Physics | MIT | Since 2021, v5.6.0 (2026-07). C++17. Horizon Forbidden West, Death Stranding 2, Godot's default. Soft bodies, vehicles, ragdoll tooling with swing-twist constraints, mature `CharacterVirtual`, job-system interface |
| PhysX 5 | BSD-3 | Very capable; large, heavy build, GPU features unused |
| Bullet | zlib | Older, slower, less maintained |
| Rapier | Apache 2.0 | Rust; awkward boundary |

News coverage (not the repository) says Box3D builds partly on Valve's
Rubikon and that Facepunch's s&box uses it in production; unconfirmed.

**2D:** Box2D v3 (MIT; by Erin Catto; C, multithreaded through task
callbacks, SIMD), or our own.

## Decision

1. **3D: Box3D, with Jolt as the fallback.** Checkpoint before the action
   RPG starts: ragdoll-capable joints (ball-and-socket or swing-twist; not
   in Box3D's README joint list today), maturity of its character mover,
   and stability and release cadence. If any fall short, Crucible switches
   to Jolt behind the same API.
2. **2D: Box2D v3.**
3. **Both sit behind Crucible's physics API,** a replaceable subsystem (ADR
   0006); game code never calls Box3D or Box2D. Both run on Crucible's job
   system through their task callbacks, at frame priority.
4. **Stepping and sync:** physics steps inside the `FixedTick` phase group.
   PrePhysics pushes kinematic and teleported bodies from ECS to the
   physics world; PostPhysics writes dynamic bodies back, keeping previous
   and current tick transforms for per-body interpolation (ADR 0003). All
   of it is native loops over ECS chunks, with no per-body C# calls.
5. **Character controller:** the chosen engine's (Box3D's character mover,
   or Jolt's `CharacterVirtual`), with collide-and-slide movement; root
   motion (ADR 0026) supplies its desired movement.
6. **Combat hit detection uses swept, query-based hitboxes.** During an
   active window (ADR 0026), each hitbox shape attached to a joint is swept
   from its previous to its current position with a shape cast; a hit's
   position along the sweep gives an exact time, comparable with input and
   animation times. Hitboxes are queries, not simulated bodies.
7. **Determinism is not a goal** (no rollback or lockstep networking);
   revisit if networking ever comes up.

## Why

Box3D and Box2D v3 share an author, an API style, and the same task
callback design, so one integration pattern serves both. Box3D's C API
fits the C-shaped boundary (ADR 0017) with almost no adaptation, and its
recording and replay helps debug collisions. Its main weakness, newness,
falls mostly on the action RPG, which comes later; the first game's needs
are within its current feature set; and the physics API makes switching to
Jolt one backend's worth of work. Box3D runs correctly in our left-handed
world (its FAQ: it runs in a mirrored world unchanged).

Swept hitboxes keep combat the same at 30 and 240 fps: a fast swing that
passes through a target between two frames is still found, with its exact
time.

Given up: Jolt's maturity and breadth today, unless the checkpoint calls
for it.
