# End-to-end checks: testing a session, not a feature

A learning note for M7 step 8 (ADR 0077).

## Why one long check after many short ones

Every editor feature in M7 has its own check: drag and drop places a model,
the inspector edits a field, Play hot-reloads code. Each starts from a state
set up just for it and checks one thing. That's what makes them good at
pointing at a broken feature.

What they can't see is the seams between features. Does the model placed by
dragging survive a save and reopen? Does a field typed into the inspector
still hold after the code behind it hot-reloads? Does the standalone run load
what the editor imported? Only a check that does all of it, in order, on the
same state, can tell. That's an end-to-end check: a scripted session that
reads like a person using the editor, from a brand-new project to a game
running on its own.

## Writing one that stays honest

- **Start from nothing.** A check that starts from a prepared project can
  quietly depend on something that project happens to have. Making a new
  project inside the check proves a new project works.
- **Drive it the way a person would** where it matters: the model is dragged
  from the asset browser's tile with pointer events, and fields are typed
  into, not set in code. Setup that isn't the point (a sun, a camera) can be
  done directly.
- **Check after every step,** not just at the end, so a failure says which
  step broke.
- **Make it repeatable.** The game code rocks by frame count rather than by
  time, and captures come a fixed number of frames after each action, so two
  runs, on two graphics APIs, capture the same moment and can be compared
  pixel for pixel.
- **Don't let the check change what it measures.** Twice in M7 the test
  script itself kept the old version of the game alive by holding something
  of it in a variable, making a leak check fail for a reason that wasn't in
  the engine. In a long script, anything kept in a local lives as long as the
  script does.

## Then by hand

A scripted session proves the steps work; it can't say whether they feel
right: whether dragging is smooth, whether the inspector is readable, whether
anything is confusing. So the milestone ends with the same session done by
hand, and that's what signs it off.
