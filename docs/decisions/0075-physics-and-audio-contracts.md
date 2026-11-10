# 0075 — Physics and audio contracts

**Status:** Accepted (2026-11-09); M7 step 7c built. Names settled by the
owner as drafted; those added while building are placeholders
(docs/naming.md).

## Context

ADR 0006 makes subsystems replaceable at coarse boundaries (renderer
pipeline, physics, audio, input, asset pipeline), crossed once per frame or
per batch, never per object; ADR 0013 adds the loop driver. ADRs 0027 and
0028 put physics and audio "behind Crucible's API" so a game could use
Jolt, FMOD, or Wwise, but neither subsystem was built. The owner chose to
define the interfaces in M7 rather than as each subsystem is built, and
split them: **7c**, physics and audio (new contracts); **7d**, input,
runtime assets, the renderer, and the loop driver (existing seams).

## Options

**The boundary:** (A) components, systems in fixed phases, and a service;
or (B) one backend interface per subsystem that the engine's own systems
call. **The language:** C++, bound to C#; or C# interfaces. **How much to
build:** contracts with null defaults and tests; or an ADR only. **What the
contracts cover:** a minimal set, or a larger one.

## Decision

Decided by the owner (2026-11-09):

1. **Option A.** A subsystem is its **components** (the data contract:
   saved in scenes, edited in the inspector, read by game code), its
   **systems**, registered under fixed names in fixed phases (the
   per-frame work, one call per tick, looping over chunks inside), and a
   **service** (the calls game code makes). Replacing it swaps the systems
   and the service; components and game code stay. The renderer already
   works this way (MeshInstance and the lights, the render steps,
   RenderAssets). Option B's per-object calls cross the boundary thousands
   of times a tick; batched, it becomes option A with the loops on the
   engine's side.
2. **C++, bound to C#.** Components and service functions in public
   headers (crucible/Physics.h, crucible/Audio.h); the implementations are
   native libraries, and a native module (ADR 0074) replaces a subsystem
   with no C# glue.
3. **Contracts, null defaults, and tests.**
4. **The larger contracts:**
   - **Physics:** `RigidBody` (static, kinematic, dynamic; mass,
     velocities, gravity scale, layer), `Collider` (box, sphere, capsule,
     mesh, convex hull; offset, friction, restitution, trigger), `Joint`
     (fixed, hinge, ball-and-socket, swing-twist, distance; anchors, axis,
     limits), `CharacterController` (capsule, step height, slope limit);
     `raycast`, `shapeCast`, `overlap`, `contacts`, `moveCharacter`, and a
     32-layer collision matrix. Systems: "Physics.Sync" (PrePhysics),
     "Physics.Step" (Physics), "Physics.WriteBack" (PostPhysics).
   - **Audio:** `AudioSource` (clip, volume, pitch, loop, play on start,
     bus, spatial, distances, rolloff, occlusion), `AudioListener` (a
     tag); `playOneShot` at an exact time, `stop`, bus volume and effects
     (low-pass, reverb send, ducking), snapshots, music with crossfades
     aligned now, on a beat, or on a bar. System: "Audio.Update"
     (LateUpdate).
5. **Replacing:** an implementation registers the systems under the same
   names (registering a name again replaces it) and installs its service
   (`installPhysics`, `installAudio`). The engine registers its defaults
   with `registerDefaultSystem`, which leaves a replacement registered
   first in place, so the order modules and the engine start in doesn't
   matter.
6. **Gameplay systems:** physics, audio, and a native module's systems are
   marked gameplay (`SystemDesc::gameplay`). A schedule adds them only when
   asked (`ScheduleBuilder.IncludeGameplaySystems`): the host asks for a
   game, the editor for its play mode, not for its own schedule, so physics
   doesn't simulate while editing. This replaces ADR 0074's module mark
   (`nativeSystemFromModule` is now `nativeSystemIsGameplay`,
   `IncludeModuleSystems` is now `IncludeGameplaySystems`).

## Why

Keeping the data in components keeps it where the editor, scenes, and game
code already look, and lets an implementation do its sync in native loops,
as ADR 0006 asks. One shape for every subsystem, matching the renderer's,
means one thing to learn. Null defaults make the seam real and tested before
Box3D or the mixer exist, and give tests and tools a subsystem that does
nothing predictably.

## Details

- **The null physics:** its systems do nothing; nothing moves or collides;
  queries find nothing; `moveCharacter` moves straight through everything
  and is never grounded.
- **The null audio:** silence that counts what it was asked
  (`nullAudioCounts`), and starts each source that plays on start once.
- **Times:** `playOneShot` takes a time on the input clock
  (`platform::timestampNow`, nanoseconds; 0 is now), so a sound can be
  placed exactly at an input or animation event (ADR 0026, 0028).
- **Snapshots** are defined bus by bus (`defineSnapshot`): a name, a bus,
  its volume and effects; blending moves every bus a snapshot names.
- **Music alignment** to beats and bars uses the playing track's tempo,
  which comes with M8's audio assets.
- **Collider sizes:** a box's full extents; a sphere's radius in `size.x`;
  a capsule's radius in `size.x` and the distance between its end caps'
  centers in `size.y`, along Y. Mesh and convex colliders name a model and
  one of its meshes (`meshIndex`).
- **The layer matrix is the engine's,** not an implementation's: every
  implementation reads it, so changing implementation keeps a game's
  layers.
- **`linkEngineComponents`** registers the new components in a module's
  own IDs too.

## Not yet

- Real implementations: Box3D or Jolt (ADR 0027), the mixer (ADR 0028, M8).
- Audio clip and music assets, and their importers (M8).
- 7d: input, runtime assets, the renderer, and the loop driver.

## As built

- **Core:** crucible/Physics.h and Physics.cpp, crucible/Audio.h and
  Audio.cpp; `SystemDesc::gameplay`, `registerDefaultSystem`,
  `nativeSystemIsGameplay`.
- **Engine:** the components' C# halves (Physics/PhysicsComponents.cs,
  Audio/AudioComponents.cs), registered and saved with the engine's;
  `ScheduleBuilder.IncludeGameplaySystems` (off unless asked); the host
  registers the engine's components before building its schedule and asks
  for gameplay systems unless it's the editor.
- **Editor:** the play schedule asks for gameplay systems.
- **Tests:**
  - **`PhysicsContractTests`** (native): the components and three
    gameplay systems in their phases; the null physics's answers and
    character moves; the layer matrix; an installed service answering.
    (C#): gameplay systems join a schedule only when asked; queries and
    layers through the bindings; the components saved.
  - **`AudioContractTests`** (native): the components (the listener a
    tag) and the gameplay system; the null audio's counts; play-on-start
    sources started once; an installed service answering. (C#): the null
    audio through the bindings; the components saved.
  - **`SubsystemReplacementTests`** (on the shared core): a test module
    replacing physics and audio; its systems take the names as gameplay
    systems and its services answer; the defaults registered again leave
    them in place.
