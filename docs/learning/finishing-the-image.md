# Finishing the image: tonemapping, bloom, grading, outlines

A learning note for M5 step 9b (ADR 0055).

## From light to a screen

Everything until now is in "light units": a white wall in sunlight is
about 1, a lamp might be 20, a dark corner 0.01. A screen shows 0 to 1.
Three things bridge that gap, in order:

1. **Exposure** scales the light, like a camera's: +1 EV doubles it.
2. **Tonemapping** squeezes the endless range above 1 into what's left
   below it, smoothly, so a lamp is "very bright white" rather than a flat
   clipped blob.
3. **Grading** adjusts the result to taste: warmer, more contrast, less
   saturated.

## Tonemappers are opinions

Every tonemapper is a curve plus decisions about color. Take a saturated
orange, lit brightly:

- **Clamping** cuts each channel at 1: it turns yellow, and detail in the
  bright part is gone.
- **ACES** (the film industry's) gives a punchy, contrasty look, and
  rolls bright colors toward white, shifting hues on the way.
- **AgX** (Blender's) desaturates highlights gracefully toward white,
  keeping the hue: photographic.
- **PBR Neutral** (Khronos) leaves colors alone until they get close to
  white, then compresses only that. A stylized game's artists pick exact
  colors, and this keeps them.

Crucible defaults to PBR Neutral, and a camera can choose the others.

## Resolve after tonemapping

MSAA keeps several samples per pixel; at an edge, some are the lamp, some
the wall. Average them *before* tonemapping, and a sample of 20 next to
one of 0.1 averages to 10, which tonemaps to near white: the edge is as
jagged as without MSAA. So Crucible tonemaps each sample and averages
afterwards: the edge blends between "white" and "dark", as intended.

## Bloom

Real lenses (and eyes) spread bright light into its surroundings. Bloom
fakes it in one frame:

- Shrink the image to half size, then keep halving it, filtering each
  step so nothing flickers. That makes a chain of blurrier and blurrier
  copies.
- Climb back up, adding each smaller level, slightly blurred, onto the
  next larger one. The top is a wide, soft glow made of many blur sizes,
  which looks far more natural than one blur.
- Mix a little of it (a few percent) into the image before tonemapping.

Without a threshold, everything blooms a tiny bit, so brighter things
simply glow more. A threshold makes only light above it bloom: crisper,
"only the magic glows", but things can pop as they cross it.

## Grading without textures

- **Lift** adds to the shadows.
- **Gain** multiplies the highlights.
- **Gamma** bends the middle.
- **Contrast** pushes values away from mid-gray.
- **Saturation** pushes colors away from (or toward) their gray.
- **White balance** shifts the whole image warmer or cooler, the way a
  camera does: for a cold blue night, or golden hour.

Each is zero when it changes nothing, so a camera can add the settings
component and adjust only what it needs.

## Outlines: two ideas

**Inverted hull.** Draw the mesh a second time, a little bigger, inside
out (only the faces pointing away), in a flat color. Where the bigger
copy sticks out behind the real one, you see a line: the silhouette. It's
real geometry, so MSAA smooths it, and each material can choose its width
and color.

Its weak spot is hard edges. A cube's corner has three vertices, one per
face, each with its own normal; pushed out along those, the faces
separate and the outline has gaps. Smoothing the normals fixes the
outline but makes the cube *shade* rounded. So the importer stores a
second set, **outline normals**, the average of all normals at each
position, used only for pushing the hull out.

**Edge detection.** After drawing, look at each pixel's depth and normal
and compare its neighbours: a big jump in depth is a silhouette; a sharp
change in normal is a crease; two objects meeting is an intersection. It
outlines everything, including lines hulls never can. But it costs the
same whatever is on screen, and it's hard to tell it "not this object".

Crucible chose hulls for now (characters, per-material control) and
keeps edge detection, and lines drawn by the material itself, on the list
for later (ADR 0055).

## Width on screen

Outline width is given in pixels, so a line looks the same up close at
any resolution. But a crowd far away, each figure a few pixels tall with
a 3-pixel outline, would be mostly outline. So past a distance (10 m),
the width shrinks with distance, like the object does.
