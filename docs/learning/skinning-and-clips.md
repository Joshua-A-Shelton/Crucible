# Skinning and clips: what a character is made of

A learning note for M8 step 1 (ADR 0078).

## Skinning in one formula

A skinned mesh is modelled once, in one pose: the *bind pose*, usually
standing with arms out. Every vertex says which joints it follows and how
much (its *weights*). To draw the character in another pose, each vertex is
moved by each of its joints and the results are blended:

    skinned = Σ weightᵢ · (vertex · inverseBindᵢ · jointWorldᵢ)

Read it right to left as "undo, then redo". The *inverse bind matrix* takes
the vertex from the mesh's space into the joint's own space, as the joint
was in the bind pose. The joint's world matrix now takes it back out,
wherever the joint has moved to. If nothing moved, the two cancel and the
vertex stays put. That's exactly what the "rest pose skins every vertex to
where it was" test checks.

## Why a joint's weights are bytes

Weights are fractions that sum to 1, so 8 bits each (0 to 255) lose very
little: a 1/255 step is far below what anyone can see on a character. The
importer keeps the strongest few (4, or 8 for faces), drops the rest, and
renormalizes so the bytes sum to exactly 255. Without that last step a
vertex whose weights sum to 254/255 would shrink towards the origin by
0.4%, which at a few meters from the origin is a visible crack.

Joint *indices* are 16 bits, because 256 joints isn't enough for a
character with a face rig and fingers.

## Clips: curves become samples

Animation tools store curves: keys at uneven times with tangents between
them. Playing curves means finding the right key with a search, then
evaluating a spline. Crucible resamples every curve at a fixed rate when it
imports, so playing a clip is an index and a lerp: sample `t × rate` and the
one after it.

Most of a clip's size is rotations, one quaternion per joint per sample.
*Smallest three* stores a unit quaternion in 48 bits plus 2: drop the
largest of the four components (the other three are then each within
±1/√2), store the three, and rebuild the fourth from `x² + y² + z² + w² = 1`.
Negating a quaternion gives the same rotation, so the dropped one is always
made positive first.

Translations and scales are stored within each track's range: a foot that
only ever moves between 0 and 0.4 m gets all 65536 steps for those 40 cm. A
channel that never changes (most joints never scale) is stored once.

## Testing against someone else's answer

The best check of a skinning importer is a pose someone else computed. ufbx
ships OBJ exports of some of its samples at given frames, written by the
tool that made the file. The clip test poses the imported 3ds Max skin with
the imported clip, the way the runtime will, and compares the result with
Max's own export of frames 5 and 15. It found that the reference export was
Y-up in centimeters, not Z-up in inches as Max's other samples are: when a
comparison is wildly off, check the reference's conventions before the code.

## Sub-assets

One source file can make several assets: a model's clips, later perhaps an
atlas's sprites. Each needs an ID that survives reimports, or every scene
referencing "Walk" breaks when the model is re-exported. The ID can't come
from the content (that changes) or from an index (clips get reordered), so
it's kept by name in the parent's sidecar, which is checked in. The cached
file's key comes from the parent's key plus the name, so a sub-asset is up
to date exactly when its parent is, with no bookkeeping of its own.
