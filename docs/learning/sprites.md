# Sprites

A learning note for M11 step 1 (ADR 0104): drawing pixel art, sorting it,
sizing it, and keeping its colors exact.

## A sprite is a rectangle of a texture

A sprite is a picture placed in the world: a rectangle cut from a texture
(often one frame of a sheet holding many), put at an entity's position.
The *pivot* is the point of the picture that sits on that position.
Characters usually pivot at their feet, so a position means "where it
stands" and the picture grows upward; a coin might pivot at its center.

## Drawing thousands cheaply

A 2D level can have thousands of sprites, and drawing each with its own
draw call would spend more time talking to the GPU than drawing.
Engines *batch*: sprites that can be drawn together go in one call.
Traditionally that meant sprites sharing a texture, which is why engines
pack art into atlases.

Crucible's renderer gives every shader the whole texture heap (bindless,
ADR 0003), so a sprite can say which texture it uses by number. Every
sprite then fits in one draw: the CPU writes a small record per sprite
(where its corners go, which texture, which part of it, its tint) into a
buffer, and the GPU draws six vertices (two triangles) per record.

## Painter's order

With no depth buffer, whatever is drawn last is on top. Sorting decides
the order, at three levels: *sorting layers*, broad bands such as
background, characters, and effects; an *order in layer* for fixed
stacking within a band; and *y-sort*, for top-down games, where something
lower on screen is nearer the viewer and drawn later.

Levels with floors, such as a bridge walked under and over, are bands of
layers per floor. A character under the bridge is in a layer drawn before
it; walking up the ramp, a trigger moves it to a layer drawn after it, and
moves its collision to the bridge's physics layer too. Classic 2D games
did exactly this. Faking 2D in 3D, where a depth buffer handles it, suits
games with continuous height, but fights pixel art's grid.

## How big is a pixel?

Physics wants meters: gravity, speeds, and jump heights are tuned in them.
Pixel art is made in pixels. *Pixels per unit* joins them: at 16, a
16-pixel tile is one meter, and a 32-pixel-tall hero is two meters tall.
It's a setting on the texture, so art drawn at another resolution can mix
in at the right size.

## Keeping colors exact

Pixel art is drawn color by color, and a palette shifted by one step is a
different picture. Three things would shift it. *Compression* (BC7)
approximates blocks of texels. *Mipmaps* average texels into smaller
copies, and *linear filtering* blends neighbours, both blurring. And
*tonemapping*, which maps the 3D scene's light to the screen, bends every
color. So sprite textures are stored uncompressed with no mipmaps, sampled
nearest, and drawn after tonemapping. A texel is stored as sRGB, read into
linear light by the GPU, and written back to an sRGB screen, landing on
the same byte it started as.
