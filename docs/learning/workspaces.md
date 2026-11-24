# Workspaces

How an editor gives specialized work a place of its own, and what that
means for layouts, keys, and the things a tab points at (ADR 0119).

## Modes and documents

Editors have two ways to give a task its own room. A *mode* changes the
whole editor: a layout of its own swapped in, its tools in place of the
usual ones, until you leave (Blender's workspaces, the old map mode). A
*document* opens beside what's there, as a tab you can switch away from
and back to (a code editor's files, Unity's Animation and Shader Graph
windows). Modes are simple and focused, but only one is on at a time and
leaving it loses where you were. Documents let several tasks stay open,
side by side if you like, at the cost of sharing the screen.

Crucible's workspaces are documents: a tab among the panels.

## One panel of each, shared

The editor has one Hierarchy and one Inspector, and they follow the
selection. A workspace doesn't get its own copies: it has the parts that
belong to its task (a Palette, a view, its Layers), and uses the shared
ones like everything else. That keeps one selection and one undo history
for the whole scene, so a change in one tab is the same change everywhere.

## Several views of one scene

A view is a camera and an image to render into. The scene is one world;
each view renders it from its own camera into its own surface. Rendering
costs per view, so a view whose tab isn't shown draws nothing, and two
shown side by side cost two renders.

## Keys and focus

Keys go to the element with *focus*, the one last clicked or tabbed to.
Keys it doesn't use *bubble* up through its parents. A workspace's keys
are checked on the way: if focus is inside its tab, its shortcuts win
over the editor's. Click the Viewport and the editor's keys are back. The
same key can mean different things in different places, without a
global list of who owns what.

## Pointing at things that may go away

A tab outlives a lot: undo, another scene opened, the editor restarted.
So a workspace doesn't hold its entity directly; it keeps a stable name
for it (the entity's scene ID and its scene) and looks it up when it needs
it. Each frame it's asked whether it can go on; when its entity is gone,
its tab closes. After a restart the tab first comes back as a placeholder,
and becomes the workspace again once the scene with its entity is open.

## A debugging note: lifetimes in debug builds

Unloading code needs nothing to refer to it. In a debug build, .NET keeps
a method's temporary values alive until the method returns, not just
until their last use. A test that looks at a workspace and then reloads
the code in the same method call keeps the old workspace, and so the old
code, alive until that call ends. Doing the reload in the next call (or in
a helper method) avoids it. Release builds don't do this.
