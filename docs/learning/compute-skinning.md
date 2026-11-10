# Compute skinning: deform once, draw many times

A learning note for M8 step 3 (ADR 0080).

## Where skinning usually happens

The textbook place to skin is the vertex shader: each vertex reads its four
joints' matrices, blends them, and transforms itself. It works, but every
pass that draws the mesh does it again. A character in Crucible is drawn by
the depth prepass, up to four shadow cascades, shadow tiles for nearby
lights, the opaque pass, the outline pass, and, in the editor, the pick
pass. That's the same skinning done eight or nine times a frame.

It also means every material needs a skinned variant of every pass's vertex
shader. With compute pre-skinning, one compute pass writes the deformed
vertices to a buffer once per frame, and every later pass reads that buffer
exactly as it reads a static mesh's. The materials don't know skinning
exists, apart from where they fetch four of their streams from.

## The palette

Each joint contributes one matrix, the *palette* entry:

    inverse bind × posed joint × (character root → mesh)

The first two are the skinning formula from step 1's note: undo the bind
pose, then apply the current one. The last one moves the result from the
character's space into the mesh entity's, so the mesh's ordinary world
matrix can draw it. A vertex blends its joints' palette entries by its
weights and transforms itself once. Because the mesh's own transform is
undone and redone, it cancels out: a skinned mesh goes where its joints
are, wherever its node is, just as glTF specifies.

## Keeping the formats

The compute shader writes positions as three floats and normals and
tangents in the same 16-bit octahedral encoding the importer uses. Writing
them back in the source formats means the draw shaders read them with the
code they already have. It costs an encode per vertex, but it's far less
memory traffic than floats, and there's only one vertex fetch path to keep
correct.

## Culling something that moves

A static mesh's bounds come from its file. A skinned mesh's vertices go
wherever the pose takes them, so its file bounds lie. The fix is cheap: at
load, measure for each joint how far its vertices are from it in the bind
pose (its *reach*). However the pose bends, a vertex stays within its
joint's reach of that joint, and a vertex blended between joints stays
within the union of their spheres. So each frame, the bounds are the posed
joints' positions, each grown by its reach. That's a dozen numbers per
character, not a pass over the vertices.

## One render graph, two kinds of resource

The skinned vertices live in a transient buffer of the frame's render
graph. The Skinning pass declares that it writes it; every pass that draws
meshes declares that it reads it. From those declarations the graph places
the barrier between the compute writes and the vertex reads. Nobody has to
remember to add it, which is exactly what a render graph is for.

## Testing what you can see

A two-joint quad makes a good test: bind it flat, rest its second joint 1.5
m up, and the quad's right edge rises while its left stays, shearing it into
a parallelogram. The test checks pixels where skinning on the CPU says the
quad now is and isn't. When a check like that fails, it's worth redoing the
geometry by hand before suspecting the shader: the first failures here were
in the test's own arithmetic.
