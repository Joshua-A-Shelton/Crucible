# Undo and hierarchies: records of edits, names that last, trees as lists

A learning note for M7 step 4a (ADR 0068).

## Three ways to undo

Every editor needs undo, and there are three common ways to build it.

- **Commands.** Each kind of edit is a class that knows how to do and
  undo itself: `MoveEntity`, `RenameEntity`, `DeleteEntity`. It's the
  textbook pattern, and it's clear, but every new kind of edit needs a
  new class, and an inspector can edit hundreds of fields.
- **Snapshots.** Save the whole document before each edit; undo loads the
  previous one. Nothing can go wrong in the undoing, but each step costs a
  whole scene, and loading it replaces every entity.
- **Edit records.** Record what changed, before and after, once the edit
  is done. If the records are generic ("this component of this entity
  was this, and is now that"), a handful of kinds covers everything.

Crucible uses records, with three kinds: a component's value, a subtree
made or deleted, and an entity moved in the hierarchy. A component's
value is recorded as the JSON the scene format already writes, so the
undo system needs no code for any particular component: whatever can be
saved can be undone.

## Recording after, not before

The editor makes a change, then records it. A handle drag sets the
transform as the pointer moves, then hands the history the transform's
JSON before and after. Recording after means the code making the change
stays ordinary, and the history only has to know how to go back and
forth.

## A gesture is one step

Dragging a handle sets the transform dozens of times. Undo should take
the whole drag back, not one frame of it. So the history has groups:
`BeginGroup` when the drag starts, `EndGroup` when it ends, and the steps
between undo as one. Inside a group, two edits of the same component of
the same entity merge into one (the first's "before", the last's
"after"), so a long drag doesn't keep a step per frame.

## Names that survive deletion

A step can't remember an entity by its handle. Delete a house, then
undo: the house comes back, but as a new entity with a new handle. A step
recorded before the delete that says "move entity 42" would now point at
nothing, or worse, at something else.

Every entity in a scene already has a lasting name: its scene ID, the
random number the scene file uses for it (ADR 0041). Restoring a deleted
subtree keeps those IDs. So every step names entities by scene ID, and
looks them up when it's undone or redone. Duplicating is the one case
that wants new IDs, so a copy gets fresh ones.

## Knowing what's saved

The title shows an asterisk when the scene has unsaved changes. The
history knows: when the scene is saved, it remembers the newest step
done. The scene is unchanged exactly when that step is again the newest:
undo past it and it's changed; redo and it's back. A new step after
undoing clears the steps that were undone, and if the saved one was
among them, nothing can get back there, so the scene stays changed until
saved again.

## A tree is a list with indents

A tree view looks like nested boxes, but it's drawn as a list: the
visible nodes, in order, each indented by its depth. "Visible" means the
roots and the children of expanded nodes. Expanding a node inserts its
children's rows after it; collapsing removes them. This makes everything
a list does work for a tree: scrolling, reusing rows as they scroll (only
the rows in view exist), moving focus up and down. The tree adds Left
and Right, which go out and in.

The tree doesn't hold the nodes. It asks for a node's children when it
needs them, and the caller answers from its own data (here, the world's
hierarchy). After the data changes, the tree reads it again, keeping
what was expanded and selected.

## Dropping between and inside

Dragging a row onto another can mean three things: put it before that
row, after it, or inside it as a child. The usual answer is to split each
row: the top quarter means before, the bottom quarter after, the middle
inside. A line between rows or an outline around one shows which.

Two drops make no sense: onto the dragged node itself, and onto anything
inside it (that would make it its own ancestor). The tree knows which
rows are inside the dragged one, since they're the rows right after it
with a greater depth, and refuses them.

## Why some edits are refused

A prefab instance saves only what it changes from its prefab: a
component's fields, or a child added. It can't say "the prefab's door is
gone" or "the door moved to the roof". So deleting or moving part of an
instance below its root would look fine in the editor and then vanish on
reload. The editor refuses those edits rather than lose them silently.
