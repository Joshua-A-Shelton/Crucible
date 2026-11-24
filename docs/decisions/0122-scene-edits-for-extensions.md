# 0122 — Scene edits for extensions

**Status:** Accepted (2026-11-24); step 7a of editor extensions (map mode
rebuilt on the API, split by the owner into 7a, the API's additions; 7b,
map mode moved onto them in an assembly of its own; 7c, the owner's notes).
Names marked `*` in docs/naming.md are placeholders until the owner has
tried it.

## Context

Map mode is to be rebuilt on the public editor API alone (step 7), in its
own assembly, where the compiler proves the API is enough. It makes and
arranges entities (a map and a child per layer), reacts to the editor's
own Duplicate, rename, and Delete (copying, renaming, and offering to
delete its map file), adds Create > 2D > Tilemap to the Hierarchy, and
draws tileset textures. The API had none of these: extensions could only
edit components of entities that already existed.

## Options

**Making entities:** undoable scene calls; only through prefabs. **The
editor's own operations:** events after, plus files offered on delete;
events after only; full before events. **The Hierarchy's Create menu:** a
menu path given the parent; commands with a Hierarchy path. **Textures:**
`editor.Assets.Texture(id)`; a UI image control only.

## Decision

Decided by the owner (2026-11-24):

1. **Undoable scene calls.** `editor.Scene.Create(name, parent)`*,
   `Destroy(entity)`*, `SetParent(entity, parent, index)`*,
   `Rename(entity, name)`*, each one undo step, several made one with the
   history's groups. `Create` makes an entity with only its name (and its
   scene ID): the extension adds its transform, 3D or 2D, and the rest.
   These are an extension's own edits: they raise no events and ask
   nothing.
2. **Full before events.** The editor's own Duplicate, rename (the
   Hierarchy, F2), and Delete raise `Duplicating`*, `Renaming`*, and
   `Deleting`* first, handing a `SceneChange`*: an extension may `Cancel()`*
   it, `Hold()`* it while asking a question (the hold's `Go()`* or
   `Cancel()`* answers), and add work with `Then(action)`* that runs after
   the change in its undo step. The editor waits for every hold; any
   cancel drops the change. Then `Duplicated`* (originals and copies),
   `Renamed`* (the entity, its name before and after), and `Deleted`* (the
   deleted entities' scene IDs) are raised inside the change's undo step,
   so an extension's follow-up edits undo with it. The editor's own map
   question ("Also delete Level 1.crtilemap?": Delete File, Keep File,
   Cancel) is now a `Deleting` handler of its own, holding the change.
3. **A menu path for Create.** `editor.Scene.AddCreate(path, create)`*: an
   item in the Hierarchy's right-click menu at its path ("2D/Tilemap"
   joins the editor's 2D submenu), given the entity clicked or none.
4. **`editor.Assets.Texture(id)`***: a texture asset's GPU texture, acquired
   on first asking and released when the extension stops; null until
   it's ready, or for an asset that isn't an imported texture.

## Consequences

- Reparenting (the Hierarchy's drag, `SetParent`) keeps the world place of
  an entity with a 3D transform; one without (2D, or just made) keeps
  what it has. Fixed here: it used to need a `LocalTransform`.
- Extensions' handlers run guarded, as their other code does; a handler
  that throws is logged and the change goes on.
- `SceneApiChecks`* (CTest `launcher.jit.scene-api`*) runs an extension's
  Create item, a rename it cancels and one it lets through, a Duplicate, a
  Delete it holds and lets go with work after it, one it cancels, a
  texture, and a reload after which only the new code is told.
