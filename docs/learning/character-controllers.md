# Character controllers: moving people, not boxes

A learning note for M9 step 4 (ADR 0091).

## Why not a rigid body

A person in a game shouldn't move like a crate. A rigid body tips over,
bounces, slides on slopes, and gets shoved by everything it touches.
Players expect to stop dead when they let go of the stick, walk up stairs,
and stand still on a hill. So most engines move characters with a
**character controller**: a capsule that isn't simulated, moved by code
that asks "where can it go?" and handles the collisions itself.

A capsule (a cylinder with round ends) is the usual shape: its round
bottom slides over small bumps and edges instead of catching on them.

## Collide and slide

Asked to move, the controller sweeps its capsule along the move (a shape
cast) and stops where it would touch something. If that's a wall at an
angle, stopping dead would feel sticky, so it keeps the part of the move
along the wall and tries again: it slides. Box3D does this with planes:
every surface the capsule touches is a plane it mustn't cross, and a small
solver finds the move closest to the one asked for that crosses none.

## Steps

Stairs are a problem: each step's front is a little wall. Crucible's
capsule **rides a step height above the feet**, so its bottom passes over
anything lower; after each move a short cast down finds the ground and
puts the feet on it. Walking up stairs, the feet rise step by step; walking
down, the same cast follows the ground down instead of leaving the
character to fall and land on each step. The other common way (PhysX,
Unreal) is to try stepping up only when blocked; it's more exact at edges
but more work.

## Slopes

Ground steeper than the slope limit isn't ground: the controller treats it
as a wall, sliding along it rather than up it, and a character standing on
it isn't grounded, so it falls, sliding down.

## Gravity

Who makes a character fall is a design choice. Unity's `Move`, Godot, and
Jolt leave it to the game: you keep a vertical speed and add gravity. Unreal
does it in the engine. Crucible does it in the engine too, because its
characters are often moved by root motion (animation), which has no
gravity in it: without the engine's help, a walking animation would carry
a character straight off a ledge into the air. A gravity scale of 0 hands
it back to the game. Jumps set an upward speed that gravity then slows.

## Meeting rigid bodies

The controller's capsule isn't part of the simulation, so on its own a
rolling ball would pass through a character. Crucible gives each character
a **kinematic** capsule body that follows it: something the simulation
knows is there but doesn't move itself, so balls bounce off it and queries
hit it. Walking into a ball, the controller gives it a push.
