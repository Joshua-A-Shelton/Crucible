# 0065 — Text editing

**Status:** Accepted (2026-11-04); built. Names settled by the owner:
`TextField`, `NumberField`, `TextChanged`, `Committed`, the platform's
`TextInput` event, `polledText`, and the clipboard functions; the rest are
placeholders (docs/naming.md).

## Context

M7 step 1. The editor needs text boxes (names, search, notes) and number
fields (every numeric property in the inspector), and M6's controls have
none: keys reach the UI as scancodes only, with no typed text, no
clipboard, no caret. Latin text only, with no input-method composition:
non-Latin text and IMEs come after v1 (ADR 0019).

## Options

**Typed text across the boundary:** each `TextInput` event carrying its
text inline (a fixed-size array in `InputEvent`, so every event grows); or
the events pointing into a side buffer of the poll's text, read once per
frame.

**One-line fields wider than their text:** scroll sideways to keep the
caret in view; or clip with no scrolling.

**Undo:** the field's own history, grouping a run of typing into one
step; or a shared undo service now (which the editor's undo, in step 4,
may replace).

**Number fields:** typing only, with up and down to step; or also
dragging to scrub the value (as in most editors).

## Decision

1. **A side buffer:** a `TextInput` event's `code` is where its UTF-8
   starts in the poll's text and `value` its length; `polledText` copies
   that text out. `InputEvent` stays plain and small.
2. **One line scrolls to the caret,** by whole physical pixels so the text
   stays crisp. Several lines wrap, and ask the scroll views around them
   to show the caret.
3. **Each field keeps its own undo,** grouping a run of typing (or of
   deleting) into one step; moving the caret or any other edit starts a
   new one.
4. **Number fields scrub:** a press that focuses one and drags sideways
   changes the value; released without moving, it's ready to type.

## Details

- **The platform:** SDL's text input events become `TextInput` events
  (`window`, `code` = offset, `value` = length); `polledText` copies the
  last poll's text (returning its whole length, so a short buffer can be
  grown); `startTextInput` / `stopTextInput` turn SDL's text input on for a
  window (which shows an on-screen keyboard where there is one);
  `clipboardText` / `setClipboardText` reach the system clipboard.
- **The frame:** the Input phase reads the polled text into
  `FrameContext.PolledText` (a buffer grown as needed, so no allocation
  per frame), and `UIInputSystem` hands it to each root with the events.
