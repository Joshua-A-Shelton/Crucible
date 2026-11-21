# 0113 — 2D lighting

**Status:** Accepted (2026-11-20); M11 step 5, tried by hand by the owner.
Names settled by the owner as drafted (2026-11-20; docs/naming.md).

## Context

M11 step 5: lights for the pixel-art path's sprites and tiles (ADR 0005),
keeping its look: light on the pixel grid, no smoothing past the art, the
art exact when unlit.

## Options

**Model:** a light buffer at the virtual resolution that lit sprites are
multiplied by; per-pixel lighting in the sprite shader, each sprite
looping over its lights; normal-mapped sprites (a normal map drawn for
every sheet).

**Lights:** ambient, point, spot. **Shadows:** none yet; hard shadows from
colliders and tilemap outlines. **What's lit:** per sorting layer, per
sprite, or both. **Brightness:** up to twice the art, or only revealing
it. **Falloff:** smooth, or with a banded option. **Which layers a light
lights:** every lit one, or a list per light.

## Decision

Decided by the owner (2026-11-20):

1. **A light buffer.** Each frame the Sprites step draws the point and
   spot lights into a buffer the size of the image the sprites draw into
   (a pixel camera's virtual resolution, else the view's), cleared to the
   ambient light; each lit sprite and tile is multiplied by it at its
   pixel. Light changes a (virtual) pixel at a time. Without point or spot
   lights there's no buffer: the ambient multiplies as a constant.
2. **`PointLight2D`** (`color`, `intensity`, `range`, `bands`),
   **`SpotLight2D`** (also `innerConeAngle`, `outerConeAngle`, full
   angles in radians as SpotLight's, shining along its entity's local
   down, so a lamp shines down unturned), **`AmbientLight2D`** (`color`,
   `intensity`; one per world, the lowest entity index's, as in 3D).
   Without an ambient light the ambient is white: the art as drawn,
   lights only brightening; existing scenes look as they did.
3. **Up to twice the art's colors:** light past 2 is cut, so a torch can
   make a wall glow toward white.
4. **Bands:** 0 fades smoothly (`(1 - (d / range)²)²`); more steps the fade
   into that many flat rings.
5. **Every lit layer** gets every light (one buffer).
6. **Both switches:** a sorting layer's **`Unlit`** in the project file
   (a UI or sky layer), and **`Sprite.unlit`**, **`Tilemap.unlit`** (a
   glowing eye in a lit layer). Lit is the default everywhere.
7. **No shadows yet**; normal maps and lights for some layers only also
   wait (open items).
8. **The editor:** the hierarchy's Create > 2D > **Point Light**, **Spot
   Light**, **Ambient Light**; a selected light's range (and a spot's
   cone) drawn over the viewports.

## How it works

- `Light2DInstance` (Sprites.cpp, light2d.slang): position, a spot's
  direction, color times intensity, range, the cone's half angles'
  cosines, bands; gathered after the sprites, uploaded per surface and
  frame slot (`lights2DDescriptor`).
- The `Lights2D` pass draws a square per light into a transient
  `R16G16B16A16Float` image (light past 1 kept), additively, cleared to
  the ambient; sprite passes read it at their own pixel
  (`lightBufferDescriptor`, written while recording), through a pixel
  camera for the image behind the 3D and the one in front alike.
- `SpriteInstance.lit`, from the sorting layer's mask
  (`render2d::setUnlitLayers`, from RuntimeSettings) and the sprite's or
  tilemap's own switch. `SpriteConstants` gains the light buffer's slot
  and the ambient.
- Light multiplies the art's linear colors (the GPU decodes the sRGB
  texture and encodes the sRGB target), so a light's color is linear, as
  3D lights' are; under white light the art is exactly as drawn.

## Consequences

- A world with no 2D lights draws exactly as before; one with only an
  ambient light costs nothing extra; lights cost a small pass of quads
  at the virtual resolution, whatever the sprite count.
- Tests: native, on every backend (ambient per channel, a point light's
  fade and range, bands, the 2x cap, an unlit sprite and an Unlit layer,
  a spot's cone turned with its entity, and light a virtual pixel at a
  time through a pixel camera); the editor's `launcher.jit.lights-2d`.
