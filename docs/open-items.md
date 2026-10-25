# Open items

Everything not yet decided. Resolve each one, record the outcome in an ADR
or in CLAUDE.md, and remove it from this list.

## Needs a decision from the project owner

- Input timestamps for frame-rate-independent timing checks (parry
  windows). → 0003, 0010
- 2D physics library (third-party). → 0005
- Renderer detail ADRs: materials and shaders; shadows; lighting and
  ambient light; post-processing and the post-tonemap MSAA resolve; the
  pixel-art 2D path; the C# rendering (batched command) API. → 0015
- Upscaler licenses: DLSS, XeSS (FSR is MIT); SMAA's license. → 0004
- Names of the default phases. → 0013
- Audio (third-party?). → 0018
- C++ math library, layout-compatible with the C# types it mirrors. → 0017
- Interfaces for replaceable subsystems. → 0006
- ECS: storage strategy (archetypes or sparse sets), and build our own or
  use a library (flecs, EnTT; both MIT). → 0012
- ECS details: hierarchy and relationships, struct-with-references pools,
  blittable layout migration across hot reload. → 0012
- How plain C# services are registered, found, and saved. → 0012
- C++ runtime extension mechanism (not plugins; C++ never extends the
  editor). → 0002
- Asset pipeline and serialization: import formats, the asset database,
  scene and save-data formats, and serializing managed components across
  hot reload (0012).
- 3D physics library (third-party?).
- Skeletal animation.

## Revisit once there is a prototype to test against

- No AOT-analyzer opt-out for game code: expected to stay, but confirm
  with real game code. → 0001
- Plugins are source-only in v1; revisit prebuilt (binary) plugins. → 0002
- Late latching of the camera: tentatively in. → 0003
- Pacing fallback when Slag's frame latency capability is missing, and how
  pacing behaves under `Mailbox`. → 0003
- Accuracy of SDL event timestamps on Windows, X11, Wayland, and Steam
  Deck (parry windows depend on it). → 0018
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

## Parked

- Mod API stability across game updates. → 0001
