# Transforms and cameras

A learning note for M5 step 4 (ADR 0050): from an entity's place relative
to its parent to a pixel's depth.

## Local and world

An entity stores where it is **relative to its parent**: a position, a
rotation, and a scale (`LocalTransform`). That's what editors and gameplay
change. The GPU needs where it is **in the world**: one 4x4 matrix
(`WorldTransform`) that takes a point on the model straight to world
space.

With row vectors (Crucible's convention, as in DirectX and System.Numerics),
a point is transformed as `p * M`, and matrices multiply in the order things
happen:

    local  = scale * rotation * translation
    world  = local * parent's world

A hand's world matrix is its local matrix, then its arm's world matrix, and
so on up to a root. Parents must be finished before their children, so
the propagation system walks each root's subtree from the top down. Each
entity's local transform becomes a matrix and is multiplied by its
parent's world matrix. Different roots' subtrees share nothing, so they're
spread across the job system.

## Only what moved

Most things in a level don't move most frames, so the system skips them.
Each chunk column has a change version (ADR 0034), and the frame loop
gives every system the version of its previous run. The walk compares
versions: an entity whose chunk's local transform or parent changed since
then is recomputed, and so is everything below it, whether its own chunk
changed or not. The rest keep last frame's matrix.

Two lessons from measuring it:

- **Skipping the math isn't skipping the work.** The walk still visits
  every entity to look at versions, and those scattered lookups cost more
  than the matrix multiplies. With nothing moving, a frame is only about a
  third cheaper.
- **The bigger win is downstream.** A world matrix is written only when it
  actually changes, so its column's version means "this chunk moved". Step
  5 can then skip re-uploading instance data for everything that stood
  still.

For one entity right now (say, a hand that was just attached),
`computeWorldTransform` multiplies up through its ancestors without
waiting for the system and without storing anything.

## From world to screen

The camera adds two more matrices:

- **View** moves the world so the camera sits at the origin, looking along
  +Z. It's the inverse of the camera's own world matrix (scale removed: a
  camera has no size).
- **Projection** squeezes what the camera sees into the GPU's clip space:
  x and y from -1 to 1 across the screen, and a depth.

A shader computes `mul(float4(p, 1), viewProjection)` and the GPU divides
by the result's w. For a perspective camera, w is the distance ahead, which
is what makes far things small.

## Why reverse-Z

After the divide, a perspective projection's depth crowds near the camera:
half of all depth values are used before twice the near distance. Standard
Z then stores those values as floats near 0, where floats are densest, and
leaves far-away depths with few distinct values. Distant surfaces z-fight.

**Reverse-Z** stores the near plane at 1 and far at 0, so the float's
density near 0 lands on the distances that need it. The two effects nearly
cancel, and precision is roughly even at every distance. With precision no
longer depending on it, the far plane can go to infinity: depth is simply
`near / distance`. The cost is remembering the convention: depth buffers
clear to 0 and the test is "greater", not "less".

## Getting the view to shaders

Each frame the renderer fills a `ViewConstants` struct (the matrices, their
inverses, the camera position, time, and viewport size) and writes it into
a small CPU-writable buffer. Shaders read it as the root constant buffer.

The CPU is building frame N+1 while the GPU may still read frame N's
buffer, so there's one buffer per frame in flight, used in turn. Before
reusing one, the render graph's `beginFrame` waits until the GPU has
finished the frame that last used it.

## Late latching, later

Input is sampled at the start of a frame, but the GPU draws the frame some
milliseconds later. **Late latching** rewrites the camera in that buffer at
the last possible moment before the GPU reads it, for fresher input (VR
relies on it). Crucible writes the camera at a single point so this can be
added later. Today it would gain almost nothing, because a frame is
submitted as soon as it's recorded.
