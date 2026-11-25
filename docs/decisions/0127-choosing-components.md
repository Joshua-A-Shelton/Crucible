# 0127 — Choosing components

**Status:** Accepted (2026-11-25); found by the owner while trying step 9
of editor extensions, tried by hand by the owner. Names settled by the
owner as drafted (2026-11-25; docs/naming.md).

## Context

The Inspector's Add Component was a drop-down menu of every saved
component's type name (ADR 0069). With the engine's own and a game's
components there were more than the menu had room for, and menus didn't
scroll; a flat list of names says little about what each does either.

## Options

**What replaces the menu:** a searchable popup under the button; a
dialog; submenus with scrolling. **Groups:** a category attribute; the
namespace; none, search only. **Menus in general:** scroll when they
don't fit; leave them.

## Decision

Decided by the owner (2026-11-25):

1. **A dialog**, `ComponentPicker`, over the main window: a search field
   (focused when it opens), a list of every saved component the entity
   hasn't, with Cancel. Typing filters across the groups by name,
   category, and description; Down moves into the list; a click or Enter
   adds the component and closes it (Enter in the search field, the first
   match); Escape or Cancel closes it. (A click adds, as a list item's
   activation does; not a double-click.)
2. **Grouped by a category attribute**: .NET's own
   `System.ComponentModel.CategoryAttribute` names a component's group and
   `DescriptionAttribute` gives the line shown under it, so there are no
   new attributes to learn. The engine's components are in Scene,
   Rendering, Lights, Physics, Physics 2D, Animation, Audio, and 2D; a
   game's (or a plugin's) without a category go under its assembly's
   name.
3. **Menus scroll** when they don't fit: a menu's items are in a scroll
   view, a popup beside an anchor is measured against the room on the
   larger side (or the whole root when neither has a third of it), and
   the item moved to by the keys stays in view.

## Consequences

- The Inspector's names keep a capital after a digit with it: "SpotLight2D"
  is "Spot Light 2D" (it was "Spot Light 2 D").
- A scroll bar shorter than its thumb's minimum no longer throws (a menu's
  first layout found it).
- launcher.jit.inspector opens the dialog, checks its groups, search, and
  keys, and saves inspector.add-component.png; MenuTests scroll a menu of
  80 items.
