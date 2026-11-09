# 0070 — Editor hints for C++ components

**Status:** Accepted (2026-11-09); built. Names settled by the owner as
drafted (docs/naming.md).

## Context

The inspector's hints (ADR 0069: `[Color]`, `[Range]`, `[Tooltip]`,
`[Header]`) are C# attributes, but the components the editor shows are
the engine's C++ ones (lights, sky, camera, post-processing), whose C#
halves the binding generator writes (ADR 0017). Until play mode builds
games (M7 step 6), they're the only components in the editor, so no hint
showed anywhere: a light's color was three numbers.

## Decision

- **Macros on C++ fields**, empty in normal builds and annotations while
  the binding generator parses: `CRUCIBLE_HINT_COLOR`,
  `CRUCIBLE_HINT_RANGE(min, max)`, `CRUCIBLE_HINT_TOOLTIP("…")`,
  `CRUCIBLE_HINT_HEADER("…")`. The generator writes the matching C#
  attributes onto the mirror's field, and the serializer generator reads
  them as for any C# field.
- **Comments are tooltips:** a field's comment, after it on its line or
  the comment lines just above it, becomes its tooltip unless
  `CRUCIBLE_HINT_TOOLTIP` gives one. Its first letter is capitalized.
- **Annotated:** every light's color, the ambient light's and sky's
  colors; the spot light's cone angles (0 to π), the camera's vertical
  field of view (0.01 to 3.13), the sky's sun size (0 to 0.5);
  post-processing's bloom strength (0 to 1), and its grading under a
  "Grading" header, each −1 to 1.

## Why

The owner chose a `HINT_` prefix to group the macros and keep them
plainly editor-only, and comments as tooltips so the explanations the
headers already carry show in the editor with no extra work.

## As built

- `Expose.h`'s macros; the binding generator's `FieldHints`, read from
  annotations and the source lines around a field, written as attributes.
- **Tests:** `FieldTableTests` checks the hints on `PointLight`,
  `SpotLight`, `PostProcess`, `Camera`, and `AmbientLight`, and comment
  tooltips from the same line and from the lines above.
- **Tried by the owner on KWin (2026-11-09):** swatches, sliders, the
  header, and tooltips show, but long field names ("Inner Cone Angle",
  "Casts Shadows") ran under the controls beside them, and sliders under
  their number fields (the theme's sliders are a fixed 200 wide). Names
  now wrap within their column, and the inspector's sliders take the room
  left; `launcher.jit.inspector` checks a spot light's names and controls
  don't overlap.
- **Tried again by the owner:** names and sliders stay in place; only in
  a very narrow inspector does a slider (60 wide at least) still reach
  under its number field, which the owner judged acceptable.
