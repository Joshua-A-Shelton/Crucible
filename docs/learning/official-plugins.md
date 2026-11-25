# Official plugins

How an engine ships parts of itself as plugins, and why that keeps its
editor API honest (ADR 0123).

## Dogfooding

Building your own features on the API you give others is called
*dogfooding*. If map mode can be written with only the public editor API,
so can a studio's level editor. Putting it in an assembly of its own makes
the compiler the judge: anything it reaches for that isn't public fails to
build. Each failure is a gap in the API, found before a user finds it.

## Swappable parts

Many engines split themselves into modules or packages that a project
turns on or off: a 2D game needn't load 3D terrain tools, a 3D game needn't
carry tilemaps. That keeps the editor lighter for each project and makes
the parts' boundaries real. The cost is a way to list them and to load
them, which plugins already had.

## Shipped beside the editor

An official plugin isn't copied into each project. It's built with the
editor and sits beside it, and the project file only lists which ones it
uses. Updating the engine updates them everywhere at once, and a project
never holds a stale copy. Leaving one out is a line in the project file.

## Loaded once

Plugins written by a game reload with its code. Official ones are part of
the editor, so they load when it starts and stay until it quits; opening
another project only starts and stops their extensions. That also means
anything loaded alongside them (their checks) survives a project being
opened in the middle of a run.

## Tests with more access than the code

The plugin may use only the public API, but its tests may need more:
helpers that click the editor's buttons, or the plugin's own insides. So
the tests live in an assembly of their own that's allowed to see both,
while the plugin itself still can't. Shipping builds leave the tests out.
