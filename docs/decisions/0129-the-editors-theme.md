# 0129 — The editor's theme

**Status:** Accepted (2026-11-25); step 1 of the editor's look
(milestones). Names are placeholders until the owner settles them
(docs/naming.md).

## Context

After the engine review, the owner asked for a pass over the editor
focused on making it look nicer, with an identity of its own. The editor
was styled in C# (`Shell.CreateTheme`), over the engine's default theme:
flat dark grays with a blue accent, 16px text, components as rounded
cards. Colors were repeated per style, and custom drawing (the Sprite
Sheet Editor, map mode, chain handles) kept its own copies. The UI
library's selectors matched one element only, so an icon couldn't follow
its button's hover and disabled buttons dimmed by opacity (review page 5,
question 1).

## Options

**Direction:** quiet and dense; a familiar game editor; an identity of
its own. **Palette:** Forge (warm charcoal, molten orange); ember on
slate; brass on graphite; Forge with warm, neutral, or cool grays.
**Density:** 14px; 13px; 16px as it was. **Structure:** dividers; cards.
**Corners:** 2 to 3px; 4 to 6px. **The theme's form:** dark in a file;
dark and light in files; dark in code. **Selectors:** descendant and
child parts; descendant only. **Tokens:** any value with opacities;
colors only. **A mistake in the file:** keep the last good theme; fall
back to the default.

## Decision

Decided by the owner (2026-11-25):

1. **Forge on neutral charcoal:** panels #1e1e1e, an orange accent
   (#ff7418, more saturated than the first mockup, to be tuned by hand).
   Noto Sans kept. **Compact:** 13px text. **Hairline dividers** between
   sections, not cards. **Medium corners** (5px; 3px on small parts).
2. **Ancestor parts in selectors, in the UI library** (games get them
   too): `Button:hover Icon` (anywhere inside), `DockTab.active > Label`
   (directly inside). A `Selector` has `Parts` (`SelectorPart`: type,
   classes, states, and a `Combinator`, `Descendant` or `Child`, saying
   how the next part sits in it); specificity adds up over the parts.
3. **Tokens in .crstyle, in the UI library:** a `"Tokens"` table of named
   values, any kind, written `"@accent"` in a style, or `"@accent/55%"`
   for a color at an opacity; a token may name another. Each is read as
   the property using it needs. `Theme.Tokens`, `SetToken`,
   `TryGetToken`, and `Color(name)` for custom drawing.
4. **The editor's theme is a file:** `Editor.crstyle` beside the editor,
   layered over the engine's default theme, read at start and again when
   it changes (`EditorTheme`). A mistake leaves the look as it was and
   says where in the Log. Extensions read it as `editor.Theme`.

## How it works

- **Matching:** the last part is the element styled; for the parts
  before, its ancestors are searched outward, a `Child` part only at the
  parent, a `Descendant` part anywhere above (trying each until one
  leads to a full match).
- **Restyling what's inside:** when an element's classes or states
  change, its subtree restyles too, but only if some style's ancestor
  part names its type (`Theme.MayStyleInside`); hovering ordinary
  elements restyles only them.
- **Resolving tokens:** a style's value naming a token is kept as a
  reference and read when an element is styled, cached per token and
  property, so `SetToken` changes every style using it. Parsing reads
  every reference once, so an unknown token, a cycle, a value the
  property can't read, or an opacity on something that isn't a color is
  reported with where it is: "Styles[12] (Button).Set.Background: no
  token @acent". `SetToken` checks the same and changes nothing if a
  style can't use the value.
- **The file:** copied beside the editor by its build. The editor watches
  it and reads it once a change has been quiet for 0.2 s (editors save in
  several writes). The theme object every window uses is changed in
  place (`ReplaceWith`, which now carries tokens), so every window
  restyles.
- **Custom drawing** reads tokens as it draws (`Shell.ThemeColor`, or
  `editor.Theme.Color` in a plugin): the Sprite Sheet Editor's backdrop
  and selection, the Palette's, map mode's brush, a selected chain point,
  the Game panel's focus. Gizmo axes and debug lines keep their colors.
- **Dividers:** the Inspector's sections sit one unit apart over the
  divider color, so hairlines show between them without per-side
  borders.

**Added while building:** tokens `control`, `pressed`, `edge`, and
`small-radius` beyond the drafted list (the default controls needed a
raised fill, a pressed fill, an outline, and a smaller corner). The editor
check runs up to 5000 frames (was 600): waiting for the file watcher
takes several hundred unpaced frames.

## Consequences

- Restyling the editor, or tuning a color, is an edit to one file while
  the editor runs; while the editor is built, the copy beside it is the
  one read (copy changes back to `src/managed/Crucible.Editor/`).
- Games' themes gain tokens and ancestor selectors with no change to
  existing files.
- Plugins drawing their own controls can match the editor by token name;
  the names are placeholders until settled.
- Not yet: a user's own theme file in the preferences folder; a light
  theme.
