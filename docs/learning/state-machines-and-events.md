# State machines and events: deciding what to play, and when things happen

A learning note for M8 step 4 (ADR 0081).

## Why a state machine

Step 2 gave gameplay `play(clip, fade)`. That's enough for a door, but a
character has rules: walk when moving, idle when not, jump when the button
is pressed, land when the jump ends. Written as gameplay code, those rules
end up scattered, and every animator change becomes a code change.

A state machine is those rules as data. **States** say what to play.
**Transitions** say when to leave one state for another and how long to
crossfade. **Parameters** are the only inputs: gameplay sets `Speed` or
fires the `Jump` trigger, and the machine decides. The animator can then
rework the transitions without touching gameplay.

## The three kinds of parameter

- **Float:** a quantity, compared with `>` or `<`. Equality on floats is
  fragile (is 0.30000001 equal to 0.3?), so the importer doesn't offer it.
- **Bool:** a lasting fact ("is on the ground").
- **Trigger:** a one-off ("jump was pressed"). It stays set until a
  transition that tests it is taken, then clears itself. If it cleared
  every frame instead, a press arriving while no transition could take it
  (mid-landing, say) would be lost.

## Exit times

Some transitions shouldn't happen mid-animation: an attack should finish
its swing. An **exit time** says "only once the state has played this far".
For a looping state that means each time round the loop, so a transition
with an exit time of 1 happens at the loop's end and never mid-step.

## Blend spaces and phase

A **blend space** is a state that plays several clips at once, weighted by
a parameter. Walk sits at speed 0 and WalkForward at 1.7; at a speed of 0.85
the character plays half of each.

Blending two clips only works if they're doing the same thing at the same
moment: blend a walk with its left foot down and a run with its right foot
down, and the legs pass through each other. So the clips aren't timed
separately. A state has one **phase**, from 0 to 1, and each clip plays at
that share of its own length. The state's length is the clips' lengths,
weighted, so the blend gradually speeds up as it leans toward the shorter
clip.

The weighting is the only part that knows how many parameters a blend
space has. A 1D space picks the two clips either side of the value, and a
2D one would pick a triangle of three. Everything after it (sampling,
phase, events, root motion) takes a list of weighted clips, which is why
adding 2D later won't mean a rework.

## Events at exact times

A frame doesn't happen at an instant. The animation update moves every
clip from where it was at the last frame's start to where it is at this
one's. A footstep at 0.5 s in the clip might be passed a third of the way
through that move.

Reporting it as "this frame" would tie it to the frame rate. At 30 fps,
that's up to 33 ms late, and the lateness changes from step to step, so
footsteps sound uneven. Instead the event gets the time it was actually
passed: a third of the way between the two frame starts. Footsteps half a
second apart in the clip are then half a second apart in timestamps at any
frame rate. The audio mixer can schedule each one at its exact sample, at a
fixed delay that's never noticed.

The same reasoning drives **windows** (a parry window from 0.20 to 0.35 s).
Input events carry the time they happened (ADR 0010), so "was the press
inside the window?" compares two timestamps rather than two frame numbers.
That keeps a 150 ms window 150 ms wide at any frame rate.

## Only the loudest clip speaks

During a crossfade, two clips play at once, and each would report its
footsteps. Mid-fade from Walk to Run, that means two footstep sounds for
one step. So only the clip with the most weight reports events. That's one
clip in the base and one in each layer. The swap happens halfway through
the crossfade, which is where the motion is most like the new clip's.
