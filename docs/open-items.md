# Open items

Everything not yet decided. Resolve each one, record the outcome in an ADR
or in CLAUDE.md, and remove it from this list. Items tagged with a
milestone are decided or checked when that milestone reaches them
(`docs/milestones.md`).

## Needs a decision from the project owner

- C# test framework (third-party). [M0]
- Names of the default phases. → 0013 [M3]
- Whether x64 builds require AVX2. → 0024 [M3]
- ECS details: hierarchy and relationships, change detection,
  struct-with-references pools, blittable layout migration across hot
  reload. → 0012, 0023 [M3]
- How plain C# services are registered, found, and saved. → 0012 [M3]
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

- How Slag maps Vulkan's clip space (Y down) against D3D12's. → 0024 [M2]
- Pacing fallback when Slag's frame latency capability is missing, and how
  pacing behaves under `Mailbox`. → 0003 [M2]
- Swapchain image count when switching `Mailbox` ↔ `Fifo`, and drivers
  that return three images for `Fifo`. → 0003 [M2]
- Accuracy of SDL event timestamps on Windows, X11, Wayland, and Steam
  Deck (parry windows depend on it). → 0018 [M2]
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
