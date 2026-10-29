# Drawing meshes

A learning note for M5 step 5 (ADR 0051): from entities with a mesh to
pixels, and what measuring the "skip unchanged" idea taught.

## Culling: don't draw what the camera can't see

The camera sees a pyramid-shaped region, the **frustum**, bounded by
planes. The view-projection matrix encodes them; adding and subtracting
its columns gives each plane directly. Each mesh's bounding box is moved
into world space and tested against every plane: if the whole box is
behind any one plane, the mesh is skipped. One job per chunk tests a
chunk's worth of entities, so 100k instances cull in well under a
millisecond.

## Level of detail by pixels

The importer stored, for each simplified version of a mesh, how far (in
meters) it strays from the original. Divide that by the distance and scale
by the screen's height, and you know how many pixels the error covers. The
renderer picks the simplest version whose error stays under one pixel: a
far-away statue can be much simpler without anyone seeing a difference.

## Instancing: one draw for many copies

A hundred identical crates are one draw call with an instance count of
100. Each instance needs its own position, so the shader looks it up:
- the draw's first instance (`SV_StartInstanceLocation`) plus the
  instance number points into a list of entity indices;
- the entity index points into a buffer of world matrices.

The CPU builds the list each frame from what culling found. Batches group
instances by model, mesh, and level of detail.

## Vertex pulling

The classic way to feed vertices is the input assembler: tell the GPU
the format of each attribute, and it fetches them. Crucible instead reads
vertices in the shader itself, from the model's buffer by byte offset. Any
mix of attributes (with or without UVs, colors, ...) then works with one
shader, compressed formats like half floats decode however we like, and
the vertex layout lives in one place: the file format.

## Depth prepass

Shading a pixel that something nearer later covers is wasted work. The
prepass draws every mesh with depth only, which is cheap. Then the shaded
pass tests against that finished depth buffer, so each pixel is shaded
once. With reverse-Z (ADR 0050), nearer is greater.

## MSAA and the resolve

Multisampling stores several samples per pixel at triangle edges, so edges
come out smooth without blurring textures. The samples must be averaged
into one pixel at the end. Averaging bright HDR values before tonemapping
brings back jagged edges: one very bright sample dominates the average.
So each sample is tonemapped first, then averaged. For now, "tonemapping"
is a clamp to [0, 1].

## Measuring "only what changed"

Change versions are kept per chunk, not per entity, which is what makes
them cheap. The measurement showed what that costs:

- **Moving things scattered among still things:** with 1% of entities
  moving, nearly every chunk contains one, so nearly everything counts as
  changed and nothing is saved.
- **Moving things in chunks of their own** (an archetype of their own,
  which physics bodies and characters usually have): only their chunks
  are sent, about a quarter cheaper in that measurement.

The lesson: an optimization that depends on data layout only pays when
the layout cooperates. Measure with realistic layouts before keeping it.
