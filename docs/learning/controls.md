# Controls: parts, popups, and virtual lists

A learning note for M6 step 7 (ADR 0063).

## Controls made of parts

A slider is a track and a thumb; a toggle is a box and a mark. Making
those *parts* ordinary child elements, each with its own type, means a
theme styles them like anything else ("SliderThumb:hover grows a
little"), and each part gets its own hover and pressed states for free,
because the pointer hits it like any element. Where the selector set has
no word for a control's state (a toggle being on), the control puts a
class on itself and its parts (`on`) instead.

## Popups

A dropdown's list has to draw over everything else and catch the pointer
first, even though it belongs to a combo box deep in the tree. So the
root keeps a separate *popup layer*: laid out after the content, drawn
after it, and hit-tested before it. A popup is positioned against its
anchor (below it, or above if there's no room), keeps focus inside like a
modal dialog, and closes when you press outside it. That press is used up
closing it, so it doesn't also click whatever was underneath.

## Virtual lists

A list of 10,000 save slots doesn't need 10,000 elements: only a screen's
worth are visible. A *virtualized* list makes elements only for the rows
in view, and when you scroll, it reuses the rows that scrolled out for
the ones scrolling in, just rebinding them to different items. Element
count stays constant whatever the list's length.

The hard part is height. If every row is the same height, the position of
row 5,000 is simple multiplication. If rows differ (wrapped text, a
header), nobody knows the total height until every row has been measured,
which defeats the point. So unmeasured rows are *estimated* (from the
average of those measured so far), each row is measured when it first
comes into view, and the scrollbar settles as more of the list is seen.
One more trick keeps it from jumping: when a row above the view turns out
taller than estimated, the scroll offset moves by the difference, so
what's on screen stays put.
