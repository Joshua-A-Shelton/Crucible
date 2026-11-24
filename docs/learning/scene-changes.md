# Scene changes

How extensions get a say in the editor's own operations without breaking
them, and how several edits become one undo step (ADR 0122).

## Before and after

Code that reacts to something can run before it, with the chance to stop
it, or after it, once it's done. *Before* events are powerful: an
extension can veto a delete or ask a question first. *After* events are
safe: nothing an extension does can stop the operation. Many editors offer
both, and so does Crucible: Duplicating, Renaming, and Deleting before;
Duplicated, Renamed, and Deleted after.

## Asking takes time

A question shown to the person is answered later, not inside the event
that asked it. So a *before* handler can't simply return "yes" or "no".
Instead it takes a *hold*: the editor sees the operation is held and waits.
When the answer comes, the hold says Go or Cancel. Once every hold is
released (and nobody cancelled), the editor carries on. It's the same idea
as a deferral in other event systems: a token that says "not yet".

## One undo step for several edits

Undo history records steps. An operation that makes several changes (an
entity deleted, and its map file with it) should undo as one, or a single
Ctrl+Z would leave things half done. A *group* gathers the steps between
its start and end into one. The editor opens a group around each of its
operations, runs the extensions' added work and *after* handlers inside
it, and closes it: whatever they changed undoes together.

## Not reacting to yourself

An extension's own edits (Create, Destroy, Rename through the API) don't
raise these events. Otherwise two extensions reacting to each other's
changes could loop, or ask questions about edits nobody made by hand. The
events are for what a person does in the editor.
