# 0084 — The M8 demonstration

**Status:** Accepted (2026-11-11); M8 step 7 built, then tried by hand by
the owner on KWin (the Walker idling, walking its circle, thumping on each
foot; Stop putting it back); M8 signed off. Names, including those added
while building, settled by the owner as drafted (docs/naming.md).

## Context

M8's "done when": a character walks with footstep events scheduling
sounds, at 30 and 144 fps; headless, the mixer renders to a buffer and each
footstep's onset is checked against its event time; then it's tried by
hand. Steps 1 to 6 built the pieces: skins and clips (ADR 0078), poses
(0079), GPU skinning (0080), state machines, events, and root motion
(0081), the mixer (0082), and sound assets (0083).

## Decision

Decided by the owner (2026-11-10):

1. **In the Walker project:** a scene (Walk.crscene) and game code
   (Code/Footsteps.cs), opened and played in the editor like any project;
   the check runs it standalone through the editor, so the project's code
   is built the way it always is (as ADR 0077's standalone run is).
2. **A real-time frame cap,** `--frame-rate N` in the launcher, so the check
   meets real frame-time jitter rather than a simulated clock.
3. **Footsteps at each foot:** the sidecar's events are `LeftFoot` (0.5 s)
   and `RightFoot` (0 s) on Walk and WalkForward, and each step is heard at
   that foot's joint, in world space.
4. **A circle:** the game turns the Walker by the distance root motion
   carried it over a radius, so it walks round in front of the camera.

## The demonstration

- **The scene:** a sky, a sun, a camera that is also the listener, and the
  Walker (a prefab instance of its model) with its Animator (Walker.crstates,
  root motion on), `Footsteps`* (the sound, a volume), and `Circle`* (a
  radius, 3 m).
- **`FootstepSystem`** (LateUpdate, after "Animation.Update"):
  - It reads the frame's animation events.
  - For each `LeftFoot` or `RightFoot` it plays the footstep at that foot's
    joint (model matrix times the Walker's world transform), scheduled at
    the event's timestamp.
- **`CircleSystem`** turns the Walker by `distance / radius` about up each
  frame.
- **The footstep** (Assets/Sounds/Footstep.wav, `make_footstep.py`) is a
  70 Hz thump at its peak on the first sample plus low-passed noise: a fifth
  of a second, with a sharp onset.
- **The check** (`WalkerChecks`*, CTest `samples.walker`):
  1. A copy of the project, Walk.crscene opened.
  2. Run standalone, headless, at 30 and then 144 fps, for seven seconds
     each (`--game-arg check=1`).
  3. In each run, the game mixes offline as far as each frame has come (as
     a device would ask), then finds every step's onset.
  4. Every step heard, none late, each within a frame of its event's time
     plus the schedule delay (they land on it exactly), and the two runs'
     steps at the same times within a millisecond.

## What it found

The first runs passed, alone; under a loaded parallel test run, the 144 fps
run's second walk came one frame (6.9 ms) late. A transition taken at an
exit time happened at the end of the frame that passed the exit, and the
next state started there, losing the rest of the frame. With frames paced
exactly, 2 s of Idle is a whole number of frames at both rates, so nothing
was lost; any jitter lost up to a frame.

Now a transition at an exit time carries the frame's time past the exit
into the next state: it's played for that long in the same update, its
events timestamped where they fall (ADR 0081, amended). A test steps the
same machine by 0.3, 0.07, and 0.4 s and finds the next state equally far
in (it fails without the change). Transitions on conditions alone are taken
when the frame sees the condition, which is when gameplay set it.

## As built

- Launcher `--frame-rate` (`LaunchOptions::frameRate`*, C#
  `FrameLoop.FrameRate`*): each frame starts a period after the last,
  sleeping then spinning the last millisecond; a frame that runs a whole
  period long moves the schedule on rather than rushing.
- samples/Walker: make_footstep.py, Assets/Sounds/Footstep.wav,
  Assets/Scenes/Walk.crscene, Code/Footsteps.cs; the sidecar's events.
- The editor's `WalkerChecks`* (`--game-arg check-walker=<folder>`*).
- src/native/core/Animation.cpp: `pastExit`*, `catchUp`*.
