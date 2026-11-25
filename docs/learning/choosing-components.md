# Choosing components

How editors let people pick from a long list (ADR 0127).

## When a menu stops working

A menu is quick for a handful of choices: everything is visible, one click
picks. Past a screenful it fails: items fall off the edge, and a reader
has to scan every name to find one. Long lists need a different tool.

## Search first

The fastest way to find one item among many is to type part of its name.
A picker that opens with the cursor already in a search field lets people
who know what they want type three letters and press Enter, without
touching the mouse. Matching the description as well as the name helps
people who know what they need but not what it's called ("light" finds
every kind).

## Groups for browsing

People who don't know what exists need to browse, and a list grouped by
what things are for (physics, lights, audio) reads far better than one
alphabetical column. Groups come from an attribute on each type, so a
game's own components join the right group, or one named after the game.

## Reusing what the platform has

.NET already defines `[Category]` and `[Description]` attributes for
exactly this, used by visual designers for decades. Reusing them means
nothing new for game programmers to learn, and other tools already
understand them.
