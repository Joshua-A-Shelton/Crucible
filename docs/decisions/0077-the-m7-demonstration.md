# 0077 — The M7 demonstration

**Status:** Accepted (2026-11-09); M7 step 8 built. Names settled by the
owner as drafted; those added while building are placeholders
(docs/naming.md).

## Context

M7's "done when": open a project, place an imported model, edit a property,
press Play, edit C# and see it hot-reload, and launch a standalone run.
Steps 3 to 7 each had a check of their own, each starting from a prepared
state and checking one thing. Step 8 runs the whole session at once, the
way a person would, so the pieces are checked working together; the owner
then tries it by hand to sign M7 off. M6's demonstration (ADR 0064) is the
model: a scripted session run headless by CTest, captures compared across
backends on Windows.

## Decision

Decided by the owner (2026-11-09):

1. **A brand-new project,** made by the check: one model and its texture
   copied in from the Diorama, and the game's code (tests/demonstration/
   Project: a C# component and system, `Wobble`, and a native module,
   `demo::Pulse` with the system "Demo.Pulse"). A new project works with no
   configuration (ADR 0006).
2. **The "done when", plus:** saving, closing, and reopening the scene; a
   texture changed while playing; the native module; and undo along the way.
3. **Captures,** compared on Windows between D3D12 and Vulkan within ADR
   0056's tolerance, as M6's were.

## The session

`DemonstrationChecks`, in the editor, headless (CTest
`launcher.jit.demonstration`, through tests/demonstration/
compare_demonstration.cmake):

1. **Create** the project "Demo"; copy in Tree.gltf, its buffer, and
   Leaves.png, and the game's Code and Native folders.
2. **Open** it: its C++ builds and loads, then its C#; its components are
   known. A new scene gets a sun and a camera.
3. **Place** the tree by dragging its tile from the asset browser into the
   viewport: placed under the pointer, kept on the drop as one step; undone
   and redone. Captured (`placed.png`).
4. **Edit** it in the inspector: its scale typed (undone and redone);
   `Wobble` and `Pulse` added, `Wobble`'s fields typed.
5. **Save**, close (a new scene), and **reopen** it: the tree and its edits
   are there.
6. **Play:** the C# rocks the tree; the C++ counts it, read from C# through
   the module's bindings. Captured (`playing.png`).
7. **Edit the C#** while playing: rebuilt, hot-reloaded, the old build
   unloaded, the tree's `Wobble` kept, still playing.
8. **Change the texture** while playing: reloaded on the GPU.
9. **Stop:** the tree back as it was.
10. **Run standalone,** headless: the game, its module, and the scene load,
    30 frames run, it ends cleanly, with every component known and every
    model loaded.

## Details

- **The editor starts with no project:** the check finds the Diorama and
  the game's code through `--game-arg demonstration-repo=<repository>`.
  Opening the Diorama first would load its native module, and a process
  loads one module per project for its lifetime (ADR 0074).
- **Determinism:** `Wobble` rocks by frame count, not time, and the captures
  come a fixed number of frames after each action, so both backends capture
  the same moment.
- **Found by writing it:** the script itself kept the inspector's number
  fields in its locals, and with them the old build's types, so the old
  build couldn't unload (the same trap ADR 0072's play check had); fields are
  now found and typed into by a helper that keeps nothing. A capture taken
  in the frame a placement landed showed the hierarchy before its scheduled
  refresh (it reads the scene again a frame after a change); the capture
  now waits for it.
- **Found by hand (owner, 2026-11-09):** lights and a camera added with Add
  Component started at all zeros (black, intensity 0, a field of view of 0),
  so the hand-made scene was black throughout and the texture swap had
  nothing to show; the scripted session had set their values in code, which
  hid it. Like the physics components (ADR 0075), the C# halves of
  `DirectionalLight`, `PointLight`, `SpotLight`, `AmbientLight`, `Sky`, and
  `Camera` now have constructors with the values a new one starts with (the
  Diorama's, roughly); `PostProcess` stays at zeros, which is neutral. The
  session now adds its sun and camera with Add Component, setting only where
  they are, and checks they start lit and with a field of view.
- **The standalone run** is checked through its log: a game process has no
  way to save a capture of its own.
- **The comparison covers the 3D view** (the viewport for placed.png, the
  Game panel for playing.png), not the whole window: the first Windows run
  differed in 1.4% of the window's pixels, nearly all of them the viewport's
  frame-time readout and the hierarchy, where the new tree landed in a
  different row. Neither is rendering. The views differ by a mean of
  0.018/255 (placed) and 0.023/255 (playing), 0.06% and 0.17% of pixels by
  more than 8/255. The windows are still saved whole.
- **Noticed, not changed:** the hierarchy orders top-level entities by their
  entity slot, so a model placed after a drag (whose preview entity was made
  and removed) can land above entities made before it, depending on timing.
  And the inspector's position fields clip a long number (the tree landed
  about 17.78 m away, shown as "7836").

## As built

- **Editor:** `DemonstrationChecks` (`--game-arg check-demonstration`,
  `demonstration-repo`, `compare`).
- **tests/demonstration:** `Project/Code/Wobble.cs` and `Pulse.cs`,
  `Project/Native/Pulse.h` and `Pulse.cpp` (`demo::Pulse`,
  `pulsesLastFrame`), `compare_demonstration.cmake` (Vulkan, then on Windows
  D3D12 compared with it).
- **CTest:** `launcher.jit.demonstration`: 27 checks on Vulkan; on Windows,
  33 on D3D12 (with the comparisons).
