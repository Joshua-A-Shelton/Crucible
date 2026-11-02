# Materials

A learning note for M5 step 6a (ADR 0052): how a surface's look is split
between what it is and how light treats it.

## Three pieces

- **The material** says what the surface *is*: its color, roughness,
  textures. A material shader computes that at each pixel in its
  `surface()` function, producing `SurfaceData`.
- **The shading model** says how light *treats* it: whether lighting
  falls off smoothly (physically based) or in steps (toon).
- **The engine** runs the lighting loop: which lights reach the pixel,
  their direction and strength, and later shadows. It hands each light to
  the shading model.

Keeping these apart is what lets a game write a new look without touching
lights, or swap a material from toon to PBR by changing one line.

## Generics instead of virtual calls

On the CPU you'd write an interface and call it through a pointer. GPUs
handle that poorly, so the shaders use Slang generics instead:
`shadeMaterial<DefaultToon>` is compiled into a separate, fully inlined
copy for each material type. There's no lookup at runtime; the cost moves
to compile time, as more compiled variants. That's why ADR 0025 limits
variants to what changes the pipeline itself.

## Hooks that carry state

A shading model gets four calls: `begin` before any light, `light` for
each one, `ambient` once, and `finish` at the end. Between them it carries
an **accumulator**, a struct of its own choosing. A simple model just adds
up light in it. A stylized one can keep more, such as the brightest light
so far or how lit the pixel is overall, and decide everything in `finish`.

## Toon's soft bands

Physically based lighting brightens smoothly as a surface turns toward
the light (N·L). Toon cuts N·L into a few bands. A hard cut aliases badly,
so each band edge is blended over a small range (`softness`). Shadowed
parts are tinted rather than darkened, and a rim light outlines edges
turned away from the camera. That's the stylized look ADR 0010 aims for.

## The material buffer

Every material's parameters sit in one big GPU buffer. A draw passes only
its material's byte offset, and the shader loads its struct from there.
There are no per-material descriptor sets: this is the bindless model
Slag is built around. Textures are just slot numbers inside the struct,
filled in when the material loads.

## Alpha testing and the depth prepass

Leaves and fences are cut out with an alpha mask. The depth prepass must
cut them too, or it would write depth where nothing is drawn and hide
what's behind. So alpha-tested materials run their surface in the prepass
(`maskPixel`) and discard below the cutoff. Opaque materials keep the
cheaper positions-only shader.
