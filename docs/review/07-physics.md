# Review 7: physics

Bodies, colliders, joints, queries, character controllers, and hitboxes,
in 3D on Box3D and in 2D on Box2D, behind one contract. Decisions: ADRs
0027, 0075, 0085, 0086, 0090, 0091, 0092, 0093, 0094, 0095, 0096.

## What it does

Entities with a `RigidBody` and a `Collider` (or their 2D twins) are
simulated at a fixed 60 ticks a second and shown smoothly between ticks.
Games query the world (rays, shapes, overlaps), read contacts, move
characters that slide along walls and climb steps, join bodies, and use
hitboxes that follow bones and open with animation windows.

```
 FixedTick (0..4 times a frame)
   PrePhysics:  Physics.Sync      ECS ──► Box3D / Box2D (new bodies, moved
                                          kinematic and teleported ones)
   Physics:     Physics.Step      the physics world steps, on the job system
   PostPhysics: Physics.WriteBack Box3D / Box2D ──► last two tick poses
 Update:        Physics.Interpolate  LocalTransform between them (smooth)
 Animation:     Hitboxes            swept between frames, gated by windows
```

## How it works

### The contract and stepping (ADR 0027, 0075, 0085)

- **Box3D for 3D, Box2D v3 for 2D,** both behind Crucible's physics API:
  game code never calls them, and the physics is a replaceable subsystem
  (a native module can bring its own).
- **Fixed tick:** sync, step, and write back inside the FixedTick phases,
  all native loops over chunks; both libraries run their work on
  Crucible's job system.
- **Interpolation into the transform:** each dynamic body's last two tick
  poses are kept, and every frame its `LocalTransform` is set between them,
  so rendering needs nothing special. Gameplay sees the transform up to a
  tick behind and can ask for the exact tick pose.
- **What the game writes moves bodies by kind:** a dynamic body is
  teleported (velocity kept), a kinematic one moves to the new pose over
  the tick (pushing what it meets).
- **Contacts hold every tick since the last frame,** so a system running
  once a frame misses none.
- **Physics is gameplay:** it steps only while playing, not while editing.

### Colliders and joints (ADR 0086, 0092, 0094, 0095)

- **Shapes:** box, sphere, capsule, and a model's mesh or convex hull
  (cooked at import when the model's Collision setting asks, or built at
  run time from game geometry). In 2D: box, circle, capsule, polygon,
  **chains** (a JSON asset of points, edited in the viewport), and
  tilemaps' merged outlines.
- **Joints:** fixed, hinge, distance, ball-socket, swing-twist (3D);
  fixed, hinge, distance, slider (2D). A joint belongs to its entity and
  is measured as that entity moving relative to the other (or the world);
  limits are off unless set.

### Queries and characters (ADR 0090, 0091, 0094)

- **Queries** (rays, shape casts, overlaps) take a filter: layers, an
  entity to ignore, whether triggers count (they don't by default). They
  see the last tick's world.
- **The character controller** collides and slides, rides a step's height
  above the ground so steps are climbed, follows slopes down, applies
  gravity (scaled per character), and jumps by `Launch`. A kinematic
  capsule follows each character, so bodies collide with it and it pushes
  them. Root motion feeds it.

### Hitboxes (ADR 0096)

- **Bone attachments** put an entity on a bone of the Animator above it
  (a sword in the hand, a collider on a fist).
- **Hitboxes** (`Hitbox`, `Hitbox2D`) are shapes on their own entities,
  active while enabled and while their Animator has their window open;
  they're swept from the last frame's position to this one's, so a fast
  swing doesn't pass through a target between frames.

## Why it was built this way

- **Proven libraries, wrapped:** physics is hard to get right; Box3D and
  Box2D are by the same author, so 3D and 2D behave alike, and wrapping
  them keeps Jolt (3D) a possible swap.
- **Fixed tick with interpolation** gives stable simulation and smooth
  motion at any frame rate.
- **Native loops, no per-body C# calls,** so physics costs the same
  whatever the game's code does.
- **Swept, windowed hitboxes** because the action RPG's combat depends on
  hits landing exactly when the animation says.

## What I'd question

1. **Box3D is young.** It's pinned at a master commit (its only release,
   v0.1.0, is months behind), and ADR 0027 set a checkpoint before the
   action RPG: ragdoll-capable joints, the character mover's maturity, and
   its release cadence, with Jolt as the fallback. Worth deciding when
   that checkpoint happens, since switching gets harder as games use it.
2. **No physics while editing.** Bodies don't move and queries find
   nothing in the editor (the world isn't stepped). Placing props by
   dropping them, or checking a character's slope limit, needs Play.
   A "simulate" mode in the editor (physics only, no game code) would
   help level building.
3. **Joints have no motors, springs, or breaking.** Doors that swing shut,
   suspension, or a bridge that snaps need them; both libraries support
   them, so it's surface Crucible hasn't exposed.
4. **No ragdoll workflow.** Swing-twist joints exist, but making a ragdoll
   from a skeleton (bodies per bone, joints between them, blending from
   animation) is all by hand.
5. **Two parallel APIs.** 3D and 2D each have bodies, colliders, joints,
   queries, characters, and hitboxes, with their own types and systems.
   Consistent, but every feature is built twice; worth keeping an eye on
   as both grow.
