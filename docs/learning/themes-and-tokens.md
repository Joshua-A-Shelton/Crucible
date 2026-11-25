# Themes, tokens, and selectors

How a theme says what things look like, and how the editor's look (ADR
0129) is built from one. The UI library's styling is ADR 0061; this is
what tokens and ancestor selectors add.

## A theme is a list of rules

Each rule is a **selector** and the values it sets. An element takes,
for each property, the value of the most specific rule matching it (the
later one between equals):

```json
{ "Selector": "Button", "Set": { "Background": "#2a2a2a" } },
{ "Selector": "Button:hover", "Set": { "Background": "#303030" } }
```

`Button:hover` names a type and a state, so it's more specific than
`Button` and wins while the pointer is over a button.

## Selectors that look at ancestors

A selector can have several parts. The last is the element being
styled; the ones before are its ancestors:

- `Button:hover Icon`: an icon anywhere inside a hovered button.
- `DockTab.active > Label`: a label directly inside the active tab.

Without them, an icon inside a button can't change when the button is
hovered: the icon isn't hovered, the button is. With them, the rule
lives where the look is decided, not in code that copies states down.

The cost: when an element's state changes, the elements inside it may
need restyling. The theme knows which types its ancestor parts name, so
only those elements' insides are restyled, not every hovered element's.

## Tokens: naming values once

The same gray or orange appears in dozens of rules. A token names it:

```json
"Tokens": { "accent": "#ff7418", "selection": "@accent/35%", "radius": 5 },
"Styles": [
  { "Selector": "Button.primary", "Set": { "Background": "@accent", "CornerRadii": "@radius" } },
  { "Selector": "Splitter:hover", "Set": { "Background": "@accent/60%" } }
]
```

- `@name` uses a token's value; `@name/60%` is a color at 60% opacity.
- Tokens can name tokens, so a palette can be built from a few colors.
- A token has no type of its own: `5` is a corner radius where a style
  uses it as one.
- Mistakes are found when the file is read and say where: an unknown
  token, tokens naming each other in a circle, an opacity on something
  that isn't a color.

Code that draws for itself (a grid, a selection rectangle) asks the
theme: `theme.Color("accent")`. Then a color changed in the file changes
the drawing too.

## The editor's theme

`Editor.crstyle` sits beside the editor. It's layered over the engine's
default theme (the controls' plain look), restyling every control in the
editor's colors and density, then the editor's own parts.

The editor watches the file: save it and the editor restyles a moment
later. A mistake keeps the old look and puts the reason in the Log, so a
half-finished edit never breaks the editor.

Extensions read the same tokens (`editor.Theme.Color("accent")`) so
their panels match.

## Try it

1. Open `Editor.crstyle` beside the built editor
   (`build/debug/artifacts/bin/Crucible.Editor/debug/`, next to `Fonts/`).
2. Change `"accent"` and save: the selection, toggles, and tool buttons
   change color.
3. Misspell a token and save: the Log says which style and which token.
