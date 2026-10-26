# Open items

Everything not yet decided. Resolve each one, record the outcome in an ADR
or in CLAUDE.md, and remove it from this list.

## Needs a decision from the project owner

- Input timestamps for frame-rate-independent timing checks (parry
  windows). → 0003, 0010
- Renderer detail ADRs: shadows; lighting and
  ambient light; post-processing and the post-tonemap MSAA resolve; the
  pixel-art 2D path; the C# rendering (batched command) API. → 0015
- Upscaler licenses: DLSS, XeSS (FSR is MIT); SMAA's license. → 0004
- Names of the default phases. → 0013
- Whether x64 builds require AVX2. → 0024
- How Slag maps Vulkan's clip space (Y down) against D3D12's. → 0024
- Interfaces for replaceable subsystems. → 0006
- ECS details: hierarchy and relationships, change detection,
  struct-with-references pools, blittable layout migration across hot
  reload. → 0012, 0023
- How plain C# services are registered, found, and saved. → 0012
- C++ runtime extension mechanism (not plugins; C++ never extends the
  editor). → 0002

## Revisit once there is a prototype to test against

- No AOT-analyzer opt-out for game code: expected to stay, but confirm
  with real game code. → 0001
- Plugins are source-only in v1; revisit prebuilt (binary) plugins. → 0002
- Late latching of the camera: tentatively in. → 0003
- JSON as the authored text format. → 0021
- The save-data format. → 0021
- EXR input and BC6H compression for HDR textures, when needed. → 0022
- ACL for clip compression, when clip memory matters. → 0026
- Animation state machine graph editor, when the action RPG starts. → 0026
- **Box3D checkpoint, before the action RPG starts:** ragdoll-capable
  joints, character mover maturity, stability and release cadence. Switch
  to Jolt if any fall short. → 0027
- Pacing fallback when Slag's frame latency capability is missing, and how
  pacing behaves under `Mailbox`. → 0003
- Accuracy of SDL event timestamps on Windows, X11, Wayland, and Steam
  Deck (parry windows depend on it). → 0018
- SDL3 audio output latency per platform (miniaudio if it falls short).
  → 0028
- Steam Deck: no test device yet; check that SteamOS ships Mesa 26.2+
  (Slag's RADV minimum). → 0008
- Swapchain image count when switching `Mailbox` ↔ `Fifo`, and drivers
  that return three images for `Fifo`. → 0003

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
