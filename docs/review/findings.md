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
| Renderer | One camera, one image: no rendering to a texture a game can use (portraits, minimaps, mirrors), no split screen. | Revisit (owner, 2026-11-25) | Noted |
| Renderer | Limits are silent: lights past 64 in a cluster dropped, a full shadow atlas leaves a light unshadowed, visible lights kept in entity order; none reported. | Revisit (owner, 2026-11-25) | Noted |
| Renderer | Material gaps: no per-instance overrides, samplers fixed to linear repeat, no shader hot reload, material type names in one C# namespace. | Revisit (owner, 2026-11-25) | Noted |
| Renderer | Outlines are only hulls; edge-detected outlines and painted line maps were set aside. | Revisit (owner, 2026-11-25) | Noted |
| Renderer | Late latching is ready but not built (ADR 0050); revisit with the D3D12 timing priority. | Revisit (owner, 2026-11-25) | Noted |
| UI | No descendant selectors: icons can't follow their button's hover, tooltip text needed its own class, disabled play buttons dim by opacity. | Revisit before v1 (owner, 2026-11-25) | Noted |
| UI | Layouts are code only: designers can restyle a HUD or menu but not arrange it; a layout asset, then an editor for it. | Revisit before v1 (owner, 2026-11-25) | Noted |
| UI | No data binding: controls are filled and read by hand; a light binding (a property path, an observable value). | Revisit before v1 (owner, 2026-11-25) | Noted |
| UI | Latin text only: CJK (font fallback, larger glyph caches) for Japanese titles, names, or a localized release. | Revisit after v1 (owner, 2026-11-25) | Noted |
| UI | Composition bugs found late (a menu in a scroll view, a list in a dialog); tests cover controls alone, not combined. | Revisit before v1 (owner, 2026-11-25) | Noted |
| Animation and audio | Every sound is 50 ms late (the fixed schedule delay): a shorter delay for sounds triggered by input, or a measured, adaptive one. | Address, a high priority (owner, 2026-11-25) | Noted |
| Animation and audio | State machines are hand-edited JSON; the graph editor (planned for the action RPG) as a workspace: every game will need it. | Address soon (owner, 2026-11-25) | Noted |
| Animation and audio | No animation in the editor's scene: characters hold their first pose while editing; preview a clip on a scene's character with a preview's clock. | Address (owner, 2026-11-25) | Noted |
| Animation and audio | Music is loaded whole into memory; stream it from disk. | Address before the first game (owner, 2026-11-25) | Noted |
| Physics | No physics while editing (the world isn't stepped, queries find nothing): a simulate mode, physics only, for level building. | Address at some point (owner, 2026-11-25) | Noted |
| Physics | Joints have no motors, springs, or breaking (both libraries support them). | Address sooner rather than later (owner, 2026-11-25) | Noted |
| Physics | No ragdoll workflow: bodies per bone, joints between them, blending from animation, all by hand. | Address at some point (owner, 2026-11-25) | Noted |
| 2D | Sorting layers are edited in the project file by hand: no editor UI for adding, ordering, or y-sorting them; `Behind3D` untried by hand. | Address after v1, not critical (owner, 2026-11-25) | Noted |
| 2D | No importing levels from Tiled or LDtk. | Address after v1, not critical (owner, 2026-11-25) | Noted |
| 2D | Tilemap features waiting for a game: auto-tiling, animated tiles, 90° turns per cell, chunk culling, a trigger layer. | Address after v1, not critical (owner, 2026-11-25) | Noted |
| 2D | 2D lighting has no shadows, normal maps, or lights limited to layers. | Address after v1, not critical (owner, 2026-11-25) | Noted |
| 2D | The Sprite Sheet Editor needs Select and Draw tools; 2D sprite tooling could become an official plugin. | Address after v1, not critical (owner, 2026-11-25) | Noted |
| Editor | A native crash in Play (in the editor's process) loses unsaved scene edits: no autosave or recovery. | Address (owner, 2026-11-25) | Noted |
| Editor | One entity selected at a time: no multi-select for moving, deleting, duplicating, or editing fields across several. | Address (owner, 2026-11-25) | Noted |
| Editor | Prefabs can't be made or changed from the scene: no "make a prefab", "apply overrides", or editing a prefab on its own. | Address (owner, 2026-11-25) | Noted |
| Shipping | Steam Deck untested; Slag needs RADV from Mesa 26.2 (`VK_EXT_descriptor_heap`), and whether SteamOS ships it isn't known. | Worth addressing (owner, 2026-11-25) | Noted |
| Shipping | Packages build only for the editor's own platform: cross-building, or a documented second-machine or CI step. | Address next after store integration and patches (owner, 2026-11-25) | Noted |
| Shipping | No crash reporting: no crash handler, minidump, or note in the log. | Worth addressing (owner, 2026-11-25) | Noted |
| Shipping | No store integration (Steamworks: achievements, cloud saves, overlay, Steam Input, on-screen keyboard); touches platform, save games, input. | Most important (owner, 2026-11-25) | Noted |
| Shipping | One archive per game: no layering, patches, or DLC; shares layering with mods. | Most important (owner, 2026-11-25) | Noted |
