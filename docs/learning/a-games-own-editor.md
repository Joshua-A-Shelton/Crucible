# A game's own editor

A tour of the pieces a game uses to build an editor of its own, by way of
the Walker's character editor (ADR 0128).

## Data first

It starts with a component: the data the editor edits and the game uses,
here a character's Strength, Agility, and Pace. Saved in scenes like any
other component, it's edited by the Inspector's generated fields before
any editor code exists.

## A plugin to hold it

Game code and editor code that belong together live in a plugin: the
game half compiled into the game, the editor half into an editor assembly
that's never shipped. Building the package proves it: the game's half is
there, the editor's isn't.

## Drawing the data better

A drawer replaces the generated fields where they're poor: bars that show
stats at a glance, a derived number a designer cares about, with the
ordinary number boxes still there to type into. Every change goes through
undo, as the editor's own do.

## A place to work

A workspace is a tab for one thing being worked on, opened from it (the
component's Open button). It holds what the job needs: here a preview, a
world of its own with the character's model, and controls to play its
animations, pause, and scrub through them.

## Only the public API

None of it reaches into the editor's insides: the same API map mode is
built on. That's the promise to a game's programmers: what the editor can
do for itself, a game can do for its own editors.
