# Physics queries: asking the world what's there

A learning note for M9 step 3 (ADR 0090).

## Three questions

Games ask the physics world three kinds of question:

- **A raycast:** "if I draw a line from here, what does it hit first?" Line
  of sight, bullets, what the mouse points at, whether there's ground below.
- **A shape cast:** the same, but sweeping a solid shape (a sphere, a box, a
  capsule) instead of a thin line: "would a sword-sized box moving this way
  hit something?" A ray can slip through a gap a body couldn't.
- **An overlap:** "what's inside this shape right now?" An explosion's
  radius, everything within reach.

## Why ask the physics engine

The physics engine already keeps every collider in a **bounding-volume
tree** (boxes inside boxes, see collision-meshes.md) so it can find what
might touch quickly. A query uses the same tree: a ray only tests the few
colliders whose boxes it passes through, not all of them.

## Filtering

Most queries shouldn't hit everything. Three filters cover most needs:

- **Layers:** the query hits only colliders on its layers. Put the scenery
  on one layer and the enemies on another, and a ray can look for either.
- **Ignore one entity:** a ray fired from inside a character would hit the
  character first. Leaving out the caster fixes that without giving every
  character a layer of its own.
- **Triggers:** trigger colliders (pickups, zones) report contacts but
  aren't solid, so queries skip them unless asked.

## The last tick, not this frame

Physics runs in fixed ticks (fixed-step-physics.md). Between ticks, the
physics world holds where everything was at the last one. A query asks
that world, so if your code moved a body this frame, a query sees the body
where it was until the next tick picks up the move. That's what most
engines do by default; it keeps queries cheap and consistent with what
physics simulated.

## A ray that starts inside something

If a ray starts inside a collider, there's no surface between it and the
outside to hit going in, so Crucible doesn't count that collider: the ray
goes on to the next. A swept shape that starts already touching something
is different: it's blocked from the start, so it hits at distance 0.
