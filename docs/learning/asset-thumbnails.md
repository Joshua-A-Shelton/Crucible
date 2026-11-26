# Asset thumbnails

How the editor's Assets panel shows models, prefabs, materials, and
tilemaps as small pictures (ADR 0132): rendering offscreen, getting a
clear background out of a renderer that always fills one, and caching by
what an asset is made from.

## Rendering where nobody sees

A thumbnail is an ordinary frame, drawn somewhere other than a window:

- a world of its own, holding only the asset and two lights;
- an **offscreen surface**, an image the renderer draws into like a
  window's, but never shows;
- a **readback**: the image copied from GPU memory into memory the CPU can
  read.

A readback makes the CPU wait for the GPU to finish, which games avoid
every frame. A thumbnail is drawn once and kept, so one wait each is fine.

Wait until it's all there before drawing. Meshes and textures load in the
background, and a model whose textures haven't arrived isn't drawn yet.
The engine already answers "does anything in this world still wait for a
load?", the same question a scene asks before its first frame.

## A clear background from two pictures

The renderer always clears to a color, and after tone mapping no alpha
survives. So the editor draws the thumbnail twice, once on black and once
on white:

- where both pictures agree, the pixel is the model: **opaque**;
- where they differ by the full black-to-white step, it's background:
  **clear**;
- in between, at an antialiased edge, the difference says how much
  background shows through.

So `alpha = 1 − (white − black) / step`, and the model's own color is
`black / alpha`. Do the arithmetic in linear light, not on sRGB bytes, or
edges come out too dark or too light. Film and video call this
**difference matting**.

## Smaller copies, filtered by alpha

A 256px thumbnail drawn in a 64px tile skips three pixels of every four
and shimmers. **Mipmaps** fix that: copies at half the size, a quarter,
and so on, each averaged from the one above. On a clear background,
average by alpha. A clear pixel's color is meaningless (black, here), and
counting it equally would draw a dark outline around everything.

## Caching by what it's made from

A thumbnail must be drawn again when its asset changes, and not
otherwise. The asset database already gives every import a **key**, a
hash of the source file, its settings, and the importer's version. A
thumbnail's key combines that key with the keys of everything the asset
names: its textures, a prefab's models, a tilemap's tilesets and their
sheets. Change any of them and the key changes, so the old file is simply
never asked for.

A key in the file name also means there's no "is this stale?" check: a
file either exists under the current key or it doesn't.

## One material, many meshes

Showing a material means drawing something with it. The renderer batches
instances that share a mesh and draws them in one call, with the mesh's
own materials. A **material override** says "this instance, this
material", so the renderer groups instances by mesh *and* material. Two
overridden the same way still share a draw. The sphere the editor shows
materials on is just a mesh with an override, the same tool a game uses
to repaint a car.

## Try it

1. Open the Diorama in the editor and open Assets ▸ Models: each model
   shows itself after a moment.
2. Drag the slider at the right of the panel's bar: the tiles grow, and the
   badge in the corner says what kind each is.
3. Look in the project's `Cache/Thumbnails/`: one file per thumbnail,
   named by its key. Delete one and come back to the folder: it's drawn
   again.
