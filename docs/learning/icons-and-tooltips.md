# Icons and tooltips

How editors show small symbols and name them, and where a brush is held
(ADR 0124).

## Icon fonts

An icon font is a font whose characters are pictures. Each icon is a
single character, usually in Unicode's *private use area*, a range of
codes set aside for things that aren't letters. Because it's a font, an
icon is drawn by the same machinery as text: sharp at any size, and in
whatever color the text would be. A dark theme and a light one get the
right icon color for free, which images can't do without a copy per
theme. Icon sets publish a table from each icon's name to its character,
so code asks for "eraser" rather than a number.

## Tooltips and timing

A tooltip names a button that has only an icon. It shouldn't pop up the
moment the pointer passes over something on its way elsewhere, so it
waits until the pointer *rests*, typically half a second to a second.
It also gets out of the way: pressing, typing, or scrolling hides it, and
it stays hidden on that element until the pointer leaves, or it would
reappear under the pointer as soon as the click was done. A tooltip never
takes clicks or keyboard focus itself; it's a label floating above
everything, not a window.

## Where a brush is held

A brush made of several tiles has to be held by one of them, the way a
stamp is held by its handle. Holding it by a fixed corner feels wrong:
you picked the tiles up from one spot and the brush jumps away from the
pointer. Map editors hold it either by its middle or by the cell you
started dragging from. Crucible does the second, so the tile you grabbed
is the one that lands under the pointer.
