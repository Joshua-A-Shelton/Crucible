# Text editing: typed text, carets, and undo

A learning note for M7 step 1 (ADR 0065).

## Keys are not text

A key event says which key went down: its *scancode*, a position on the
keyboard. It doesn't say what character that makes. The same key types
"q" on QWERTY and "a" on AZERTY, Shift changes it, a dead key (´ then e)
makes one character from two presses, and an input method turns several
presses into a word. So the OS sends typed text separately, already
worked out, and a text box takes its characters from *text input*
events and only its commands (arrows, Backspace, Ctrl+C) from key
events. Both arrive for the same press: the field handles the key event
too, so Space, say, types a space instead of also pressing a button.

Text input is something you turn on, and off. While it's on, the OS may
show an on-screen keyboard or an input method's window, which is why the
UI turns it on only while a text field has focus.

## Where text lives between C++ and C#

Most input events are a few numbers, but a text event carries a string
of any length. Rather than make every event big enough for text, each
text event points into one buffer of the frame's typed text: where its
text starts and how long it is. C# reads that buffer once a frame into a
buffer it keeps, so typing allocates nothing in the frame loop.

## Indices, characters, and carets

A caret sits *between* characters, so the caret positions in "abc" are
0 to 3. But "character" needs care. C# strings count UTF-16 units, where
an emoji is two; the native layout counts UTF-8 bytes, where "é" is two;
and "e" followed by a combining accent is two code points that show as
one letter. Moving the caret by one must step over a whole visible
character (a *text element*, or *grapheme cluster*), or Backspace would
leave half a letter behind. The text layout converts between its byte
offsets and the string's indices, and snaps a position inside a cluster
to its start.

## Selection is two positions

A selection isn't a start and a length, but an *anchor* (where it began)
and the *caret* (where it is now). Shift+Right moves the caret and leaves
the anchor, so the selection grows or shrinks from the side you're
moving. With no selection, the two are equal. Typing replaces what's
between them.

Moving up and down between lines aims at the caret's x, but lines are
different lengths: going down through a short line and on to a long one
should land back in the original column. So the field remembers a *goal
x* while you move up and down, and forgets it when you move any other
way.

## Undo in groups

Undoing one letter at a time is useless; undoing a whole paragraph is too
much. Text boxes group a *run* of typing into one step: before the first
character of a run, the field saves the text, and further characters add
to the same step. Anything else (moving the caret, pasting, deleting
after typing) ends the run. Redo is the same history in reverse, and any
new edit clears it.

## Scrolling a line by whole pixels

A one-line field shorter than its text scrolls sideways to keep the
caret inside. The scroll is rounded to whole *physical* pixels: exact
glyphs are rasterized for pixel positions (ADR 0064), and text moved by
a fraction of a pixel would land between them.

## Redrawing for a blink

The UI redraws only when something changes. A caret blinking twice a
second is a change with no input behind it, so the focused element gets a
tick on every update and asks for a redraw only when its blink flips.
An idle screen with no focused field still draws nothing.

## Scrubbing numbers

Editors let you drag across a number to change it: quicker than typing
when you're adjusting by eye. But a press on a field also places the
caret. The field tells them apart by which press it is: the press that
gave it focus can scrub, and once it's focused, presses edit text. A
press that doesn't move far (3 units) wasn't a drag after all, and leaves
the text selected to type over.
