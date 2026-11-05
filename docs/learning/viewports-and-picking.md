# Viewports and picking: a camera of its own, IDs on the GPU, handles on top

A learning note for M7 step 3 (ADR 0067).

## A viewport is just another image

An editor's viewport looks like a hole in its window through which the
scene shows. It's simpler to treat it as a picture: the scene is rendered
into a texture the panel's size, and the UI draws that texture like any
image, then draws the handles over it. The scene's renderer doesn't know
about panels, and the panel doesn't know about rendering.

The order within a frame matters. The viewport renders first, then the
window, which samples the viewport's texture. Both go to the same GPU
queue, and a queue finishes and publishes one submit's work before the
next starts, so nothing more is needed to keep them apart: the texture is
left in the "ready to sample" layout at the end of the viewport's frame,
and the window just reads it.

## The editor's camera isn't in the scene

Game cameras are entities, saved with the scene. The editor's camera
must not be: opening the editor shouldn't change the scene. So the
renderer lets a surface carry a camera of its own (a position, a
rotation, a field of view) that it draws from instead of the world's.

An orbit camera is best stored not as a position but as what it looks at
(the pivot), how far away it is, and two angles. Orbiting changes the
angles; dollying changes the distance; panning moves the pivot; the
position follows. Looking around with the right button is the reverse:
the position stays, and the pivot moves with the view.

## Picking by drawing IDs

Which object is under the pointer? A CPU answer shoots a ray into the
scene and tests it against each object's shape, or, cheaply, its bounding
box, which is wrong wherever the box is bigger than the object. A GPU
answer draws the scene once more with each object's ID as its "color",
into an integer texture, and reads back the one pixel under the pointer.
It's exactly what's drawn, at any detail, for the cost of one more
geometry pass.

Reading back takes care: the GPU runs a frame or two behind the CPU. The
pixel is copied into a small buffer, and the answer is read once the GPU
reports that frame finished, one buffer per frame in flight so a newer
frame never overwrites an answer not yet read.

The ID texture gives the selection's outline for free: wherever a
selected pixel touches an unselected one, draw orange.

## Handles on top

Move, rotate, and scale handles are lines and circles a fixed size on
screen, always visible, so they're drawn by the UI over the image rather
than in the scene. Each frame they're re-projected from the entity's
position. To keep them the same size on screen however far the entity
is, their length in the world is however many meters 90 pixels cover at
that depth.

Dragging turns pointer motion back into world motion:
- **an axis:** the point on the axis line nearest the pointer's ray;
- **a plane:** where the ray meets it;
- **a ring:** the angle swept around its axis, between where the drag
  started and where the ray meets the ring's plane now.

A square for a plane seen nearly edge-on is too thin to aim at, so it's
hidden.
