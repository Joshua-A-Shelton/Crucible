# 0009 — Scope and audience

**Status:** Accepted (2026-10-25).

## Context

The audience decides costs that never show up as features: API stability,
documentation, how broad the defaults must be, and how polished the editor
has to be for newcomers. It also decides how much gets built.

## Options

- **(a) Personal engine:** for the owner's own games.
- **(b) Small circle:** the owner plus collaborators.
- **(c) Public, general-purpose engine** (Godot-like): real docs, stable
  releases, broad defaults, a newcomer-friendly editor.

## Decision

- **Mostly (a), possibly (b).** Crucible is Josh's engine. The public is
  free to use it (ADR 0007), but isn't designed for, and (c) is not closed
  off.
- **A deliberately limited feature set:** not trying to be Unity, or even
  Godot. Features are added because Crucible's games need them.
- **Good documentation** for the features that exist.
- **Semantic versioning.**
- **First target game: a turn-based 3D RPG,** mainly to learn the
  development process. Later: a more ambitious 3D action RPG. Neither is
  "state of the art".

## Why

A concrete first game keeps scope in check: v1 is what that game needs,
not everything an engine could have. A turn-based RPG puts the weight on
the engine's foundations and workflow (editor, content, UI, save data)
before the real-time demands of an action RPG.

Given up: breadth. Genres outside Crucible's own games get no special
support unless they need it.

## What the first games lean on (for planning, not yet decided)

- **Turn-based 3D RPG:** 3D rendering, skeletal animation, a lot of UI and
  text, dialogue, save/load, data-driven content (items, stats), scene
  editing, camera, audio, input.
- **3D action RPG, adding:** character controller and physics, combat hit
  detection, animation blending and state machines, and the latency goals
  (ADR 0003) under real pressure.
