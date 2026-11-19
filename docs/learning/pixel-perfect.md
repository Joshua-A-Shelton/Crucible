# Pixel-perfect

A learning note for M11 step 2 (ADR 0106): drawing pixel art at its own
resolution, scaling it up cleanly, and layering it with 3D.

## The virtual resolution

Pixel art is drawn on a coarse grid. A whole screen might be 320x180
pixels, though the player's window is 1920x1080. The game is meant to look
as if that small screen were blown up: every art pixel a clean 6x6 block.

Drawing sprites directly at 1920x1080 only looks like that while every
sprite sits exactly on the coarse grid. Move one by a fraction and it lands
between blocks; turn one and its edges become staircases of tiny steps,
finer than any pixel the artist drew. Pixel artists call those *mixels*
(mixed pixels), and they read as a mistake.

So a pixel camera draws the sprites into a small texture at the virtual
resolution, 320x180 here, and only then scales that texture up to the
window. Everything is on the grid because the grid is all there is. A
turned sprite turns in chunky steps; a moving one moves a whole art pixel
at a time. The camera itself snaps to the grid too, or the whole image
would crawl as it scrolled.

## Fitting the window

1920x1080 is exactly 6 times 320x180, but players' windows come in every
size. Two answers:

- **Exact:** scale by the largest whole number that fits, and fill the rest
  with black bars. Every art pixel is a perfect square of window pixels.
- **Sharp:** scale by whatever fills the window (say 4.25), drawing each
  window pixel from the middle of its art pixel, except the one or two
  window pixels on each edge between art pixels, which blend the two. The
  image fills the window and stays crisp; only edges soften, by under a
  pixel. At a whole-number scale it's exactly the same as Exact.

Ordinary smooth (bilinear) scaling would blur every art pixel into its
neighbours; nearest-neighbour scaling at 4.25 would make some art pixels 4
window pixels wide and others 5, which shows as a ripple when the image
scrolls. Sharp scaling avoids both.

## Layering with 3D

A pixel layer can sit over a 3D scene, each seen through its own camera:
the 3D through a perspective camera, the pixel art through the pixel
camera. Drawing the pixel art after the 3D has been tonemapped keeps its
colors exact.

Drawing pixel art *behind* 3D is harder, because by then the 3D is already
in the image. Crucible looks at the 3D's depth buffer, which records, for
every window pixel, how near the nearest 3D surface was, and draws the
behind-layers only where nothing was drawn (the sky counts as nothing). A
pixel background shows around the 3D objects, colors still exact. The cost
is that a 3D object's edge against it is hard, decided per pixel, which in
a pixel-art game looks right anyway.
