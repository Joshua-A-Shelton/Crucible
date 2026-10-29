# The render graph and the render schedule

A learning note for M5 step 2 (ADR 0048): why a frame is described before
it's recorded, and how Crucible lets many pieces of code share one frame.

## The problem a render graph solves

With D3D12 and Vulkan, the application does work the driver used to do:

- **Barriers.** Before a texture written as a render target is sampled, the
  GPU must finish writing, flush caches, and change the texture's layout. A
  missing barrier is a race that may only show on some GPUs; an extra one
  costs time.
- **Queues.** Compute work on a second queue needs a semaphore before the
  graphics queue may read its result, and the textures must be in layouts
  that queue allows.
- **Memory.** A bloom chain's half-size textures live for a millisecond;
  giving each its own memory wastes hundreds of megabytes that other
  short-lived textures could reuse.

Doing this by hand in every pass doesn't scale: each pass would have to
know what every other pass did to each resource. A **render graph** turns
it around: each pass only *declares* what it uses ("I sample this texture,
I render to that one") and records its draw calls. Before anything runs,
the graph sees the whole frame and works out every barrier, semaphore, and
memory placement from those declarations.

Slag's graph (Slag ADR 0072) has one rule that keeps it predictable: **it
never reorders or removes passes.** Declaration order is execution order,
so what you write is what runs.

## Building a frame every frame

The graph is rebuilt each frame, in four calls:

1. `beginFrame`, then declare: import the surface's image, add each pass
   with a *setup* function (its declarations, run now) and a *record*
   function (its commands, stored for later).
2. `compile`: work out barriers, submits, and transient memory.
3. `recordPass(i)` for every pass. Each pass gets its own command buffer and
   all its barriers are known in advance, so passes record in parallel.
   Crucible runs them on the job system.
4. `execute`: submit everything in order, with the semaphores between
   queues.

Rebuilding costs a little CPU each frame. In exchange, the frame can change
freely (a window resized, a feature switched on) with nothing cached to
invalidate.

## Steps, not passes

A game's frame is more than a list of GPU passes: "shadows" is one feature
but one pass per cascade; "bloom" is a chain of downsamples and upsamples.
Crucible's **render schedule** is a list of named **render steps**, each of
which adds however many graph passes it needs. Plugins edit the schedule by
name, as they edit frame phases: insert a step before "Post", replace
"Sky", remove "Outlines". Because a feature is one step, moving or replacing
it moves all of its passes together.

## The blackboard

Steps need each other's results: the opaque step renders into a color
texture that post-processing reads. Passing handles directly would tie
each step to its neighbours. Instead, steps share through the
**blackboard**: one step publishes a handle under a key ("SceneColor"), a
later step reads it by the same key. Neither needs to know the other
exists, so a plugin can insert a step between them that reads the color
and publishes a modified one under the same key.

Keys are registered by name once, then used as small numbers, so a lookup
each frame is an array index, not a string comparison. Reading a key no
earlier step published is a programming error: it's reported with both
names and the frame fails, rather than drawing with a missing texture.

## Where the shaders live

A step like Triangle needs its shader at run time. The engine's own shaders
are compiled by shaderfax during the build and **embedded in the core
library** as byte arrays, so they can't be missing or out of date, and no
code ever searches for a shader folder. That needed one build-order
trick: shaderfax uses parts of the core, and the core now contains what
shaderfax produces. The core is built in two halves: shaderfax links the
first, the shaders are compiled, and the second half (the renderer, with
the shaders inside) is built after.
