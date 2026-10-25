# Open items

Everything not yet decided. Resolve each one, record the outcome in an ADR
or in CLAUDE.md, and remove it from this list.

## Needs a decision from the project owner

- 2D physics library (third-party). → 0005
- Are 2D gameplay features (2D physics, tilemaps, frame animation) usable
  from HD 2D games on the 3D renderer, or tied to the pixel-art path?
  → 0005
- Renderer architecture ADR (clustered forward direction set by 0004).
- Upscaler licenses: DLSS, XeSS (FSR is MIT); SMAA's license. → 0004
- Frame-phase mechanism: the frame is built from ordered phases a game can
  reorder, replace, or add to. → 0003, 0006
- C#↔C++ binding generator: input (C++ headers or a separate description)
  and output (`LibraryImport` bindings + editor metadata). → 0006
- Interfaces for replaceable subsystems. → 0006
- C++ runtime extension mechanism (not plugins; C++ never extends the
  editor). → 0002
- Editor language, editor UI toolkit (third-party?), and whether
  play-in-editor runs in the editor's process. → 0002
- Hosting: native executable embedding CoreCLR (JIT) or linking NativeAOT
  output; what "load the root plugin" means under AOT. → 0001, 0002

## Revisit once there is a prototype to test against

- No AOT-analyzer opt-out for game code: expected to stay, but confirm
  with real game code. → 0001
- Plugins are source-only in v1; revisit prebuilt (binary) plugins. → 0002
- Late latching of the camera: tentatively in. → 0003
- Pacing fallback when Slag's frame latency capability is missing, and how
  pacing behaves under `Mailbox`. → 0003
- Swapchain image count when switching `Mailbox` ↔ `Fifo`, and drivers
  that return three images for `Fifo`. → 0003

## Parked

- Mod API stability across game updates. → 0001
- Baseline hardware for the speed goal (e.g. CPU core count and age);
  decides how much multi-core scaling to design for.
- Non-goals (CLAUDE.md §2).
- Crucible's own license.
- Target platforms (inherit Slag's Windows/Linux, consoles kept possible?).
- Scope and audience (personal engine vs. general-purpose).
- Reference games for the clarity and latency targets.