- **The UI:**
  - a `TextInputEvent` raised on the focused element (bubbling), and
    `UIRoot.TextTyped` to raise one directly;
  - elements that take text say so (`AcceptsTextInput`), and the root
    turns the platform's text input on while one has focus (in a root
    attached to a window);
  - `PointerEvent.ClickCount` counts presses close together in time and
    place (`UIRoot.DoubleClickTime`, 0.5 s; `DoubleClickDistance`, 4
    units), for double- and triple-clicks;
  - the focused element gets `OnFocusedTick` on every UI update, for what
    changes with time alone (the caret's blink), so a blinking caret
    redraws twice a second and an idle UI not at all;
  - `Clipboard.Text` is the system clipboard, or the UI's own copy where
    the platform can't hold one (no platform started, as in tests).
- **`TextField`:**
  - one line by default, `Multiline` to wrap and take Enter as a new line
    (Ctrl+Enter commits); a pasted line break becomes a space in one line;
  - `Text` (setting it puts the caret at the end and starts a new
    history), `Select`, `SelectAll`, `CaretIndex`, `SelectionStart`,
    `SelectionLength`, `SelectedText`, `Undo`, `Redo`;
  - keys: arrows, Home and End (a line's ends in several lines, the
    text's with Ctrl), Ctrl+arrows by words, Shift to select,
    Backspace and Delete (Ctrl: a word), Ctrl+A, C, X, V, Z, Shift+Z, Y;
    up and down move between lines in several lines and, in one line,
    are left unhandled so focus moves on; every other key is handled
    (Space types, it doesn't activate) except Tab and shortcuts with
    Ctrl, Alt, or the Windows key the field doesn't use, which stay the
    game's;
  - the pointer: a click places the caret (Shift: extends), a drag
    selects, a double-click selects a word, a triple-click everything
    (several lines: the paragraph);
  - focus from the keyboard or a gamepad selects everything, ready to
    type over;
  - `TextChanged` on every change; `Committed` when an edit is done
    (Enter in one line, or focus leaving) and the text differs from when
    it began; Escape puts back the text from then, or, with nothing to
    put back, goes on as Cancel (closing a dialog);
  - the caret is one physical pixel wide at 1x (whole pixels above), in
    `CaretColor`, blinking once a second and shown again whenever it
    moves; the selection is drawn behind the text in `SelectionColor`;
    both only while focused;
  - carets step over whole characters (`StringInfo` text elements), and
    the text is shaped by the same helper as `Label`'s (font resolution
    and the cached layout, pulled out of `Label`).
- **`NumberField`** (a `TextField`): `Value` kept between `Minimum` and
  `Maximum`, `Step` (default 1), `ValueChanged`, `Format`. It shows at
  least as many decimals as the step has, more when the value has them
  (up to six), in the invariant culture. Typed text is read when
  committed; text that isn't a number puts the value back. Up and down,
  and the wheel while focused, step it (Shift: ten steps), each step a
  commit of its own; a wheel over an unfocused field scrolls past it.
  Scrubbing moves a step every 4 units after 3 units of movement, and
  commits on release.
- **The default theme:** a sunken well with a thin border, the accent
  outline while focused, a translucent accent selection.

## Why

A side buffer keeps every input event small for the sake of the rare one
with text, and reads typed text once a frame without per-event
allocation. Scrolling one line is what every text box does; rounding the
scroll keeps exact glyphs exact (ADR 0064). A field's own undo is what
text boxes everywhere do, and works before the editor has an undo model;
the editor's undo (step 4) can record a field's commits as its steps.
Scrubbing is how editors make numbers quick to adjust by feel, and tying
it to the focusing press keeps a focused field's press free for placing
the caret.

## Not yet

- **Shortcuts by key position:** keys arrive as scancodes, so Ctrl+Z is
  the key where QWERTY has Z on any layout (on AZERTY, the one marked W).
  Fixing it means sending SDL's keycodes too (docs/open-items.md).
- Input-method composition and non-Latin text (ADR 0019), password
  fields, placeholder text, and right-click menus (cut, copy, paste) come
  when something needs them; context menus are step 2's.

## As built

- Native: `InputEventType::TextInput`, `polledText`, `startTextInput`,
  `stopTextInput`, `clipboardText`, `setClipboardText` (`Platform.h`).
- C#: `FrameContext.PolledText`; `TextInputEvent`; `UIElement`'s
  `OnTextInput` / `TextInput`, `AcceptsTextInput`, `OnFocusedTick`;
  `PointerEvent.ClickCount`; `UIRoot`'s `ProcessInput(events, text)`,
  `TextTyped`, `DoubleClickTime`, `DoubleClickDistance`; `Clipboard`;
  `TextField`, `NumberField`; the internal `ShapedText` shared with
  `Label`.
- Tests:
  - native (`PlatformTests.cpp`): TextInput events pointing into the
    polled text (multi-byte text, a short buffer, a fresh poll), text
    input on and off for a window, the clipboard (SDL's offscreen
    driver keeps its own);
  - C# (`TextFieldTests.cs`, JIT and NativeAOT, synthetic events):
    typing, selecting, word moves, Space not activating, grouped undo and
    redo, copy, cut, and paste, Enter, Escape, and focus loss, clicks,
    double- and triple-clicks and drags, sideways scrolling by whole
    pixels at 1.25x, wrapping and moving between lines, the blink, number
    fields committing, rejecting, clamping, stepping, and scrubbing;
  - `launcher.jit.textfields`: a field focused by a synthetic click,
    typed into through TextInput events and polled text, its last word
    selected with Shift+Left; checked at pixels (the selection behind
    the word, the caret, the focus outline, a long field clipped at its
    edge, wrapped lines, a number) at 1.25x, saved as `textfields.png`.
