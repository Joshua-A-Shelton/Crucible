# 0128 — The character editor

**Status:** Accepted (2026-11-25); step 10 of editor extensions, its
demonstration, tried by hand by the owner. Names settled by the owner as
drafted (2026-11-25; docs/naming.md).

## Context

The editor extensions milestone was asked for with an example: an RPG's
character editor, the character's model in a view of its own, its stats
edited, its animations played. Steps 1 to 9 built the public API for it;
the demonstration shows a game building one on that API alone. The owner
called it a proof of concept, not a finished editor: it won't go into the
engine.

## Options

**Where:** the Walker, as a source plugin; the Walker's own editor code;
a new sample project. **What the workspace has:** the model in a preview
with its clips played and scrubbed; a stats drawer; equipment on bones; a
viewport tool. **The stats at runtime:** used lightly; editor data only.
**Checked:** a CTest drive; by hand only.

## Decision

Decided by the owner (2026-11-25):

1. **A source plugin in the Walker**, `Characters`
   (`samples/Walker/Project/Plugins/Characters/`): its game code in
   `Code/`, its editor code in `Code/Editor/`, as any plugin's (ADR 0118).
2. **Simple: the preview and the stats.** `Character` (`Strength`,
   `Agility`, `Pace`; `[Category("Characters")]`) on the Walk scene's
   Walker. `CharactersExtension` registers `CharacterWorkspace` (id
   `Characters.Character`, opened from the component; its tab "Character:
   Walker", named after the model for an instance without a name of its
   own): the model (its Animator's skeleton) in a `PreviewView`, orbiting;
   its clips listed, the one playing lit; Pause and a slider scrubbing it.
   `CharacterDrawer` draws the stats in the Inspector: a bar each, a
   derived Power, and the generated editors below.
3. **Used lightly at runtime.** `CharacterPaceSystem` sets an Animator's
   speed to its Character's Pace while the game runs, so editing it
   changes how fast the Walker walks. Pace 1 (the scene's) changes nothing.
4. **Checked by a CTest drive**, `CharacterEditorChecks` (CTest
   `launcher.jit.character-editor`): the workspace from the Character,
   the model playing, paused, and scrubbed; the stats by their drawer,
   changed and undone; and a package with `Characters.dll` and without
   `Characters.Editor.dll`.

## Consequences

- The milestone's "done when" is met: a sample's editor code, on the
  public editor API alone, adds a character workspace with the model in a
  preview of its own, the stats edited with undo, and its clips played
  and scrubbed; map mode runs on the same API (ADR 0123); a package
  contains none of the editor code.
- A clip's length comes from setting its time past the end, which holds
  it there (ADR 0125), and reading it back; an API for asset info (a
  clip's length) can replace that when an editor needs more of it.
