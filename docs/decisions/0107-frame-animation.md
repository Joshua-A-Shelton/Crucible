# 0107 — Frame animation

**Status:** Accepted (2026-11-19); M11 step 3 built. The design's names
were settled before building; those added while building are marked `*`
until the owner settles them (docs/naming.md).

## Context

Steps 1 and 2 (ADR 0104, 0106) draw a sprite as one rectangle of a
texture. Pixel-art characters animate by showing one drawing after
another: a walk is four or eight frames of a sprite sheet, each held for a
moment. ADR 0005 asks for frame animation on the 2D path, and 3D already
has clips, events, and state machines (ADR 0078, 0081) a 2D game would want
the same of.

## Options

**Slicing:** a grid only (cell size, margin, spacing); or rectangles drawn
one by one; or both.

**Where clips are authored:** in the sheet's import settings; or as files of
their own (an .crspriteclip authored like .crstates).

**Playing:** a component that plays one clip, with game code switching
clips; or state machines now, as 3D has them.

**Events:** none yet; or frame events through the 3D event system.

## Decision

Decided by the owner (2026-11-19):

1. **The Sprite Sheet Editor** (an editor window), opened when a texture's
   usage becomes Sprite, from the Inspector's or the asset browser's "Edit
   Sprite Sheet…", or by double-clicking a texture. It shows the sheet
   zoomed with square pixels, its frames outlined. `Slicing` is `Grid`
   (`CellWidth`, `CellHeight`, `Margin`, `Spacing`; the default) or
   `Rectangles` (`Frames`, each `X`, `Y`, `Width`, `Height`, drawn by
   dragging; a grid can become rectangles to adjust). `Clips` each have a
   `Name`, `Frames` (a `Frame`, a `Duration` in seconds, `Events`), and
   `Loop`, with a preview.
2. **Clips live in the sheet's import settings** and are imported as
   `.crspriteclip` sub-assets ("Hero.png / Walk"), so state machines and
   components name them by asset ID.
3. **`SpriteAnimator`** (`clip`, `speed`, `stateMachine`; C++
   `crucible::render2d`, C# `Crucible.Render2D`) plays a clip on its
   entity's Sprite, or lets a `.crstatemachine` choose: state machines now,
   the same runtime as 3D, with parameters, transitions, and exit times.
4. **Frame events** go through the AnimationEvent system 3D clips use.

## Why

Most sheets are grids, so a grid is the default and quickest; packed
sheets and odd-sized frames need rectangles, and turning a grid into
rectangles covers "almost a grid". Keeping clips with their sheet means
one place to edit them, beside the frames they name, and sub-assets make
them assets anything can point at, as a model's clips are. One state
machine runtime and one event list for 2D and 3D means one thing to learn,
and gameplay code reading events or setting parameters doesn't care which
kind of character it talks to.

## How it works

- **The importer** (TextureImporter, version 3) cuts the sheet into frames
  (`TextureImporter.FramesOf`*: a grid's cells left to right, top to
  bottom; without a cell size, the whole texture), checks every clip's
  frames are the sheet's and inside it, and bakes each clip: its frames'
  regions, start times, and durations, its events at their frame's start.
  The runtime never slices.
- **The file** (crucible/SpriteClipFile.h*): an SPCL file with INFO
  (`SpriteClipInfo`*: the sheet's ID, duration, frame count, loop), FRMS
  (`SpriteClipFrame`*), and EVNT (`ClipEvent`, as 3D clips have).
- **Playing** (SpriteAnimation.cpp*): a player per SpriteAnimator, made
  and dropped as the components come and go. "Animation.Update" moves them
  on after the 3D poses, so their events join the same list, sorted by
  timestamp. Without a machine, the clip's time moves at `speed`, looping
  as the clip says. With one, a state shows the clip its parameters give
  the most weight (frames can't blend, so a blend space picks the nearest);
  its progress is a share of that clip, so exit times read as for 3D, and
  a transition at an exit time partway through a frame hands the rest of
  the update to the next state. Blend times are ignored: frames cut.
- **The Sprite** gets the clip's sheet as its texture and the frame's
  region; it's only written when that changes.
- **The calls 3D uses** (`animation::setFloat`, `setBool`, `trigger`,
  `currentState`, `findState`, `events`) reach a SpriteAnimator's machine
  when the entity has no Animator. Sprite clips and machines are read
  again by `reloadAnimationAsset` after a reimport.
- **The editor:** the Sprite Sheet Editor* panel edits a copy of the
  settings; Apply saves them, which imports the sheet again; Revert drops
  them. Its sheet and preview draw through the UI with
  `Canvas.DrawImage(..., pixelated*: true)` (`Image.Pixelated`*), the
  nearest texel instead of a blend.

## Known limitations

- **Windows** (named ranges, ADR 0081) aren't on sprite clips yet; frame
  events cover hits and footsteps.
- **A new image doesn't open the editor** on import: images import as
  Color, so it opens when the usage becomes Sprite. Dropping files into
  the editor (after M11) can ask.
- **Frame numbers** aren't drawn on the sheet; the selected frame's number
  and size show under the settings.
- **Undo:** the editor's edits are held until Apply, as the Inspector's
  import settings are; they aren't undo steps.

## As built

- Native: crucible/Sprite.h's `SpriteAnimator`; crucible/SpriteClipFile.h;
  SpriteAnimation.h and .cpp (`advanceSpriteAnimators`*,
  `setSpriteParameter`*, `spriteCurrentState`*, `spriteFindState`*,
  `reloadSpriteAnimationAsset`*, `releaseSpriteAnimators`*,
  `registerSpriteAnimator`*); `pastExit` moved to StateMachine.cpp for both
  runtimes.
- C#: `SpriteAnimator`'s speed of 1; `TextureImportSettings`' sheet
  settings, `SpriteSlicing`*, `SpriteFrameSettings`*, `SpriteClipSettings`*
  (with `FramesPerSecond`*, the duration new frames get),
  `SpriteClipFrameSettings`*; the editor's `SpriteSheetPanel`*,
  `Shell.ShowSpriteSheet`*, the Inspector's Edit Sprite Sheet… button.
- Tests: native (SpriteAnimationTests*): frames in turn, looping and
  holding, speed, events with timestamps, a state machine on triggers and
  an exit time, reloading. C#: the importer's grid, baked clips, and
  errors; the component's defaults. The editor's SpriteSheetChecks*
  (CTest `launcher.jit.sprite-sheet`*).
