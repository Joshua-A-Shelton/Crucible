# Pausing a game: holding time still in the editor

A learning note for editor usability step 2 (ADR 0088).

## What "paused" means

A game is a loop: read input, step the simulation, draw. Pausing in an
editor means stopping the middle part, the simulation, while the editor
itself keeps drawing and taking input, so you can look around, select
things, and read values in the inspector.

Crucible's frame is split into phases (input, physics, update, animation,
drawing, ...). Paused, the game's systems in the simulation phases don't
run, but its drawing phases still do. That way a game's HUD or debug lines,
which it builds every frame, stay on screen.

## Not jumping when it resumes

Games move things by how much time passed since the last frame (the
frame's delta). If the editor simply stopped calling the game and then let
it see "time since the last frame it saw", the first frame after a long
pause would be huge, and everything would leap. Instead the editor keeps
running its own frames during the pause; the game just doesn't see them.
When it resumes, it sees a normal frame's delta.

## Stepping one tick

Physics runs at a fixed rate, 60 ticks a second, however fast frames come
(see fixed-step-physics.md). A step runs exactly one of those ticks, and
gives the rest of the frame one tick's worth of time. So each press moves
the game by exactly 1/60 s, which makes stepping deterministic: the same
presses give the same result, handy for chasing a bug frame by frame.

## Holding sound

Sound doesn't stop politely: the mixer is producing samples continuously
for the speakers, on its own thread. Pausing it means producing silence
while keeping each sound's place. The mixer counts time in samples (its
cursor), and every sound knows the sample it starts on. While paused, the
cursor keeps counting (so its link to real time stays exact), and every
sound's start moves forward by the same amount. When it resumes, a sound
that was playing carries on with its next sample, and one scheduled for
later starts just as long after the resume as it would have after the
pause began.
