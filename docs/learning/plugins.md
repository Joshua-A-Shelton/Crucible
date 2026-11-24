# Plugins

How code from somewhere else becomes part of a game, built either from
source or as compiled assemblies, and how the engine finds it in a
shipped game without the game naming it (ADR 0118).

## Source while you write it, compiled when you share it

A plugin has two lives. While someone writes it, it's source: a folder in
a game's project that builds and hot-reloads with the game, so every
change is tried at once. When it's shared, it's compiled: two assemblies,
one for the game and one for the editor, that another game drops into its
own `Plugins/` folder. Both lives end in the same pair of assemblies,
loaded the same way; the difference is only who built them and whether
they reload.

Compiled code is how most engines share plugins: no source to hand over,
no build of someone else's code in every project, and a clear boundary.
The cost is that compiled code is tied to the API it was built against.

## Binary compatibility

Source code that uses a changed API just fails to compile, with a message
saying where. A compiled assembly fails later and worse: it names methods
and types by their signatures, and if one changed, the runtime finds out
only when that code first runs, with an exception far from the cause.

*Semantic versioning* is the usual promise: a version is
`major.minor.patch`, and only a new major version breaks what was built
against the old one. Before 1.0 the rule is looser, any minor version may
break. So a compiled plugin records the Crucible version it was built
with, and the editor refuses one from another line, before 1.0 another
minor, with a message saying what to rebuild. Refusing at load is much
better than failing at some later call.

## Order and dependencies

When plugins use each other, the ones used must load first, so their
types exist when the others are loaded. Ordering things so each comes
after what it depends on is a *topological sort*. Some lists can't be
ordered: two plugins that use each other form a *cycle*, and no order
puts each after the other. Those, and anything built on them, are left
out, with why.

A compiled plugin can't use a source one: the compiled plugin loads once,
but the source one reloads with the game, so the code under the compiled
plugin would keep changing.

## Finding plugins without the game naming them

A game might use a plugin without its code mentioning the plugin at all:
a plugin's systems just run. The C# compiler drops references nothing
uses from the assembly it writes, so the game's assembly can't be relied
on to list its plugins. Instead the editor writes the list into the build
as an attribute on the game's assembly, in load order. At runtime the
engine reads it and asks for each plugin by name: from the editor's own
load contexts, from the package's assemblies under the JIT, or from what's
compiled into the executable under NativeAOT. One list works the same
everywhere.

NativeAOT removes code it thinks is unused (*trimming*). A plugin found
only by name looks unused, so each plugin is made a *trimming root*: kept
whole.

## Assets named by their plugin

A plugin's assets keep their IDs in their sidecars, so scenes that use
them keep working wherever the plugin's folder goes. Their paths start
with the plugin's name and a colon (`Dialogue:Icons/Talk.png`), which no
file of the game's can have, since Windows doesn't allow colons in file
names. A compiled plugin's assets are part of what was built and shared,
so the editor doesn't change them.
