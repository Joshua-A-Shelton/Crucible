# Editor assemblies

Why a game's editor code is compiled separately from the game, and how it
loads and reloads beside it (ADR 0116, after ADR 0002).

## Keeping editor code out of the game

A game's tools (a character editor, a level checker) need the editor's
code, which a shipped game doesn't have and mustn't carry. There are two
ways to keep them apart:

- **Separate assemblies.** Editor code compiles into its own assembly
  that references the game; the game never references it. The compiler
  enforces the split: game code that tries to use an editor type doesn't
  compile. Packages simply leave the editor assembly out.
- **Conditional compilation.** `#if CRUCIBLE_EDITOR` around a few lines in
  game code. Handy for small touches (a debug drawing, an extra check),
  but the code outside the `#if` can quietly come to depend on what's
  inside it, and nobody notices until a package is built.

Crucible uses both: folders named `Editor` hold editor code, and the
symbol is allowed in game code, with a background build without it to
catch that second problem while it's fresh.

## Load contexts

.NET loads assemblies into *load contexts*. The default one lives as long
as the process. A *collectible* context can be unloaded: once nothing
refers to anything in it (no objects of its types, no `Type` or
`Assembly` objects, no delegates to its methods), the garbage collector
frees its code. That's how hot reload works: build again, load the new
assembly into a new context, move the game's data over, unload the old
one.

The editor assembly goes into the game's context, not one of its own,
because it uses the game's types: a `Stats` from the game and a `Stats`
seen by the editor code must be the same type, and types are only the
same if they come from the same loaded assembly. Loading both together
also means they're always the same version.

When the editor assembly asks for an assembly the context doesn't have
(the editor's own `Crucible.Editor`), the context's `Resolving` event
answers with the one already loaded. Loading a second copy would make a
second set of types that the editor wouldn't recognize as its own.

## The leak that unloading hides

Unloading only *starts* the process; the context is collected when
nothing refers to it. A reference from outside (a static cache, an event
handler, a field in the editor) keeps it alive for good. Here the trap was
less obvious: the context's own resolving handler was a closure holding
the game's `Assembly`. That looks like a cycle the collector could free,
and on paper it is; in practice the old context was never collected (the
likely reason: the runtime's native side keeps a collectible context's
object alive while its code is reachable, and the handler reached it).
Looking the assembly up by name each time, instead of capturing it,
fixed it. The general rule: nothing a collectible context
keeps (its handlers included) should hold that context's own types or
assemblies.

The editor checks every reload: it waits for the old context to be
collected and logs "The previous build didn't unload" if it isn't.
