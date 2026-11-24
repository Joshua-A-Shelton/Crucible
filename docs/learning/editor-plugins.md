# Editor plugins

How a project's editor code plugs into the editor, and how it keeps its
panels across a reload without keeping the old code alive (ADR 0117,
after ADR 0116).

Since ADR 0118 this is an *editor extension* (`EditorExtension`), and a
*plugin* is a package of runtime and editor code that a game uses (see
[plugins](plugins.md)); "plugin" below means the editor code.

## A facade, not the editor's insides

Editor code could be given the editor itself: its shell, every panel,
every internal list. That's the most power, and the most fragile: each
change inside the editor can break someone's tool. A *facade* is a small
object made for the purpose, offering only what plugins need (panels,
commands, the selection, undo, the scene, assets, the log) and hiding
how the editor does it. The editor can then rearrange its insides freely,
as long as the facade keeps behaving the same.

The facade here is also a ledger. Each plugin gets its own
`EditorContext`, and everything added through it is remembered, so
stopping the plugin takes it all out again, without the plugin having to
remember.

## An entry point, not attributes

Attributes (`[EditorCommand] static void Bake()`) need no setup code, but
their order and their setup are implicit: what if two commands need the
same state, or a panel only makes sense with a project open? An entry
point (`EditorExtension.Start`) is one ordinary method, run at a known time,
where the plugin decides what to add. It's also where unloading starts:
the editor calls `Stop`, then takes out what `Start` added.

## Keeping a panel's state

A reload unloads the plugin's code, so its panel objects must go: they
are instances of the old version's types. What survives is *data*, not
objects. Before the reload each panel writes what it wants kept into a
`PanelState` (numbers, strings, bools: values with no type from the old
code), a placeholder takes its place in the layout, and once the new code
has loaded a new panel is made there and reads the state back.

The same values are saved with the layout, so a panel comes back after
restarting the editor too. That's why the state is plain values: they are
written to a file.

## What else holds the old code

Unloading only works when nothing refers to the old code (see
[editor assemblies](editor-assemblies.md)). A plugin reaches the editor
through the facade, and some of what it hands over stays in the editor's
own objects:

- **Event handlers.** `editor.Selection.Changed += ...` puts a delegate,
  pointing into the plugin's code, into the editor's `Selection`. Left
  there, it keeps the code loaded and keeps running the old version. The
  editor removes every handler whose method or target comes from the
  unloading code.
- **Undo steps.** A plugin's own `UndoStep` subclass in the history is an
  object of the old code. It can't just be removed from the middle:
  undoing the steps before it would skip a change and leave the scene in
  a state nobody made. So the history is cut there, dropping that step and
  everything older (and, among steps to redo, it and everything after).
  Steps the facade records itself, like component edits, store JSON and
  file contents in the editor's own step types, so they survive reloads.

The general rule from editor assemblies holds: anything the editor keeps
must not hold the reloaded code's types, delegates, or objects.
