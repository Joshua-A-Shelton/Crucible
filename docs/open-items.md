# Open items

Everything not yet decided. Resolve each one, record the outcome in an ADR
or in CLAUDE.md, and remove it from this list.

## Needs a decision from the project owner

- Input timestamps for frame-rate-independent timing checks (parry
  windows). → 0003, 0010
- 2D physics library (third-party). → 0005
- Renderer architecture ADR (clustered forward direction set by 0004).
- Upscaler licenses: DLSS, XeSS (FSR is MIT); SMAA's license. → 0004
- Frame-phase mechanism: the frame is built from ordered phases a game can
  reorder, replace, or add to. → 0003, 0006
- C#↔C++ binding generator: input (C++ headers or a separate description)
  and output (`LibraryImport` bindings + editor metadata). → 0006
- Interfaces for replaceable subsystems. → 0006
- ECS: storage strategy (archetypes or sparse sets), and build our own or
  use a library (flecs, EnTT; both MIT). → 0012
- ECS details: hierarchy and relationships, struct-with-references pools,
  blittable layout migration across hot reload. → 0012
- How plain C# services are registered, found, and saved. → 0012
- C++ runtime extension mechanism (not plugins; C++ never extends the
  editor). → 0002
- Editor language, editor UI toolkit (third-party?), and whether
  play-in-editor runs in the editor's process. → 0002

## Revisit once there is a prototype to test against

- No AOT-analyzer opt-out for game code: expected to stay, but confirm
  with real game code. → 0001
- Plugins are source-only in v1; revisit prebuilt (binary) plugins. → 0002
- Late latching of the camera: tentatively in. → 0003
- Pacing fallback when Slag's frame latency capability is missing, and how
  pacing behaves under `Mailbox`. → 0003
- Steam Deck: no test device yet; check that SteamOS ships Mesa 26.2+
  (Slag's RADV minimum). → 0008
- Swapchain image count when switching `Mailbox` ↔ `Fifo`, and drivers
  that return three images for `Fifo`. → 0003

## After v1

- Revisit the "not state of the art" non-goal (CLAUDE.md §2).

## Parked

- Mod API stability across game updates. → 0001
