# Open items

Everything not yet decided. Resolve each one, record the outcome in an ADR
or in CLAUDE.md, and remove it from this list. Items tagged with a
milestone are decided or checked when that milestone reaches them
(`docs/milestones.md`).

## Recheck before every release (until v1)

Workarounds for other people's bugs. Before each release, check whether
the bug is fixed in current drivers or tools; lift the workaround when it
is, and record which version fixed it.

- **Nested structs in `[GpuShared]` types** are refused because NVIDIA's
  Vulkan driver (621.01, 2026-10) places a nested struct's members at the
  outer struct's offset 0 in buffers reached through descriptor heaps.
  Recheck with the repro in ADR 0047 on current NVIDIA drivers (and report
  it to NVIDIA if no one has); allow nesting once it's fixed. → 0047
- **ThreadSanitizer can't run FreeType's rasterizer:** its
  setjmp/longjmp (`ftgrays.c`) trips TSan's longjmp check ("can't find
  longjmp buf", clang 23.1, 2026-11). TSan runs of the text tests cover only
  the distance-field tests (`-tc="distance fields*"`), which don't
  rasterize. Recheck with current clang; run the whole suite once it
  passes. → 0057
- **GCC's `-Warray-bounds` false positive** in HarfBuzz 11.2.1's USE
  shaper (`hb-ot-shaper-use.cc`, release builds, GCC 16.2) is silenced for that
  target. Recheck with each HarfBuzz or GCC update. → 0057

- **SDL's Wayland data device is patched** (`cmake/PatchSdl.cmake`) to
  leave Crucible's window drags alone; without it, its decline cancels
  our drops (the compositor takes the last answer from an app's data
  devices). On each SDL update, check the patch still applies and is
  still needed (an SDL hook for app-defined drag types would replace it),
  and try a tear-off and dock-back on Wayland by hand. → 0066
- **`xdg-toplevel-drag-v1` is a staging protocol** (wayland-protocols
  1.45, vendored): check for a stable version, and that KWin, GNOME, and
  wlroots compositors still offer it (`CRUCIBLE_TEST_WAYLAND=1` native
  test prints what the desktop has). → 0066

## Needs a decision from the project owner

