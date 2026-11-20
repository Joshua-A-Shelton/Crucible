# 2D lighting

A learning note from ADR 0113: how pixel-art games light sprites, and why
Crucible uses a light buffer.

## Multiplying, not painting

A sprite's colors are the art. Lighting doesn't draw over them; it scales
them. Dark ambient light multiplies every color down (a night), a torch
multiplies the colors near it back up. Light of 1 leaves the art exactly
as drawn, which is why a world without lights, or with white ambient
light, looks as it always did. Crucible lets light reach 2: twice the
art's color, so a lamp can make a wall glow toward white instead of only
uncovering it.

## The light buffer

The cheap way to light thousands of sprites is to work out the light once
per pixel, not once per sprite per light. Each frame the lights are drawn,
added together, into an image the size of what the sprites draw into,
starting from the ambient color. Then every lit sprite reads that image
at its own pixel and multiplies. The cost is a quad per light, no matter
how many sprites there are.

Through a pixel camera the buffer is at the virtual resolution: light
changes one virtual pixel at a time, as the art does, so a light's edge is
as crisp as a sprite's. That's the pixel-art look; a full-resolution light
over scaled-up pixels looks smoother than the art.

## Linear light

Textures are stored in sRGB, which spends more of its values on dark
colors because eyes notice them more. The GPU decodes them to linear light
before a shader sees them, and encodes again when it writes. Lighting
multiplies the linear values, as light does in the world, so a light of
0.5 makes a color half as bright in light, which looks darker than "half"
of its sRGB number. That's why lights' colors are linear, as the 3D
lights' are.

## Falloff and bands

A light fades from full at its center to nothing at its range. A smooth
fade is `(1 - (d / range)²)²`: flat near the middle, soft at the edge, and
exactly zero at the range, so a light's square never shows. Many pixel-art
games step the fade instead, into a few flat rings: `bands` does that,
rounding the fade up to the next of that many levels.

## What's lit

Not everything should be: a HUD, a sky, a glowing eye. A sorting layer can
be Unlit (everything in it as drawn), and a single sprite or tilemap can
be too, inside a lit layer.

## What's missing

Light passes through walls: shadows (light blocked by colliders or tiles)
need each light's view of the walls, and come when a game needs them.
Normal maps (light catching bumps and edges by direction) need a second
image drawn for every sheet.
