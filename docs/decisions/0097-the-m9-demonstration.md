# 0097 — The M9 demonstration

**Status:** Accepted (2026-11-16); M9 step 8 built, then tried by hand by
the owner in the Walker's Level scene; M9 signed off (2026-11-17). Names,
including those added while building, settled by the owner as drafted
(docs/naming.md).

## Context

M9's "done when": a character walks a mesh level, and a test hit registers
identically at 30 and 240 fps. Steps 1 to 7 built the pieces: Box3D (ADR
0085), mesh colliders (0086), queries (0090), the character mover (0091),
joints (0092), 2D (0093 to 0095), and swept hitboxes with bone attachments
(0096).

## Decision

Decided by the owner (2026-11-16):

1. **A generated level:** `make_level.py` writes Level.gltf, imported with
   Collision: Mesh, so one static Mesh Collider is the whole level.
2. **A waypoint route:** game code turns the Walker toward each waypoint in
   turn; root motion drives its CharacterController.
3. **A Swing clip, swung first:** the Walker kicks a post before it walks.
   A swing started when the Walker arrives somewhere would start on the
   frame that saw it arrive, up to a frame apart at different rates. Exit
   times start states at exact times (ADR 0084), so Idle, then Swing at
   Idle's exit time, puts the swing at the same moment from Play at any
   rate.
4. **A headless check,** as M8's: the scene run standalone at 30 and 240
   fps, then tried by hand.

## The demonstration

- **The level** (`make_level.py`, Assets/Models/Level.gltf): a 24 m floor
  inside walls, a 1 m platform reached by a ramp (1 in 5) and left by four
  0.2 m stairs, a 45 degree slope up to a ledge (steeper than the
  character's limit, so a wall), and a low wall to walk round. Crucible
  mirrors X on import, so the scene's x is the script's negated.
- **The swing** (`make_walker.py`): "Swing" is a one-second kick: the
  right leg winds back with the knee bent until 0.3 s, kicks forward and
  straightens until 0.5 s, and comes back by 1 s, the arms swinging
  against it. Its "Strike" window is 0.3 to 0.55 s (the model's sidecar).
- **The state machine** (Assets/Models/Level.crstates): Idle, Swing (not
  looping), and Walk (WalkForward); a Bool `Arrived`. Idle goes to Swing
  at half its 2 s loop while not Arrived, Swing to Walk at its end, Walk to
  Idle once Arrived. The other scenes keep Walker.crstates.
- **The scene** (Level.crscene): sky, sun, a camera over the level, the
  "Level" (the model with a Mesh Collider), the Walker (Animator on
  Level.crstates with root motion, a CharacterController, Footsteps, a
  `RouteFollower`), "Foot Attachment" on its right foot with a "Strike
  Hitbox" under it (window Strike, mask layer 2), a dynamic "Post" on
  layer 2 in front of its right foot, and "Waypoint 1" to "Waypoint 6":
  round the platform's foot, up the ramp, across, down the stairs, along
  the far side, and back.
- **Code/Route.cs:** `Waypoint` (`Next`) and `RouteFollower` (`First`,
  `Reach` 0.6 m, `TurnRate` 2 rad/s). `RouteSystem` (LateUpdate), while the
  Animator is in Walk, turns the Walker toward its waypoint by at most
  TurnRate, and takes the next within Reach, along the ground; at the last
  it sets `Arrived`. On Stop it prints how far each follower got, when it
  arrived, the lowest it went, and the frames the loop clamped.
- **Code/Kicks.cs:** `KickSystem` now also prints how far into its
  owner's latest window each hit came (from the window's open event).
  That doesn't depend on when the process started, so two runs compare.
  The post is dynamic, so the kick knocks it out of the Walker's way.
- **The check** (`LevelChecks`, CTest `samples.walker-level`): a copy of
  the project, Level.crscene opened, run standalone headless at 30 and
  then 240 fps for 45 s each (a run with clamped frames is run again, up to
  three times). In each run: the post hit once, inside the Strike window;
  every waypoint reached and the route's end arrived at; never lower than
  0.1 m below the floor; a clean exit. Then the two runs' hits within
  0.5 ms.

## What it found

At 30 fps the hit came 141 µs earlier than at 60, 120, and 240 fps, which
agree within 10 µs. Between frames a hitbox follows a screw through the
two frames' places (ADR 0096), not the animation's own curve, and a 30 fps
frame spans much of the kick's snap. The design's 50 µs between 30 and 240
fps was loosened by the owner to 0.5 ms (2026-11-16), under a sixtieth of
a 30 fps frame. Sampling the pose between frames would remove the gap; it
stays a known limitation of ADR 0096. Measured: 116.457 ms into the window
at 30 fps, 116.598 to 116.612 ms at 60 to 240 fps.

The route takes about 35 s, its end reached within 0.1 s at either rate
(the turns are per frame, so the path differs a little).

## As built

- samples/Walker: `make_level.py`, `make_walker.py`'s `swing` and
  `KICK`; Assets/Models/Level.gltf, Level.bin, their sidecars, and
  Level.crstates; Walker.gltf.crmeta's Swing clip (its sub-asset ID) and
  Strike window; Assets/Scenes/Level.crscene; Code/Route.cs; Code/Kicks.cs.
- src/managed/Crucible.Editor/LevelChecks.cs; CTest samples.walker-level.
