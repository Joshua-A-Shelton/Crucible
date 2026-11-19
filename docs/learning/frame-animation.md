# Frame animation

A learning note for M11 step 3 (ADR 0107): animating pixel art by showing
one drawing after another, and driving it the way 3D characters are
driven.

## Sprite sheets

A 3D character moves by turning bones; a pixel-art character moves by
being redrawn. An artist draws each pose of a walk (four, six, eight
drawings) and the game shows them in turn, each for a moment. The drawings
are kept together in one image, a *sprite sheet*, because one texture is
cheaper to load and draw from than dozens of tiny ones.

Cutting the sheet into frames is called *slicing*. Most sheets are a grid:
every frame the same size, sometimes with a border around the whole sheet
(*margin*) and a gap between cells (*spacing*), which stops the edge of one
frame bleeding into the next when the art is filtered. Frames are numbered
left to right, top to bottom, like reading. Packed sheets put frames of
different sizes wherever they fit, so they need each rectangle given by
hand.

## Clips and timing

A *clip* is a list of frames and how long each shows. Giving every frame
its own duration, rather than one rate for the whole clip, lets an artist
hold a key pose longer: the wind-up of a punch quick, the impact frame
lingering. A rate (12 frames a second is common for pixel art, slower than
film) is still the handy default for new frames.

Playing a clip is only bookkeeping: time moves on by the frame's length
times the speed, and the frame shown is the one whose stretch of the
timeline contains the time. A looping clip wraps round; one that doesn't
holds its last frame (a jump's landing pose) until something else plays.

## Why bake

The sheet's settings say "frame 6 of an 8x8 grid"; the runtime only wants
"x 16, y 8, 8 by 8". Working that out on import, once, means the game
never slices anything, and changing the grid can't silently shift a clip
at run time: the import checks every frame a clip names is really on the
sheet, and fails with a message if not.

## Events on frames

A footstep sound should play on the frame the foot lands, not on a timer
that drifts from the drawing. So frames can carry *events*, names reported
when playing passes that frame's start. The same event list serves 3D clips
(ADR 0081), so gameplay reads "Step" the same way whatever drew it, and
each event's timestamp says when, within the frame, it happened.

## State machines for frames

A character is more than one clip: idle, walk, jump, land. Choosing between
them by hand gets tangled fast, which is what state machines are for: each
state shows a clip, and transitions say when to move between them (a
parameter crossing a value, a trigger, or the clip reaching a point, its
*exit time*).

Frames change two things compared with 3D. There's no blending: halfway
between two drawings isn't a drawing, so transitions cut, and a blend space
picks its nearest clip rather than mixing two. And timing matters more: if
a jump ends partway through a frame, the next state should already be that
far along, or every transition would add a hitch the length of a frame.
Measuring progress as a share of the clip, and handing the rest of the
update to the next state, keeps the rhythm exact at any frame rate.

## Seeing pixels

An editor showing pixel art zoomed in has to draw it with *nearest*
sampling: each screen pixel takes the one texel under it, so art pixels
become crisp squares. The usual smooth (bilinear) sampling blends
neighbouring texels, which is right for photos and wrong here, where it
turns a sharp sprite into a blur. Zooming by whole numbers keeps every
square the same size.
