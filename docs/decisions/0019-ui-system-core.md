# 0019 — UI system: core model

**Status:** Accepted (2026-10-25). Text, styling, and input are decided
separately.

## Context

ADR 0016: Crucible builds its own UI system, shared by the editor and
games. It must serve dense tools (docking, trees, property grids, text
editing), fully custom plugin tools (ADR 0002), and styled, animated game
UI. Platform input comes from SDL3 (ADR 0018).

## Options

**Mode:**

- **Immediate** (Dear ImGui): UI is code run every frame, state lives in
  user code. Easiest for tools; hard for animation, rich styling, and
  layout that depends on later content; poor for game UI.
- **Retained** (Godot Controls, Unity UI Toolkit, WPF): a persistent
  element tree with layout, styling, event routing, and animation run by
  the system. Right for game UI and dense panels; more ceremony for
  one-off tools.

**Layout:**

| Option | License | Notes |
|---|---|---|
| Our own panels | zlib | Stack, grid, dock, overlay/anchor (WPF panels, Godot containers); simple to learn and extend |
| Flexbox via Yoga | MIT | Powerful (Unity UI Toolkit uses it), but hard to reason about, and a native call from C# layout |
| Full CSS layout | — | Far too large |

## Decision

1. **A retained element tree is the core,** with a **custom-draw element**
   that gives plugin code an immediate-style drawing API inside its
   rectangle. Fully custom tools (a timeline, a node graph, a video editor)
   are custom-draw elements; docking, scrolling, and focus still come from
   the tree.
2. **UI logic is C#:** element tree, layout, styling, events, animation.
   **Text shaping and glyph rasterization are native.** UI rendering goes
   through the batched command API that C# render passes use (ADR 0015),
   not a second rendering path.
3. **Layout uses Crucible's own panels** (stack, grid, dock, overlay /
   anchor, names placeholder). Games and plugins add panel types in C#.
4. **Text: Latin scripts in v1.** Non-Latin scripts (CJK, complex scripts,
   right-to-left) are a goal after v1, so v1 must not rule them out: text
   is UTF-8 throughout, and shaping sits behind an interface.

## Why

The retained tree serves game UI and dense panels; the custom-draw element
keeps one-off tools as easy as immediate mode where they need it. UI is an
"edge" (goal 1): heavily customized and hot-reloaded, and C# UI frameworks
show C# is fast enough for it. One rendering path keeps parity and reuse.
Panels are easier to learn, debug, and extend than flexbox, which suits a
hackable engine.

Given up: flexbox's expressiveness, and non-Latin text in v1.
