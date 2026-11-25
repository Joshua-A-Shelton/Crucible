# 0126 — Custom inspectors

**Status:** Accepted (2026-11-24); step 9 of editor extensions. Names
marked `*` in docs/naming.md are placeholders until the owner has tried it.

## Context

The Inspector (ADR 0069) makes each component's section from its type's
generated field table, editing the component's JSON. A game's editors
want their own: a character's stats as something better than a list of
numbers, a damage range as one control, a curve for a float. The editor
itself had two special cases written into the Inspector's code (a
Collider's missing-collision notice, ADR 0087; a Chain collider's Edit
Points, ADR 0095), as map mode once was in the shell.

## Options

**What a drawer can replace:** a component's section; every field of a
type; a field marked with an attribute. **Mixing with the generated
fields:** the drawer builds its UI and places generated editors where it
wants; it replaces everything; it adds above or below. **Values:** typed,
with undo; the JSON. **The editor's own special cases:** moved onto
drawers; left.

## Decision

Decided by the owner (2026-11-24):

1. **All three targets.** `editor.Inspector`* (`EditorInspector`*):
   `AddComponentDrawer<T>(create)`* draws a component's whole section
   body; `AddFieldDrawer<T>(create)`* every field of a `[Serialized]` type,
   wherever it appears (matched by the type's generated field table);
   `AddDrawer<T>(name, create)`* the fields marked `[Drawer("name")]`*,
   which the generator puts in the field table (`SerializedField.Drawer`*).
   The last registered wins; each is taken out when its extension stops,
   and the Inspector is made again when drawers change, so a reloaded
   extension's drawers replace the old ones and the old code unloads.
2. **The drawer builds; the generated editors on call.** `Drawer<T>`*
   builds its UI once (`Build(context)`*) and follows the value each frame
   (`Update(context)`*). `DrawerContext<T>`*`.Default(field)`* gives the
   generated editor for any of its fields, placed wherever the drawer puts
   it and kept up to date there (a field type's or a marked field's own
   drawer included).
3. **Typed, with undo.** `Value`* is the value as its C# type, read again
   each frame; `Set(value, title)`* is one undo step, and
   `BeginChange(title)`*/`EndChange()`* make several one (a drag).
   `IsOverridden(field)`* says whether a prefab instance's field (or, with
   none, the whole component) differs from the prefab's; `Entity`*,
   `Editor`*, and `Label`* the rest. T is a component or `[Serialized]` type
   (through the generated serializer), or for a marked field a plain value:
   a number, a bool, a string, a vector or quaternion, an asset ID.
4. **The editor's own special cases moved onto drawers**, registered
   through the same API: `ColliderDrawer`* (the generated fields with the
   notice under the model, its fields marked while it shows) and
   `Collider2DDrawer`* (Edit Points under the Chain).

## Consequences

- A drawer whose code throws is logged; if it fails while building, the
  generated fields are shown instead.
- The editor's drawers still read some of the editor's insides (the
  database's model contents, the chain editing state); the drawer API
  itself is public.
- `DrawerChecks`* (CTest `launcher.jit.drawers`*) builds game code with a
  Stats component holding a DamageRange and a `[Drawer("Percent")]`
  float, and editor code drawing each: the section by its drawer, the
  generated editors placed, the field type's and the marked field's
  drawers, sets as undo steps and a change as one, the prefab override,
  a reload, and the editor's own Collider drawer.
