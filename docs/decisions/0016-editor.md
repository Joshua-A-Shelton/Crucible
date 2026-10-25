# 0016 — Editor: language, UI, play-in-editor

**Status:** Accepted (2026-10-25).

## Context

Editor extensions are C# only (ADR 0002), the editor learns about the
world from C# metadata (ADR 0006), and the engine's C# layer owns the frame
(ADR 0011). The editor needs 1:1 parity on Windows and Linux, Wayland
included (ADR 0008), an embedded Slag-rendered viewport, and room for
plugins to build fully custom tools (ADR 0002).

## Options

**UI toolkit:**

| Option | License | Notes |
|---|---|---|
| Dear ImGui | MIT | Immediate mode; renderer written on Slag, so parity and viewports are free; easy custom tools; tools-UI look, weaker layout and text editing |
| Avalonia | MIT | Polished retained-mode C# UI; large dependency with its own Skia GPU stack; embedding a Vulkan/D3D12 viewport is awkward, especially on Wayland |
| Our own, shared with game UI | zlib | Godot's and Unreal's model; one UI system for editor and games; a large amount of work, and the editor waits on it |
| Qt, GTK | LGPL / commercial | Not permissive enough |
| WPF, WinUI, MAUI | MIT | No Linux support |

**Play-in-editor:** in the editor's process (Unity, Unreal: instant, live
inspection, fits ADR 0001's in-process hot reload; a native crash takes
the editor down) or a separate process (Godot: survives crashes, closer to
shipping; slower to start, inspection needs IPC, embedding its window is
hard on Wayland).

## Decision

1. **The editor is written in C#.** It is another C++ launcher (ADR 0011)
   that starts .NET and runs the editor's C# layer.
2. **Crucible builds its own UI system, shared by the editor and games.**
3. **Play runs in the editor's process by default.** A standard "Run
   standalone" action launches the game as a separate process in its own
   window (no embedding). C# exceptions in game code are caught by the
   editor; native crashes take it down.

## Why

C# follows from ADRs 0002, 0006, and 0011; a C++ editor would need the
second reflection path ADR 0006 ruled out.

Crucible's games need a UI system anyway (RPGs are UI-heavy: menus,
dialogue, inventory), and one system means one set of skills for editor
plugins and game UI, parity by construction (everything is drawn through
Slag), and trivial viewport embedding. It is also squarely a learning
project.

Same-process Play gives the fastest iteration and reuses ADR 0001's hot
reload; "Run standalone" covers crash-prone code and testing as shipped,
without cross-process window embedding.

Given up: a ready-made editor UI. The editor can't be built until the UI
system works, and the UI system has to serve both dense tools (docking,
trees, property grids, text editing) and styled, animated game UI with
gamepad navigation.

## Consequences

- The UI system is on the critical path for both the editor and the first
  game, so it is designed early (its own ADR).
- Text must stay crisp (goal 5): glyphs rendered for exact pixel sizes or
  as distance fields, decided in the UI ADR along with font loading and
  shaping libraries.
