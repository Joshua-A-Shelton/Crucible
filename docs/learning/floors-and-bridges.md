# Floors and bridges

A learning note from ADR 0114: how a top-down 2D game lets a character walk
both under a bridge and over it.

## The problem

Top-down, the bridge and the road under it are drawn in the same place on
the screen. Someone on the road should be drawn under the deck and walk
through the space under it; someone on the bridge should be drawn over the
deck and be stopped by its railings, not by the road's walls far below.
Neither drawing order nor collision alone can tell the two apart: it's the
character's *floor* that decides both.

## Two layers that switch together

The Crossing sample gives the hero a floor, kept in two settings that
always change together:

- **Its sorting layer.** The sorting layers are, in drawing order, Ground
  (the grass and the road), Low (characters on the ground), Deck (the
  bridge), and High (characters on the bridge). On the ground the hero is
  in Low, under the deck; on the bridge in High, over it.
- **Its physics layer.** The map's colliders are split by floor: the road's
  walls and the bank under the deck are on the Ground layer, the railings
  on the Bridge layer. The hero's body is on, and collides with, only its
  floor's.

Change one without the other and something breaks: drawn over the deck
but held by the road's walls, or walking through railings it's drawn
behind.

## Where the floor changes

Only on the stairs, where the two floors meet. A trigger at the top of
each staircase puts the hero on the bridge; one at the bottom puts it on
the ground. Between them it keeps whatever it had, so walking halfway up
and back down changes nothing. The top trigger sits just inside the bank's
edge: a hero coming up from the ground reaches it before the bank (solid
on the ground floor) stops it, and from then on the bank isn't in its way.

## Masks both ways

Crucible's 2D filter lets two shapes collide if *either* one's mask names
the other's layer. A static collider with a mask of every layer collides
with everything, whatever the character's own mask says. So each floor's
colliders name only their own layer in their mask too.