- Final names of the default phases (the list is ADR 0036's). → 0013, 0036 [M3]
- C++ runtime extension mechanism (not plugins; C++ never extends the
  editor). → 0002 [M7]
- Interfaces for replaceable subsystems. → 0006 [M7, then as each
  subsystem is built]
- The pixel-art 2D path details. → 0005 [M11]
- Upscaler licenses: DLSS, XeSS (FSR is MIT); SMAA's license. → 0004
  [when each is added]

## Revisit once there is a prototype to test against


- **`launcher.jit.particles` failed twice on Windows** (debug, in full
  parallel runs, 2026-11-03: during M6 steps 3 and 8), each time with its
  output lost, and passed in every rerun since (dozens alone and in full
  runs). Run Windows suites with `--output-on-failure` and keep the log;
  investigate from the first one caught. → 0037
- **Stray KB in NativeAOT's allocation counter:** the "allocates nothing"
  tests (frames, ECS iteration, `ParallelFor`, the latency recorder) saw
  about 8 KB (8112 bytes) more on their own thread in some NativeAOT
  release runs (6 in 25), when `ControlsTests` ran beside them: never
  with those classes alone (0 in 25), not under the JIT, not from any
  frame-loop system (a per-system probe found none), and not a
  collection moving the counter (probed, idle and busy). They now measure
  several windows and pass if one allocated nothing (`AllocationWindows`),
  which still fails code that allocates every frame (tested). Find what
  lands on that thread, then measure in one window again. → 0063

- Games bringing assemblies (ADR 0056): the launcher loads a game alone
  into its own load context, and nothing resolves the assemblies beside it
  (the Diorama sample resolves its own). Packages a game uses will need
  the engine to. → 0011
- Sidecar settings at the wrong level are ignored without a word (the
  Diorama's `Outline` was, until it moved under `Settings`): warn on
  unknown keys. → 0040

- Outlines after 9b (ADR 0055), chosen as hulls but marked by the owner to
  come back to:
  - edge-detected outlines (creases and intersections), compared and
    measured in 0055;
  - lines drawn by the material from a painted line map (view-dependent
    strokes like an anime nose's), prototyped in 0055;
  - hull tints by surface color only through the material's surface, not
    vertex colors; blended materials have no outlines.
- Finishing after 9b (ADR 0055): automatic exposure (opt-in, temporal);
  LUT grading (needs 3D texture import); SMAA (ADR 0004's optional
  extra); measure bloom and outlines on Steam Deck once there is one (M10). → 0055

- Shadows after step 8 (ADR 0054): when the atlas is full, a light goes
  without shadows that frame (nothing reports it); bias and normal offset
  are fixed, not per light; the tile cache doesn't notice a material or
  model changing under the same handle (clear it on hot reload, M7); a
  debug view of the cascades and the atlas would help tuning; one-sided
  meshes cast only from their front. Measure the shadow passes on Steam
  Deck once there is one (M10). → 0054

- Lighting after step 7 (ADR 0053): lights past a cluster's 64 are dropped
  silently (count them in debug, or raise the cap if scenes need it);
  spot lights are culled and clustered by their range sphere, not their
  cone; the 1024 visible lights are kept in entity order, not by
  importance. → 0053
- MikkTSpace (third party, tangent generation) shifts a 32-bit value by 32
  at mikktspace.c line 1667 in the importers' tests; UBSan reports it. Found
  in M5 step 6b's sanitizer run; look at whether upstream has fixed it, or
  patch it where it's fetched. → 0043
- Game materials after 6b (ADR 0052): shader hot reload in the editor
  (pipelines and shaders are kept until the renderer stops); per-instance
  material overrides; material type names share one C# namespace
  (`Crucible.Materials`), so two shaders can't both declare a `Brick`;
  arrays whose elements are padded can't be mirrored in C#. → 0052
- Materials after 6a (ADR 0052): materials choose samplers (the built-in
  ones use linear repeat). → 0052
- Transparency after 9a (ADR 0055): one draw per transparent instance
  costs about 0.1 ms of CPU per 1,000 (measured with 4,000); particle
  systems will want their own path (GPU-sorted, or weighted blended OIT
  just for them). Objects passing through each other sort as wholes.
  Transparent surfaces cast no shadows. Painted cubemap skies wait for
  cubemap import. → 0055
- First-class relationship queries (flecs-style pairs: "everything related
  to X"), beyond entity IDs in components. → 0033
- Job handles with dependencies (schedule after, without waiting), on top
  of or instead of counters; likely together with automatic scheduling
  between systems. → 0014, 0013
- Changing a component's storage (archetype, sparse, managed) across a hot
  reload: it throws and needs a restart today; migrate it instead if the
  editor makes it common. → 0035
- An intermittent allocation-test failure ("Expected to be 0"): first seen
  once under NativeAOT (2026-10-28, M4 step 2), name lost. Caught again on
  2026-10-28 (M5 step 1) under the JIT, release:
  `EcsTests.IteratingMakesNoGarbageOnceWarm` saw 7,600 bytes allocated on
  its own thread across 50 `ForEachChunk`/`ParallelForEachChunk` passes, in
  1 run of 5 (and once in a full ctest run). Ruled out: garbage collections
  on other threads (a probe saw no counter drift in 348 GC windows). Next:
  find what the waiting thread allocates (tiered JIT, job helping). → 0036,
  0038
- Textures embedded in glTF files (`.glb` images, `data:` URIs): imported as
  sub-assets with IDs of their own, when a real model needs it; a warning
  until then. → 0043
- Skins and morph targets in imported meshes, with animation. → 0026, 0043
- Mesh LODs: Blender's Suzanne (seven materials, UV seams) gets no level
  beyond LOD 0; find out whether seams or the per-submesh stopping rule
  hold simplification back. → 0043, 0046
- Shader toolchain: arrays and int vectors in shared structs, when a
  shader needs them. → 0047
- Prefabs: removing or reordering a prefab's entities in an instance;
  migrating overridden fields when a component's version changes; a
  cached form for spawning at runtime (with cooking); a `Name` component,
  for the editor and for model nodes. → 0045
- FBX: textures embedded in the file (with glTF's), separate metalness and
  roughness textures packed into one, and per-instance materials. → 0044
- FBX from Blender's default export arrives with nodes scaled by 100 and
  vertices by 1/100: an import setting to fold a uniform node scale into
  a mesh only that node uses, if it bothers anyone. → 0044
- Values in a system's fields across a hot reload: the new version's
  systems are new objects, so the old ones' fields are lost. Migrate them
  like component values if games keep much state in systems. → 0036
- The generated AOT bootstrap that calls each compiled-in game's and
  plugin's registration, when games are first linked into the AOT launcher;
  until then AOT code calls the generated class directly. → 0011, 0036

- Whether pacing stays on by default under `Mailbox`. → 0003 [M2,
  decide] Measured with the latency harness (RADV, KDE Wayland, 144 Hz,
  clear-only frames): paced Mailbox reads input 3.9 ms after it arrives
  (median) and completes the present 10.8 ms after (p95 13.6), one frame per
  refresh. Unpaced Mailbox reads input in 0.05 ms and finishes on the GPU in
  0.2 ms at ~10,000 fps, but when its frames reach the screen is unknown
  without present timing (a Slag after-1.0 feature) or a photodiode. The
  fallback pacing (wait for the previous GPU frame) is decided (ADR 0015's
  amendment). On Windows (RTX 2070 SUPER, 60 Hz), Vulkan paces the same way
  (present done 24.8 ms median), but D3D12's latency waitable under Mailbox
  doesn't hold frames to the refresh: they come in bursts (interval median
  0.8 ms, p95 16.2), and "present done" there marks when DXGI accepts
  another frame, not when one reached the screen. So the harness's present
  column is only meaningful on Vulkan for now.
- Gamepad timestamp accuracy, with the injected-input check
  (`tests/native/InputTimingTests.cpp`), when gamepad input lands; Steam
  Deck input timestamps (gamescope, often XWayland). → 0018 [gamepad
  support; M10 for the Deck] Keyboard and mouse are settled (ADR 0018's
  amendment).
- SDL's X11 input timestamps: keyboard events are stamped when processed,
  and raw mouse motion passes X server milliseconds through as nanoseconds.
  Report or fix upstream; until then Crucible flags X11
  (`eventTimestampsAreArrivals`). → 0018
- Late latching of the camera: tentatively in. → 0003 [M5]
- JSON as the authored text format. → 0021 [M4]
- No AOT-analyzer opt-out for game code: expected to stay, but confirm
  with real game code. → 0001 [M10]
- Plugins are source-only in v1; revisit prebuilt (binary) plugins. → 0002
- The save-data format. → 0021 [first game]
- SDL3 audio output latency per platform (miniaudio if it falls short).
  → 0028 [M8]
- Steam Deck: no test device yet; check that SteamOS ships Mesa 26.2+
  (Slag's RADV minimum). → 0008 [M10, when a device is available]
- Hosted CI: turn the workflow's automatic triggers back on when Actions
  minutes allow, and check whether GitHub's Windows runners have LLVM
  (CLAUDE.md §6).
- EXR input and BC6H compression for HDR textures, when needed. → 0022
- ACL for clip compression, when clip memory matters. → 0026
- Animation state machine graph editor, when the action RPG starts. → 0026
- **Box3D checkpoint, before the action RPG starts:** ragdoll-capable
  joints, character mover maturity, stability and release cadence. Switch
  to Jolt if any fall short. → 0027

## After v1

- Revisit the "not state of the art" non-goal (CLAUDE.md §2).
- Automatic parallel scheduling between systems. → 0013
- GPU-driven culling and draw generation, if needed. → 0015
- Non-Latin text: CJK, complex scripts, right-to-left, IME. → 0019
- Distance-field text for large, scaled, or animated text. → 0020
- Accessibility (screen readers); low priority. → 0020
- Node-graph shader editor. → 0025
- Shipping the Slang compiler for mod shaders in JIT games. → 0025
- Steam Audio (HRTF, occlusion) as an optional plugin. → 0028

## Parked

- Mod API stability across game updates. → 0001
- Deterministic physics, if networking ever comes up. → 0027
