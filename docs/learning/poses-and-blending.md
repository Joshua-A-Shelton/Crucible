# Poses and blending: from clips to joint matrices

A learning note for M8 step 2 (ADR 0079).

## A pose, twice

A pose is where every joint of one character is, and it's kept in two
forms because they're good at different things:

- **Local:** each joint's position, rotation, and scale relative to its
  parent. Clips store this, and blending works on it: blending two elbow
  angles gives an elbow angle in between.
- **Model space:** a matrix per joint relative to the character's root. This
  is what skinning and gameplay need ("where is the hand?").

The *hierarchy pass* turns one into the other. Because joints are stored
parents first, one loop does it: `model[j] = local[j] × model[parent]`. Each
joint waits for its parent, which is why this part stays scalar while
sampling and blending go wide.

## Sampling

A clip stores samples at a fixed rate (ADR 0078). To find a pose at time
*t*, take the samples just before and after `t × rate` and interpolate:
positions and scales linearly, rotations by *normalized lerp* (blend the
four numbers, then rescale to length 1). Slerp would follow the sphere
exactly, but between samples 1/30 s apart the two are indistinguishable,
and nlerp has no trigonometry.

One catch: `q` and `−q` are the same rotation. Blending `q` with `−q`
naively averages to zero. So before blending, flip the second one if the two
point apart (negative dot product). That's "taking the shorter way".

## Blending is the same operation everywhere

A crossfade, an override layer, and a masked layer are all the same thing:
a weighted blend of two local poses, joint by joint. Only the weight
differs:

- a **crossfade** ramps one weight from 0 to 1 over the fade time;
- an **override layer** uses its weight, times the mask's weight for that
  joint;
- a **mask** is just a per-joint weight table, built once from "this joint
  and everything below it" entries.

An **additive** layer is different: it adds a *difference*. The layer's
clip is compared with its own first sample, and only the change is applied
on top of whatever is below. A "breathing" clip authored on a standing pose
then works on top of walking, sitting, or anything else.

## Structure of arrays

Most per-joint math is the same operation repeated for every joint, which is
what SIMD is for. Instead of storing joints one after another (each with x,
y, z, w), a pose stores blocks of 8 joints with all their x values together,
then all their y values, and so on. One instruction then blends eight joints
at once. The data layout is the optimization; the code reads almost like the
scalar version, with `QuaternionWide` in place of `Quaternion`.

The tests keep the plain scalar version and compare every joint's matrix
against it. That's a good habit for any SIMD code: the simple version is the
specification, and the fast one has to match it.

## Two systems, one for each mode

Advancing time and computing the pose are separate systems. While editing,
only the second runs, so a character holds its clip's first sample: what
you place is what Play starts from. While playing, both run, and the clock
moves. Splitting "change the state" from "show the state" this way comes up
again and again in editors.

## Seeing what you can't draw yet

Until the mesh is skinned (step 3), a pose is invisible. Debug lines (one
line per bone, from each joint to its parent) make it visible with almost
no machinery: a list of lines anyone can add to during a frame, drawn over
the image at the end. Being able to draw a line from anywhere is one of the
most useful debugging tools an engine can have.
