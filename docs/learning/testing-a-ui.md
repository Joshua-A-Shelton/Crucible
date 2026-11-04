# Testing a UI headless

A learning note for M6's demonstration (ADR 0064).

## Scripts instead of hands

A menu is usually checked by someone clicking through it. To check it on
every build, the showcase replays what a person would do as synthetic
input events (pointer moves and presses in physical pixels, key presses,
gamepad buttons and stick tilts) fed to the UI root, one step a frame.
After each step it asserts what should be true: which element has focus,
what the slider's value is, which screen is up, whether the dialog is
open. The same journey runs once per device, so each input path is
proven to reach the same places.

## What "crisp" means, testably

Crisp text means text that doesn't blur when it lands between pixels.
One way to test that: draw the same text several times, each moved by a
fraction of a pixel, and require every copy to come out identical, just
shifted by whole pixels. If the renderer blurred text at fractional
positions, the copies would differ.

## Make sure a test can fail

The first version of that check passed, but so did a deliberately broken
renderer, one that drew text a third of a pixel off and blurred it. The
probes had been placed by layout, which already rounds every element's
position to whole pixels, so all three copies were blurred the same way
and still matched. Moving them by a render offset, which layout doesn't
round, made the broken renderer fail at every scale, and the real one
still pass.

The lesson is general: a check that has never failed may not be checking
anything. Breaking the code on purpose, once, and watching the check
catch it, is cheap insurance.

## Captures find what assertions don't

The scripted journeys all passed while the options screen looked wrong:
the controls' text overflowed them, because a grid row didn't grow to fit
a control in its other column. No assertion asked about it; looking at the
captured image showed it at once. Saving images of each screen, at each
scale, and looking at them, belongs in the loop.
