# Opening editors

How an editor finds the right tool for a file or an object, and what to do
when several tools claim it (ADR 0120).

## Associations

An operating system knows which program opens a `.png`: programs register
the kinds of file they open, and a double-click looks the file up. The
same idea works inside an editor. Each editor registers what it opens,
kinds of asset or kinds of component, and every place that shows those
(the Assets panel, the Inspector, the Hierarchy) asks the same list. A new
editor is then reachable from everywhere at once, and none of those places
needs to know it exists.

The editor's own tools register the same way as extensions' do. That's
the test of the design: if the editor's own sprite sheet and map editors
fit it, an extension's will too.

## What a kind is

An asset's kind is what made it: its importer ("Texture", "Mesh"), or, for
the editor's own files that have no importer setting, its file
extension (".crtilemap"). A component's kind is its type. These are the
names a registration uses, and the names a remembered choice is stored
under.

## When several want it

Two editors may both open textures. Picking one silently would hide the
other; asking every time is tiresome. The common answer, the one
operating systems use, is to ask once and offer to remember: "Always Open
With". The choice belongs to the person, not the project, since two
people on a team may prefer different tools, so it lives in their own
editor settings. Open With shows which one is remembered and can forget
it.

## Opening through something else

Not everything opens directly. A tilemap file is the data of a map
*entity*: map mode edits the entity, its layers, and the file together.
So opening the file means finding the entity that uses it in the open
scene: one, open it; several, ask which; none, say so. The same pattern
fits any asset that only makes sense in context.
