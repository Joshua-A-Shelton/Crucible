# 0010 — Reference targets

**Status:** Accepted (2026-10-25).

## Context

The clarity, latency, and speed goals need concrete targets for the games
Crucible is actually for (ADR 0009: a turn-based 3D RPG first, a 3D action
RPG later).

## Decision

### Art direction (both games)

- **Stylized, anime-esque.** References: Bravely Default, parts of Breath
  of the Wild, and the characters in some Sonic games.
- **Soft-banded toon shading,** not hard cel shading: the edges between
  bands of color are softened.
- This is Crucible's own use case, **not a lock-in.** A game made with
  Crucible can use any art style (goal 7, ADR 0006).

### Feel (the action RPG)

- Between The Legend of Zelda and Kingdom Hearts, but faster and more
  free-flowing.
- **Tightly tuned parry windows,** and nuanced player response to the
  game world.

### Hardware baseline

- **The first game holds a steady 60 fps on Steam Deck at its native
  1280×800, with MSAA on** (ADR 0004).
- The action RPG's bar (e.g. higher frame rates on desktop) is set when
  that game starts.

## Why

A stylized look suits the clarity goal: flat-ish shading and clean edges
are where aliasing and temporal smearing show most, and where MSAA and
single-frame techniques shine. Steam Deck is a fixed, low-end-gaming-PC
class device (ADR 0008), so a target on it is checkable.

## Consequences (to be designed later)

- **Shading is per-material and replaceable.** Crucible ships both a
  soft-banded toon model and a physically based model; **new projects
  start with toon.**
- **Outlines** are common in this style. Inverted-hull outlines are
  geometry and get MSAA for free; post-process edge detection is a
  single-frame technique (allowed by ADR 0004) but has to work with MSAA
  samples.
- **Tight parry windows need input timestamps.** A window measured in
  milliseconds can't depend on the frame rate, so input events should
  carry the time they happened, and timing checks compare those times
  rather than frame numbers. This ties into ADR 0003's input path.
