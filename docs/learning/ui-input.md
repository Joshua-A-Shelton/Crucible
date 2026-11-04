# UI input: routing, focus, and navigation

A learning note for M6 step 6 (ADR 0062).

## Bubbling

When a click lands on a label inside a button inside a panel, which one
should hear about it? All of them, in turn, innermost first: the event
*bubbles* up from the element under the pointer through its parents,
until one marks it handled. The label usually doesn't care, the button
does (and handles it), and the panel never sees it. The same goes for
keys, starting at the focused element.

## Capture

A button should only "click" if the pointer is pressed and released over
it. But if the press is on the button and the pointer slides off before
the release, the button still has to learn about the release, to stop
looking pressed. So a press *captures* the pointer: until the release,
pointer events go to the pressed element wherever the pointer is. A
release over the pressed element is the click.

## Focus, tab order, and scopes

Keys need a destination: the *focused* element, which a click, Tab, or
navigation moves. Tab walks the focusable elements in a fixed order (tree
order, adjusted by an optional index), wrapping at the end. A modal
dialog shouldn't let Tab escape into the screen behind it, so it's a
*focus scope*: Tab and navigation from inside stay inside.

## Spatial navigation

A gamepad has no pointer, only directions. Pressing right should move
focus to whatever is "to the right", which is ambiguous when things
aren't in a neat grid. The usual answer is a score: among the elements
ahead in that direction, prefer small gaps along the direction, and
penalize sideways offset more (here twice as much), so the button
straight across beats a closer one diagonally up. When a layout makes the
guess wrong, an element can name its neighbors explicitly.

## Letting controls go first

A list wants the arrow keys to move its selection; a slider wants them to
move its value. So arrows go to the focused element first, as ordinary
key events; only if nobody handles them do they become navigation, and
only if nobody handles *that* does focus move. Plain menus navigate for
free, and controls that need arrows simply take them.

## Sticks need hysteresis

A thumbstick reports a continuous value. Navigating whenever it crosses
0.5 would flicker if the player rests near 0.5. So it counts as pushed
past 0.5 but only as released below 0.4: two thresholds (hysteresis)
with a dead band between. Holding it (or the D-pad) repeats after a
delay, as keyboards do.
