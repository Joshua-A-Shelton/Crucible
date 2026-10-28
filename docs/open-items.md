# Open items

Everything not yet decided. Resolve each one, record the outcome in an ADR
or in CLAUDE.md, and remove it from this list. Items tagged with a
milestone are decided or checked when that milestone reaches them
(`docs/milestones.md`).

## Needs a decision from the project owner

- Names of the default phases. → 0013 [M3]
- How plain C# services are registered, found, and saved. → 0012 [M4]
- Renderer details: shadows; lighting and ambient light; post-processing
  and the post-tonemap MSAA resolve; outlines. → 0015 [M5]
- The C# rendering (batched command) API. → 0015 [M6]
- C++ runtime extension mechanism (not plugins; C++ never extends the
  editor). → 0002 [M7]
- Interfaces for replaceable subsystems. → 0006 [M7, then as each
  subsystem is built]
- The pixel-art 2D path details. → 0005 [M11]
- Upscaler licenses: DLSS, XeSS (FSR is MIT); SMAA's license. → 0004
  [when each is added]

## Revisit once there is a prototype to test against

- First-class relationship queries (flecs-style pairs: "everything related
  to X"), beyond entity IDs in components. → 0033
- Job handles with dependencies (schedule after, without waiting), on top
  of or instead of counters; likely together with automatic scheduling
  between systems. → 0014, 0013
- Changing a component's storage (archetype, sparse, managed) across a hot
  reload: it throws and needs a restart today; migrate it instead if the
  editor makes it common. → 0035

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
