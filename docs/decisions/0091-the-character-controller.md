# 0091 — The character controller

**Status:** Accepted (2026-11-11); M9 step 4 built. Names settled by the
owner as drafted (docs/naming.md); names added while building are marked *.

## Context

The contract (ADR 0075) has `CharacterController` (a capsule: radius,
height, step height, slope limit, layers, mask) and `moveCharacter`, which
the Animator's root motion calls (ADR 0081); until now it moved straight
through everything. ADR 0027 chose Box3D's character mover: a capsule
outside the simulation, moved by `b3World_CastMover`, with
`b3World_CollideMover` gathering the planes it touches, `b3SolvePlanes`
settling it clear of them, and `b3ClipVector` for velocities. Box3D's
sample keeps the capsule floating above the ground on a downward ray.

## Decision

Decided by the owner (2026-11-11), after comparing Unity, Godot, Jolt,
Unreal, and Box3D:

1. **The engine applies gravity**, scaled by a new
   `CharacterController.gravityScale` (1 by default; 0 leaves moving up and
   down to the game): each character keeps a falling speed between moves
   (not saved), landing stops it, and on the ground it follows the ground
   down slopes and steps. So root motion, which has no gravity in it, walks
   off a ledge and falls. `launchCharacter(world, character, upwardSpeed)`
   (C# `CharacterController.Launch`) for jumps; `CharacterMove.fallSpeed`
   reports the vertical speed after a move.
2. **Steps by riding above the ground:** the capsule rides a step height
   above the feet, so anything lower is stepped over, and a short sphere
   cast down puts the feet on the ground each move.
3. **Bodies meet a kinematic capsule:** each character has one following
   it, so bodies collide with it, it pushes bodies it walks into, queries
   hit it, and contacts report it.
4. **The entity's position is the feet:** the capsule's bottom, where a
   model's root usually is.

## How it works

- **A move.** Off the ground (or going up), the falling speed grows by
  gravity times the scale over the call's seconds; the target is the
  displacement plus that fall. Up to five times: the capsule (its bottom
  cap a step height and a radius up) gathers the planes it touches, solves
  the move against them, and casts the solved move, going as far as it
  can. A plane too steep to walk on (its normal's up part below the slope
  limit's cosine) is made vertical first, so the solver slides along it
  rather than up it. A head that stops short going up ends the rise.
- **The ground.** Not going up, a sphere a little smaller than the
  capsule is cast down from its bottom cap, as far as the feet ride (and,
  if it stood before, a step height more, to follow stairs and slopes
  down). Walkable ground there puts the feet on it, grounded, the fall over.
  Over an edge the sphere meets the corner, too steep to stand on, so a ray
  straight down from the middle looks for the step below. Steep ground
  isn't standing: a character on it falls, sliding down.
- **Pushing.** Dynamic bodies among the planes get Box3D's sample impulse:
  enough along each plane to stop closing on it.
- **What's left out.** The character's own kinematic capsule and triggers;
  it collides with shapes on its mask's layers.
- **The kinematic capsule** reaches down to the feet, made in Physics.Sync
  (rebuilt when the controller changes) and sent where the character is
  over each tick (`b3Body_SetTargetTransform`), so it pushes.
- **Without a controller, or before the physics has a world** (its first
  Sync), an entity just moves, as before. Characters are at the top of the
  hierarchy (their LocalTransform is their place in the world).
- **View > Colliders** outlines characters' capsules too.

## Known limitations

- A character moves only when something calls `moveCharacter` (root
  motion does every frame); one nothing moves doesn't fall.
- Low obstacles under the step height can't block (the price of riding).
- Pushing into a body pinned against something stops the character, as
  pushing does.

## As built

- crucible/Physics.h (`gravity`, `CharacterController.gravityScale`,
  `CharacterMove.fallSpeed`, `launchCharacter`, `PhysicsService::
  launchCharacter`*); Box3dPhysics.cpp's `Character`*, `characterCapsule`*,
  `syncCharacter`*, `Mover`*, `gatherPlanes`*, `moverAccepts`*,
  `pushBodies`*, `characterOf`*, and its `moveCharacter` and
  `launchCharacter`.
- C#: `CharacterController.Launch`, `GravityScale` (1 by default).
- Tests: `CharacterMoveTests`, `CharacterStepTests`,
  `CharacterGravityTests` (native), `CharacterTests` (C#, JIT and AOT).
- For trying it by hand: the Walker's Characters.crscene: the Walker walks
  by root motion over three steps, off a ledge, up a 20 degree ramp and off
  its end, nudging a ball on the way, to a wall; a second Walker dropped on
  a 60 degree slope slides off it and walks to a wall of its own.
