# Walking a level: a character, a mesh, and a swing on time

A learning note for M9 step 8 (ADR 0097), the physics milestone's
demonstration.

## A level is one mesh

A game level is usually modelled in one piece: floors, ramps, stairs, and
walls all in the same model. Physics doesn't need it split into boxes. The
model's triangles become one static mesh collider (collision-meshes.md), and
the character mover collides with triangles as well as with boxes. A
static mesh is cheap: it never moves, so the physics engine builds its
search structure once.

What the character can do is set by the mover's limits, not by the level:

- **Step height.** Anything lower is stepped over, so stairs whose risers
  are under it are walked up and down like a ramp.
- **Slope limit.** A surface steeper than it is a wall: the character
  slides along it instead of climbing.

So a level designer builds to those numbers: 0.2 m stairs for a 0.3 m step
height, a 1 in 5 ramp (about 11 degrees) for a 45 degree limit, and a 45
degree slope where the character mustn't go.

## Steering on top of root motion

With root motion (root-motion-and-ik.md), the animation decides how far
the character moves: its walk carries it forward. The game only decides
which way it faces. A route follower turns the character toward its next
point, a little each frame (at most a turn rate), and moves on to the next
point once it's close enough. The walk does the rest, and the mover keeps
it on the ground.

Turning a little each frame depends on the frame rate: at 30 fps it turns
in bigger steps than at 240. The path differs by centimetres, and nothing
here needs it exact.

## Exact moments come from exact sources

The swing is different: the test is that its hit lands at the same moment
whatever the frame rate. Every part of the chain has to be exact:

1. **When the swing starts.** If the game started it ("the character has
   arrived, swing now"), it would start on the frame that noticed, up to a
   frame late, and a 30 fps frame is 33 ms. A state machine's exit time
   isn't tied to frames: "after half of Idle" is a moment on the animation
   clock, and the next state starts exactly there (state-machines-and-
   events.md). So the swing comes from Idle at its exit time.
2. **Where the foot is.** The pose at the start of each frame is exact.
3. **When it touches.** The hitbox is swept between frames, and a hit's
   share of the way along gives its time (swept-hitboxes.md).

Step 3 is where a little error is left. Between two frames the sweep
assumes a steady turn and slide (a screw), but a kick snaps: its foot
speeds up and curves within the frame. At 240 fps a frame is a short piece
of the curve and the screw follows it closely; at 30 fps one frame spans
much of the snap. Here that moved the hit by 0.14 ms at 30 fps, against a
33 ms frame. Sampling the pose at moments inside the frame would close the
gap, at the cost of posing the skeleton more than once a frame.

## Comparing two runs

Two runs of a program start at different moments, so "the hit came at
1.417 s" can't be compared to a millionth of a second: when the clock
started differs between them. What compares is the time between two
things inside one run: here, from the window opening to the hit. Both are
on the animation clock, so the difference is the same in every run if the
engine is frame-rate independent.

## Game time without waiting for it

The checks run the Level for 45 seconds of game time, several times over.
At first they ran in real time, so each run took 45 real seconds and the
three checks together took almost six minutes, most of a test run spent
waiting.

A game only knows time through its clock. If the clock, rather than
following the wall, moves on exactly one frame's length each frame (1/30 s
at 30 frames a second), the game can't tell the difference: it sees the
same frame lengths it would see on a fast enough machine, so it plays the
same. But nothing waits any more, and 45 seconds of game time take as long
as rendering 1,350 frames, a few seconds (ADR 0105). Runs become exactly
repeatable too: two runs from the same start see the same times, so they
agree to the microsecond.

Real-time behaviour, such as pacing frames against the display and the
time between input and picture, isn't what these checks are about, and has
tests of its own.

