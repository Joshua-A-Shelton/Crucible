# 0075 — Physics and audio contracts

**Status:** Accepted (2026-11-09); M7 step 7c built and tried by hand by
the owner on KWin. Names settled by the owner as drafted (docs/naming.md).

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
     velocities, gravity scale, layers and mask), `Collider` (box, sphere, capsule,
     mesh, convex hull; offset, friction, restitution, trigger), `Joint`
     (fixed, hinge, ball-and-socket, swing-twist, distance; anchors, axis,
     limits), `CharacterController` (capsule, step height, slope limit,
     layers and mask); `raycast`, `shapeCast`, `overlap`, `contacts`,
     `moveCharacter`; 32 collision layers (see Layers, below). Systems: "Physics.Sync" (PrePhysics),
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

- **The Diorama** names four layers (Default, Player, Enemies, Props).
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
- **Layers (changed after trying it by hand, owner, 2026-11-09):** first
  built as one layer per body and a project-wide matrix of which layers
  collide, shown as a 0–31 slider. The owner asked for what other engines
  do: a body can be on several layers. Now a body or character has two
  masks, `layers` (which it's on) and `mask` (which it collides with); two
  collide when either's mask has a layer the other is on. That's Box3D's
  own category and mask filtering, and Godot's layers and masks; the
  matrix (`setLayersCollide`, `layersCollide`) went. The project names its
  layers (`"PhysicsLayers"` in the .crproject, up to 32; layer 0 is
  "Default" when unnamed); the inspector shows a mask as a multi-select
  drop-down of those names (`LayerMaskField`: "Nothing", "Everything", or
  the names that are on; the list stays open while layers are toggled), for
  any `uint` marked `CRUCIBLE_HINT_LAYERS` in C++ or `[Layers]` in C#.
- **Defaults when added:** the C# halves of `RigidBody`, `Collider`,
  `CharacterController`, and `AudioSource` have constructors, which reading
  an empty object (Add Component) uses: a dynamic body of 1 kg on layer 0
  colliding with everything, a 1 m box, a 0.4 m by 1 m character, a source
  at full volume heard from 1 to 50 m.
- **`linkEngineComponents`** registers the new components in a module's
  own IDs too.

## Not yet

- Real implementations: Box3D or Jolt (ADR 0027), the mixer (ADR 0028, M8).
- Audio clip and music assets, and their importers (M8).
- 7d: input, runtime assets, the renderer, and the loop driver.

## As built

- **Core:** crucible/Physics.h and Physics.cpp, crucible/Audio.h and
  Audio.cpp; `SystemDesc::gameplay`, `registerDefaultSystem`,
  `nativeSystemIsGameplay`; `CRUCIBLE_HINT_LAYERS`, carried by the binding
  generator to `[Layers]`, by the serializer generator to
  `SerializedField.IsLayers`, and shown by the inspector as a
  `LayerMaskField` (Crucible.UI) with `Project.PhysicsLayers`' names.
- **Engine:** the components' C# halves (Physics/PhysicsComponents.cs,
  Audio/AudioComponents.cs), registered and saved with the engine's;
  `ScheduleBuilder.IncludeGameplaySystems` (off unless asked); the host
  registers the engine's components before building its schedule and asks
  for gameplay systems unless it's the editor.
- **Editor:** the play schedule asks for gameplay systems.
- **Tests:**
  - **`PhysicsContractTests`** (native): the components and three
    gameplay systems in their phases; the null physics's answers and
    character moves; an installed service answering. (C#): gameplay
    systems join a schedule only when asked; queries through the bindings;
    a body's defaults (on layer 0, colliding with everything), also when
    read from an empty object; its masks marked as layers; the components
    saved. `ControlsTests`: a layer mask field toggling several named
    layers with the list open, and showing Nothing, Everything, names, and
    an unnamed layer's number.
  - **`AudioContractTests`** (native): the components (the listener a
    tag) and the gameplay system; the null audio's counts; play-on-start
    sources started once; an installed service answering. (C#): the null
    audio through the bindings; the components saved.
  - **`SubsystemReplacementTests`** (on the shared core): a test module
    replacing physics and audio; its systems take the names as gameplay
    systems and its services answer; the defaults registered again leave
    them in place.

## Amendment (2026-11-11): queries take a QueryFilter

M9 step 3 (ADR 0090) replaced each query's `layers` parameter with a
`QueryFilter` (`layers`, `ignore`, `triggers`; `defaultQueryFilter()`), and
the engine's Box3D physics now answers them.

