# Collision meshes: what physics sees, and when it's built

A learning note for M9 step 2 (ADR 0086).

## Two kinds of shape from a model

- **A triangle mesh** is the model's own surface: every triangle collides.
  It's exact, but only on the outside of each triangle, and expensive to
  test against moving shapes. Box3D only lets meshes collide on **static**
  bodies, which suits level geometry: floors, walls, stairs.
- **A convex hull** is the tightest convex shape around the points: like
  shrink-wrap with no dents. Convex shapes are what physics engines collide
  quickly and robustly, so they're the shape for things that move, like
  crates, barrels and debris. A hull loses the dents (a mug's inside, an
  arch's gap), and Crucible caps its vertices (32 by default), so a detailed
  model's hull is simplified, rounding off small parts.

## Why cook at import

Turning triangles into something fast to query means building a
**bounding-volume tree**: boxes inside boxes, so a ray or a falling ball
tests a few boxes and then a few triangles, instead of all of them. For a
big level that takes real time. Doing it in the editor once, at import,
and saving the result means a game loads it straight into memory with no
work.

Box3D's data is designed for this. Each blob is self-contained: a header
with the byte count and offsets to its parts, rather than pointers. So it
can be written to a file and used exactly where it lands, as long as it's
16-byte aligned.

The price is that the saved data is in Box3D's own format. Each blob
carries a format version. When a new Box3D changes its format, old data is
refused and models must be reimported. That's why the importer's version
moves with the Box3D pin.

## When the model wasn't imported with it

Loading prepared data is the normal path: it costs nothing at load. But a
collider can also say **Generate If Missing**: if its model wasn't imported
with that kind of collision, the data is built from the model's own
triangles when the collider loads, just as the importer would have. That's
handy while trying things out, or for a model that's only sometimes used
for collision. It's off by default, so a forgotten import setting shows up
as a warning (and a notice in the inspector) rather than as a silent cost
at load; a build that takes more than 10 ms says so in the Log, since
that's a sign the model should be prepared at import instead.

The model's Collision import setting can be changed in the inspector (select
the model) or straight from the asset browser: right-click, Collision.
Reimport on its own imports with the settings the model already has, so it
won't add collision data that the setting doesn't ask for.

## Building at run time

Some geometry doesn't exist until the game runs: generated terrain, a wall
that's been blasted apart, a level the player builds. For those,
`createCollisionMesh` and `createCollisionHull` do at run time what the
importer did. They return an ID a collider uses just like a model's.

## Which way a triangle faces

Mesh collision is **one-sided**: a ball falling onto a triangle from its
back passes straight through. So the winding matters: the order of a
triangle's corners decides which side is the front.

The usual rule is that the front is where the cross product (b − a) × (c −
a) points. The formula is the same in left- and right-handed spaces, but
seen on screen it looks like opposite turning directions: counter-clockwise
in a right-handed world, clockwise in a left-handed one like Crucible's.

When the importer mirrors a glTF model into Crucible's space, it also swaps
two corners of every triangle. Mirroring flips the cross product, and the
swap flips it back. The result is that Crucible's "clockwise" front faces
are exactly the ones Box3D's default winding expects.

That's easy to get wrong by reasoning about "clockwise" alone, and it was
wrong here at first. The test that caught it dropped a ball on a mesh and
watched it fall through.
