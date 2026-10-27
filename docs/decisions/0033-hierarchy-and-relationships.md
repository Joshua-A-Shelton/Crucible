# 0033 — Hierarchy and relationships

**Status:** Accepted (2026-10-27). Names are placeholders (docs/naming.md).

## Context

ADR 0012 makes the ECS the world, shown in the editor as a hierarchy built
from parent/child relationships, and transforms need that hierarchy
(`world = local * parent's world`, ADR 0024). Games also link entities in
other ways: a turret targets a ship, a sword is attached to a bone. ADR 0023
left hierarchy and relationships to a later decision; this is it (M3
decision F).

Decision E (ADR 0023's amendment) constrains it: columns hold fixed-size
values in 16 KB chunks, so a variable-length list of children can't live in
a chunk.

## Options

- **A `Parent` component plus a children list** (Unity Entities: `Parent`
  and a `Child` buffer; Bevy: `ChildOf` and `Children`, kept in sync by the
  engine). Hierarchy is ordinary data; transform propagation walks it.
- **Relationship pairs** (flecs): `(ChildOf, parent)` is a component made
  from a pair, so "children of X" is a plain query, and any relationship
  works the same way (`(Likes, Bob)`). But each distinct parent creates its
  own archetype, so many parents fragment storage into tiny chunks; flecs 4
  added a non-fragmenting hierarchy for that reason.
- **A separate scene graph** (Godot's nodes): contradicts ADR 0012.

For other relationships: components holding entity IDs, or first-class
pairs. When a parent is destroyed: destroy its descendants, or make them
roots.

## Decision

1. **Hierarchy is a `Parent` component plus an ordered children list.** The
   child's chunk holds `Parent` (the parent's entity ID), so queries see it
   like any component; the parent's children, in order (the editor's tree
   and UI need one), live in side storage owned by the ECS and kept in sync
   when parents change. Transform propagation visits parents before
   children (built in M5).
2. **Other relationships are entity IDs in components** (`Target { Entity
   ship; }`); the generation in each ID makes a stale one detectable.
   First-class relationship queries are an open item, revisited when a real
   system needs "everything related to X".
3. **Destroying an entity destroys its descendants,** as in Unity's and
   flecs' defaults and every scene editor; to keep a child, reparent it
   first.

## Why

A component and a side list keep the hierarchy out of archetype identity,
so a level with thousands of parents doesn't fragment into thousands of
archetypes, and they need nothing beyond E's storage. Entity fields cover
most relationships games have today with no new machinery; pairs can be
added later without changing them.

Given up, for now: querying by relationship directly, which flecs pairs
offer.

As built (M3 step 6b): `Parent` is a built-in component
(`parentComponent()`); `setParent(child, parent, index)` inserts at a
position among the parent's children (or last), moves a child within its
siblings when the parent is the same, makes it a root for `nullEntity`, and
refuses dead entities and cycles (found by walking up from the new parent)
with `InvalidData`. `childrenOf` returns the ordered list. Destroying an
entity destroys its descendants children-first and removes it from its
parent's list. Writing `Parent` directly isn't supported: it would leave
the children lists out of step.
