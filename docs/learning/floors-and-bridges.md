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
  (the grass and the road), Under (characters on the ground under the
  deck), Deck (the bridge), Low (everything else on the ground), and High
  (characters on the bridge). On the bridge the hero is in High, over the
  deck; on the ground in Under or Low, as below.
- **Its physics layer.** The map's colliders are split by floor: the road's
  walls and the bank under the deck are on the Ground layer, the railings
  on the Bridge layer. The hero's body is on, and collides with, only its
  floor's.

Change one without the other and something breaks: drawn over the deck
but held by the road's walls, or walking through railings it's drawn
behind.

## Drawn under where it's under

Top-down, a sprite stands up from its feet, so what it overlaps depends on
where it is, not only on its floor. The first try drew everyone on the
ground under the deck and its railings: climbing the stairs, the hero's
head went behind the north railing, which is behind it. Drawing the ground
over the deck fixes that, but then a hero walking up the road toward the
bridge has its head over the deck's edge until its feet are under it,
where under a raised deck the head goes under first.

So the sample keeps a box (a Cover) over the road under the deck,
reaching a sprite's height south of it: with its feet inside, a hero on
the ground is drawn in a layer under the deck; outside, in one over it,
y-sorted with the lamps. Walking north up the road, its head goes under
the deck first and it reappears whole once its feet leave the far side;
on the stairs beside the road it stays over the railing behind it.

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
