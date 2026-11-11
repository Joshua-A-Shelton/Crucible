# Frame-rate independence: timing that doesn't depend on when frames fall

A learning note for M8 step 7 (ADR 0084).

## The problem

A game runs in frames, and frames are neither even nor the same on every
machine: 33 ms at 30 fps, 7 ms at 144, and they wobble with load. Anything
that happens "this frame" is really snapped to a frame boundary, early or
late by up to a frame's length, and by a different amount each time.

For most things nobody notices. For rhythm everyone does. Footsteps are
heard as uneven when they wander by 10 to 20 ms. The same goes for a parry
window that's 150 ms on one machine and 120 on another.

## Carrying exact time through every stage

M8 keeps the real time of each moment from where it starts to where it's
heard:

1. **Animation:** a frame's update covers the time from the last frame's
   start to this one's. A footstep passed a third of the way through is
   timestamped a third of the way between those two starts, not at the
   frame (ADR 0081).
2. **Gameplay** passes that timestamp along untouched:
   `PlayOneShot(sound, step.Timestamp, ...)`.
3. **The mixer** maps the timestamp onto its own clock (samples) and starts
   the sound at the exact sample for that timestamp plus a fixed delay
   (ADR 0082).

Every footstep is late by the same 50 ms, which nobody notices, and none
is late by a varying amount, which everybody notices.

## Testing it

The demonstration runs the same walk at 30 fps and at 144 fps. It mixes
the audio offline and finds each footstep's onset in the samples. Each one
lands exactly on its event's time plus the delay, and the two runs agree
to the microsecond.

The test runs in real time on a busy machine, with real jitter, on purpose.
Its first runs passed. Then, during a full parallel test run, the 144 fps
walk came one frame late.

The leak was a **state machine transition**. Idle hands over to Walk "at
the end of Idle", which falls partway through some frame. The transition
was taken at the end of that frame, and Walk started from zero there,
dropping the rest of the frame. With perfectly even frames the end of Idle
happened to land on a frame boundary at both rates, so nothing was
dropped. Any jitter dropped up to a frame.

The fix follows the same rule as everything else: keep the exact time.
The new state has already been playing for the part of the frame after
Idle's end, so the update plays it for that long, and events in that stretch
are timestamped where they really fall.

## The general rule

Whenever something happens "at" a time inside a frame (an event, a
transition, a sound, a window opening), carry that time forward as a
number. Don't let it collapse onto the frame. Each stage of the pipeline is
one more place where it could quietly collapse. A test at two frame rates
under real load is how you find the stage you missed.
