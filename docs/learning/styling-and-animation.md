# Styling and animation

A learning note for M6 step 5 (ADR 0061).

## Why values come in layers

A button's background might be set by three different parties: the
theme ("buttons are gray, hovered buttons are blue"), the game's code
("this one is red"), and an animation fading between colors. If each
simply wrote the same field, whoever wrote last would win, and nothing
could be undone: un-hovering the button wouldn't know what to restore.

So each stylable property keeps its values in layers, and reads the
highest one present:

1. an animation running on it;
2. a value set in code;
3. the value of the best-matching style;
4. the property's default.

Removing a class or a state just recomputes layer 3; clearing a code
value just empties layer 2. Nothing has to remember what came before.
WPF's dependency properties and Avalonia's styled properties work this
way.

## Which style wins

Several styles can match one element. CSS settles it by *specificity*:
a selector naming more classes and states is more specific than one
naming a type, which is more specific than one naming nothing. Between
two equally specific styles, the later one wins. Crucible's selectors
only look at the element itself (no "a Button inside a Toolbar"), so
restyling an element never has to look at the rest of the tree.

## Transitions and tweens

A *transition* belongs to a style: "when the background changes, take
0.15 s to get there". When hovering changes which style wins, the value
doesn't jump: an animation layer goes from the value showing now to the
new one. Starting from the value *showing* (not the old target) is what
makes un-hovering halfway through turn back smoothly. An element's very
first styling is applied without animating, or every element would fade
in from its defaults.

A *tween* is the same animation started from code: animate this property
to that value over so long.

## Easing

Linear motion looks mechanical: real things speed up and slow down. An
easing curve maps time (0 to 1) to progress (0 to 1). CSS describes its
curves as cubic Béziers from (0, 0) to (1, 1) with two control points;
`ease-out` (0, 0, 0.58, 1) starts fast and settles gently, which is why
it suits things reacting to the user. Evaluating one means finding the
curve parameter whose x is the time (a few Newton steps), then reading
its y.

## Real time, not game time

The game's clock can slow down, speed up, or stop (a pause menu). UI
animation runs on real time, so the menu over a paused game still
responds smoothly.
