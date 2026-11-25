# 0125 — Preview viewports

**Status:** Accepted (2026-11-24); step 8 of editor extensions. Names
marked `*` in docs/naming.md are placeholders until the owner has tried it.

## Context

A game's own editors (the owner's example: an RPG's character editor)
need a view of something apart from the scene: a character's model, lit,
animating, its animations played and scrubbed. The engine could already
make several worlds and draw any of them into a view; nothing ran the
engine's systems on a world other than the scene's, an animation's clock
only advanced while playing, and there was no way to move an animation to
a moment.

## Options

**What moves a preview's world:** the engine's systems on it, on its own
clock; only what the extension asks for; (the owner) the first, with the
clock able to be switched off. **Scrubbing:** a new engine call setting a
layer's time; play and pause only. **A new world:** a default light rig;
empty. **What it shows:** 3D models and prefabs; 3D and 2D.

## Decision

Decided by the owner (2026-11-24):

1. **`PreviewView`***, a public editor control a panel or workspace holds
   (`new PreviewView()`, as `SceneView`), with a `World`* of its own: the
   scene's world isn't touched. `Load(asset)`* puts in a model or prefab
   (instanced as the scene would place one), a texture as a sprite, or a
   tilemap as an entity per layer, and returns its root. `Frame()`* fits
   what's in it: models by their meshes' bounds, sprites by their
   textures'. Its camera (`Camera`*, an `EditorCamera`) orbits with a left
   drag, pans with the middle, dollies with the wheel.
2. **The engine's systems on it, on a clock that can be switched off.**
   Each frame the editor runs, on each preview's world: the animation's
   advance (only while `IsRunning`*, by the frame's time times `Speed`*;
   `Time`* adds it up), posing, and bone attachments, the same calls as
   the engine's Animation systems; then transform propagation, as its
   system does it (missing world transforms added, all placed). Asset
   loads are published once a frame for every world, so it needs no
   system of its own for them. Physics, audio, and the game's systems
   don't run on it.
3. **Scrubbing.** `Animator.SetTime(world, entity, layer, seconds)`* and
   `Animator.TimeOf(world, entity, layer)`* (new native calls): the base
   (-1: its clip, or the state machine's current state by its phase) or a
   layer moved to a moment, held between 0 and its length, its fade
   finished, no events fired for the jump. With the clock off the world
   stays posed where it's put.
4. **A default rig**: `KeyLight`* (a directional light from above, in
   front and to the side) and `FillLight`* (an ambient light), entities
   the extension can change or destroy; and `BackgroundColor`*, what's
   behind everything (the drafted `Background` is every element's own
   fill). The camera starts in front of what's loaded.
5. **3D and 2D.** `Is2D`*: an orthographic camera looking straight at the
   XY plane, x right and y up, panned with a drag and zoomed with the
   wheel, `ViewHeight`* (added while building) world units tall.
6. **Freed with what holds it.** `Dispose()`* frees its world and image;
   the editor calls it when the panel or workspace holding the preview
   closes (an extension stopping closes its panels), or the editor stops.
   `IsDisposed`* (added while building) says so.

## Consequences

- A preview's world version is advanced after each draw, as the frame
  loop does for the scene's world after each system; without it the
  renderer saw nothing change after its first draw.
- A preview made and never put in a panel lives until it's disposed or
  the editor stops.
- `PreviewChecks`* (CTest `launcher.jit.previews`*) puts the Walker in a
  panel's preview, its Walk clip playing on the preview's clock, held with
  the clock off and scrubbed; a sprite in 2D; and the world freed with
  the panel; saving previews.png. LayerTests checks scrubbing natively.
