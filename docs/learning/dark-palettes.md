# Dark palettes

What made the editor's first dark palette hard to read, and how the tuned
one (ADR 0133) fixes it. The theme mechanics are in
[themes and tokens](themes-and-tokens.md).

## Surfaces need steps you can see

A dark interface is layers: the page under everything, the strips holding
tabs and toolbars, the panels, and the headers inside them. Each layer
needs a step of lightness from its neighbours. Near black, the eye tells
apart smaller steps than it does near white, but not steps of 3 or 4
levels out of 255. The first palette had the page, strips, and panels
within 10 levels of each other, so everything blurred.

The tuned palette spaces them out, darkest to lightest:

| Surface | Color |
|---|---|
| Page | #121212 |
| Strip | #171717 |
| Panel | #1f1f1f |
| Header | #262626 |
| Divider | #353535 |

A divider has to be lighter than both surfaces it separates, or it's
invisible.

## Tints of orange go brown

On a dark background, a tint of a warm accent goes brown. Orange at 30%
over charcoal mixes to a rust-brown, which is where the first selection
color came from. Saturated, light hues stay clean as small marks and turn
muddy as large fills.

So keep the accent for small things that must stand out (an edge, an
icon, a focus ring), and make large fills neutral. The selected row is a
lighter grey, and a 2px orange bar on its left edge says "selected".
Scanning a long tree, the eye finds the one orange mark at once.

## Color where it means something

More of the accent can still read well if it's on the frame, not on the
content. The title bar's top glows orange and fades out, and so do active
tabs, so the color is around the work rather than over it. And while the
game is playing, the gaps between panels and the status bar turn orange:
there, the color is a warning that edits made now will be lost.

A **gradient** in the theme is two colors and a direction:
`"GradientFrom": "@accent/55%"` with no `GradientTo` fades from the accent
into whatever is behind it, and `"GradientLength": 0.7` finishes the fade
70% of the way along: a glow that settles before the edge looks lit,
where one fading the whole way looks like a stain. The fade is computed
in linear light, and a
clear end takes the other end's color at no opacity. Fading orange to
"clear black" would otherwise pass through a dirty brown.

## Inputs should look like inputs

A field darker than its panel, with no edge, reads as a hole. An outline
a step lighter than the panel marks its bounds. Text fields in most dark
editors are slightly sunk *and* outlined. Focus then changes only the
outline's color, to the accent, so nothing jumps.

## Borders on some sides

CSS has had borders per side for decades. A border width becomes four
numbers (left, top, right, bottom), and one number means all four. Here a
uniform border is stroked around the rounded rectangle. Uneven sides are
drawn as straight strips, which is right for a bar or an underline. Few
designs want a rounded corner on a bar anyway.

## Try it

1. Open `Editor.crstyle` beside the built editor, with the editor running.
2. Change `"selection-edge"` to `"#5aa9ff"` and save: selected rows and
   tiles get a blue edge.
3. Change `"panel"` to `"#1a1a1a"` and save: the headers and dividers stand
   out more and the panels recede. Then put it back.
4. Change `"title-glow"` to `"@accent/90%"`, or add `"GradientTo": "@accent/20%"`
   to the TitleBar style, and save to see the glow change.
