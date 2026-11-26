# Text that doesn't fit

What a UI does when text needs more room than it gets, and how Crucible's
UI library handles it (ADR 0130).

## The choices

- **Clip it:** draw it whole and let it run under whatever's next, or cut
  it off at the edge. Cheap, but a reader can't tell anything is missing,
  and "7836" looks like a real number.
- **Wrap it:** break onto more lines. Good for sentences; poor for names
  in a column (a long word breaks mid-word, "Orthograph / ic").
- **Trim it:** cut it and say so with an ellipsis, "Orthographic H…".
  The reader knows there's more, and a tooltip can show the rest.

Most tool UIs trim: rows, tabs, and fields keep one line each so lists
stay aligned and scannable.

## Where to cut

- **At the end** for names and sentences: the start says what it is.
- **In the middle** for paths: the start says where, the end says which
  file, and the middle is the least useful part.
  "Tilemaps/…/Tilemap 4.crtilemap" beats "Tilemaps/Levels/Fore…".

## How cutting works

Text is shaped once into glyphs with positions. To cut it to a width:

1. Measure the ellipsis.
2. Ask the shaped text which caret (a place between characters) is
   nearest the room left, and step back until it's inside.
3. Keep the text before that caret, add "…", and shape the result.

Cutting at carets, not at bytes or characters, keeps accented letters and
emoji whole. Kerning can make the cut text a hair wider than predicted,
so it's measured again and trimmed a character more if needed.

## In Crucible

- `Label.Trimming`: `None` (default), `End`, or `Path`, settable in a
  theme: `{ "Selector": "ListItem Label", "Set": { "Trimming": "End" } }`.
- A cut label shows its whole text as a tooltip, unless it has its own.
- Text fields not being edited show their start, cut with "…"; editing
  shows everything and scrolls.

## Try it

Drag the Inspector's name column narrower (the thin strip right of the
names): long names end in "…", and resting the pointer on one shows the
whole name.
