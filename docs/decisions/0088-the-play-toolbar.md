# 0088 — The play toolbar

**Status:** Accepted (2026-11-11); editor usability step 2 built, then
tried by hand by the owner (Pause before Play, Step, resuming, Stop).
Names, including those added while building, settled by the owner as
drafted (docs/naming.md).

## Context

Play mode (ADR 0072) was reachable only from the Edit menu and Ctrl+P, and
a game could only run or stop. Trying physics and animation by hand needs
holding the game still, looking, and moving it on a little at a time.

## Decision

Decided by the owner (2026-11-11):

1. **A toolbar under the menu bar**, its buttons centered: Play/Stop (one
   button), Pause (highlighted while on), and Step (only while paused).
   The Edit menu has Pause and Step too; shortcuts Ctrl+P, Ctrl+Shift+P,
   Ctrl+Alt+P.
2. **Paused, gameplay stops and drawing runs:** the game's BeginFrame
   through EndFrame systems (input, physics, animation, gameplay) don't
   run; its RenderPrepare and RenderSubmit systems (its UI, debug drawing)
   still do. The editor works as usual.
3. **Sound is held:** voices and music hold where they are, silent, and go
   on from there; sounds scheduled for later move by the length of the
   pause, so they still land on their frames.
4. **Step runs one fixed tick:** exactly one physics tick (1/60 s), and the
   rest of that frame with one tick as its time; the same every press.
5. **Pause works before Play:** Play then sets the scene up, runs the first
   tick, and holds. Pause stays on across Stop.

## How it works

- **Gating the phases.** Each phase's editor system (PlayPhaseSystems.cs)
  asks the shell to run the game's systems of that phase; paused, the shell
  runs only RenderPrepare and RenderSubmit. The editor's frame loop keeps
  its own clock and fixed ticks; on resume, the game's first frame gets a
  normal frame's time, so nothing jumps by the length of the pause.
- **A step frame.** Step marks the next frame. In it, the gameplay phases
  run with `DeltaSeconds` one tick, `FixedSteps` 1, and `FixedAlpha` 1 (the
  newest tick shown); of the fixed ticks the editor's loop runs that frame,
  only the first reaches the game, and if it runs none, the step's tick runs
  before Update. A frame started paused by Play is a step frame too.
- **The mixer.** `audio::pauseAll(paused)` holds the mixer: its cursor keeps
  time (the input clock's mapping is unchanged), but each held block is
  silence and every voice's start and fade frames move on with the cursor,
  so a playing voice resumes at its next sample and a scheduled one lands
  as far ahead of its time as before. Effects (reverb, glides) hold too.
  `advancePaused(seconds)`* (internal) mixes the next seconds and holds
  again: a step's tick of sound. Pause and Advance are mixer commands; the
  null audio ignores them (`AudioService::pauseAll` and `advancePaused`*,
  null for none).
- **Stop while paused** lets the mixer go first, so Stop's fade-out runs.

## Known limitations

- A sound started during a step is due 50 ms later (the schedule delay,
  ADR 0082), so with only a tick of sound per step it's heard after three
  steps, or on resume.
- Time the game reads as a moment (`FrameStartNs`) still passes while
  paused; what's measured in deltas doesn't.

## As built

- crucible/Audio.h (`pauseAll`, `advancePaused`*), the mixer's `Pause`* and
  `Advance`* commands and `hold`*.
- C#: `Shell.IsPaused`, `Pause(bool)`, `Step()`; commands `Play.Pause`,
  `Play.Step`; `PlayToolbar` (`PlayButton`*, `PauseButton`*,
  `StepButton`*), `PlayToolButton`* and `PlayGlyph`*; `EditorWindow.AddStrip`*;
  `Shell.Toolbar`*; the title's "(paused)", the status "Paused" and
  "Stepped one tick".
- Tests: `AudioPauseTests` (native), `PlayToolbarChecks` (CTest's
  `launcher.jit.play-toolbar`, a ball's speed timed through each press).
