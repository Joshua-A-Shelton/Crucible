# Custom inspectors

How an editor lets games draw their own data, without throwing away what
it already does well (ADR 0126).

## Generated first

An inspector that knows nothing about a type can still show it: the
serializer already lists every field and its kind, so a number gets a
number box, a bool a checkbox, a nested struct an indented group. That
covers most components, and keeps working as fields are added. Custom
drawing is for what the generic version shows poorly.

## Drawers

A *drawer* is the code that draws one thing. Editors usually let a drawer
attach at three levels: a whole component (its section), a type (every
field of that type, wherever it's used), or one field marked by name. The
narrower the level, the more places it reaches: a drawer for a damage
range improves every component that has one.

## Mixing with the defaults

A drawer that has to redraw every field by hand is tedious and falls
behind when fields change. So a drawer can ask for the generated editor of
any field and put it where it likes: it draws only what's special and
lets the rest be ordinary.

## Typed values over saved data

What's saved is JSON, but a drawer thinks in its own type: a `Stats` with
a `Strength`. The editor converts between them with the same generated
serializer that saves scenes, so the drawer reads and writes plain C#
values. Every change still goes through the editor's undo history, and a
drag that changes the value many times is wrapped as one step, as the
generated editors do.
