# Review findings

What the engine review (milestones, "After editor extensions: an engine
review") decided to change, system by system, with the owner. Changes are
made after the walk-through, or between systems when the owner says so,
each as its own step.

| System | Finding | Decision | Status |
|---|---|---|---|
| Core | Running a world outside the loop (a preview's) is hand work: animation steps, missing world transforms, the world's version. A way to run chosen systems on a world with its own clock. | Come back to (owner, 2026-11-25) | Noted |
| Core | `World.UpdateWorldTransforms` updates existing world transforms but doesn't add missing ones, as the propagation system does. | Come back to (owner, 2026-11-25) | Noted |
| Core | The editor doesn't show the schedule (phases, systems, order, timing) that ADR 0013 says it would, with gameplay and other systems running at different times. | Come back to (owner, 2026-11-25) | Noted |
| Core | Per-process state (component registration, registries) is a likely cause of two engine tests that fail once in a while in parallel runs. | Come back to (owner, 2026-11-25) | Noted |
| Core | Systems run one at a time; revisit if a game's frame is CPU-bound on the main thread. | Come back to (owner, 2026-11-25) | Noted |
| Platform | Games have no input layer above raw events: no actions bound to keys and buttons, no rebinding, no stick dead zones or smoothing. | Come back to (owner, 2026-11-25) | Noted |
| Platform | D3D12 and Vulkan frame timing differ on Windows: under Mailbox, D3D12's latency waitable doesn't hold frames to the refresh (frames in bursts), and whether pacing stays on under Mailbox is undecided. | Come back to, a priority (owner, 2026-11-25) | Noted |
| Platform | Typed text and keys are separate streams; only the UI library joins them, so a game not using it would redo that. | Come back to (owner, 2026-11-25) | Noted |
| Assets and scenes | A scene silently loses components nothing registers (a plugin removed): left out on load with a warning, gone on save. Keep them as JSON until something registers them. | Address (owner, 2026-11-25) | Noted |
| Assets and scenes | Editor code can't ask an asset about itself (a clip's length, a mesh's bounds, a texture's size): an asset info API. | Address (owner, 2026-11-25) | Noted |
| Assets and scenes | Save games aren't built (ADR 0021 sketched them): versioned, loading across game updates; decide before there are saves to keep working. | Address, a priority (owner, 2026-11-25) | Noted |
| Assets and scenes | Prefab overrides apply only at the outermost level of nested prefabs. | Address (owner, 2026-11-25) | Noted |
| Assets and scenes | File watching is timing-sensitive (watchers wait for quiet; one settled late on Windows during a package run). | Address (owner, 2026-11-25) | Noted |
| Mods | Not built: no loading of mod code into JIT games (ADR 0001, 0100), and one asset source at a time, so no archive layered over Game.crpak. Proposed: a mod is a plugin built against a shipped game (Build Mod: its code against Game.dll, its assets cooked into its own archive, a manifest naming the game's version); loaded from Mods/ with its archive layered over the game's (its IDs win: replace or add); data-only mods in AOT games too; authored in a mod project with the game read-only. Open: load order and conflicts, scene patches, the game's API stability, mod shaders. | Design (owner, 2026-11-25) | Noted |
