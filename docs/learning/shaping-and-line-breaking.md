# Shaping and line breaking

A learning note for M6 step 1 (ADR 0057).

## Characters aren't glyphs

A font draws **glyphs**, not characters. **Shaping** turns a string into
glyphs and positions:
- `f` then `i` may become one `fi` ligature glyph;
- `A` then `V` move closer together (kerning);
- an accent may be its own glyph, placed over the letter before it.

HarfBuzz does this from the font's own tables, the same way for every
script. For Latin text it mostly picks one glyph per character and
kerns, but doing it properly from the start means non-Latin text later
needs no new path.

Each glyph remembers its **cluster**: the byte offset of the characters
it came from. A ligature covers two characters with one cluster, which is
why a caret can only sit at the start of a ligature, not inside it.

## Font units, then pixels

Shaping runs in **font units** (Noto Sans has 1,000 per em), so the result
is the same at every size: a 16-pixel line is exactly half a 32-pixel
one, before rounding. Pixels come last:
- exact glyphs round every origin and baseline to a whole physical pixel,
  so the rasterized bitmap lands as FreeType drew it;
- distance fields keep fractional positions, since they resample anyway.

## Where a line may break

Unicode's line-breaking rules (UAX #14) cover every script. Latin text
needs a small part of them:
- **after spaces,** which stay at the end of the line and don't count
  toward its width;
- **after a hyphen between letters** ("well-" / "known"), but not in
  "-5";
- **never at a no-break space,** which is how "10 km" stays together;
- **always at a line break** (`\n`).

**Greedy** breaking takes as much as fits, then cuts at the last
opportunity. A word wider than the line alone is cut between clusters, so
every line holds at least one. Lines are cut from one shaped paragraph,
so a pair kerned across a break loses its kerning: invisible in Latin
text, but one reason full support for other scripts reshapes each line.
