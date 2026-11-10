# Subsystem contracts: swapping an engine's parts

A learning note for M7 step 7c (ADR 0075).

## What "replaceable" has to mean

Engines promise you can swap their physics or audio. What that takes depends
on where the line is drawn between the engine and the part you swap.

If the line is a single interface the engine calls ("create a body", "set its
position", "step"), the engine keeps all the bookkeeping, and the part is
small and easy to write. But every body crosses the line every tick: a
thousand bodies means thousands of calls in and out. Batch them and the
interface turns into "here are all the positions", at which point the part
is doing the bookkeeping after all.

Crucible draws the line around three things instead:

- **Components:** the data. A `RigidBody` and a `Collider` on an entity say
  what physics should do with it. Scenes save them, the inspector edits
  them, game code reads them, whoever implements physics.
- **Systems:** the work, under fixed names in fixed frame phases.
  "Physics.Sync" pushes changes into the simulation, "Physics.Step" runs
  it, "Physics.WriteBack" copies positions back. Each is one call per
  tick, with the loop over thousands of bodies inside it.
- **A service:** the questions game code asks: what does this ray hit,
  what's inside this sword's swing, move this character.

Swapping the part means registering different systems under the same names
and installing a different service. The data and the game's code don't
change.

## A default that does nothing, on purpose

Physics and audio aren't built yet, so why define their contracts now? An
interface nobody implements tends to be wrong in ways nobody notices.
Shipping a *null* implementation makes the seam real: the components exist,
the systems run in the right phases, the calls return something. The null
physics finds nothing and never collides; the null audio is silent but
counts what it was asked to play, which is exactly what a test wants.

## Order without ordering

A native module can replace physics, and the engine registers its null
physics at start-up. Which comes first? It depends: the launcher loads
modules before the engine runs, the editor after. So the engine registers
its defaults "unless something already has this name", and a module
registers "replacing whatever has this name". Either order ends with the
module's.

## Gameplay, not editing

The editor runs the engine's own systems all the time (it draws, it
propagates transforms), but physics shouldn't simulate while you're placing
things. Systems that belong to the game itself (physics, audio, a native
module's) are marked *gameplay*: a game's schedule includes them, the
editor's own schedule leaves them out, and its play mode runs them.

## The seams that already existed

Input, assets, the renderer, and the loop all worked before step 7d; what
was missing was a single place to swap each one (ADR 0076).

- **Input** came straight from the platform. Now the platform is just the
  default *source* of each frame's events. A source that plays back a
  recording can still pass the window's own events through, by calling the
  platform's source for those and adding its recorded keys.
- **Assets** were read in three places, each told separately where to look.
  Now there's one source, and everything reads through it. Whichever
  language installs it, both languages read from it.
- **The renderer** was already components, named steps, and a service. It
  just needed writing down: the step names became constants, so code that
  inserts a step "after Opaque" names a constant, not a string it might
  mistype.
- **The loop** that runs the frame was built directly by the engine's host.
  Now it's an interface the host runs through, and a game can hand it its
  own: one that renders on another thread, for instance.

