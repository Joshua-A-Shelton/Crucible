# Mixing audio: deadlines, queues, and sample-exact timing

A learning note for M8 step 5 (ADR 0082).

## The deadline that never moves

A screen can show a frame late and the player sees a stutter. A speaker
can't wait: the sound card plays samples at a fixed rate (48,000 a second
here) and asks for the next block (a few milliseconds' worth) just before
it runs out. A block that isn't ready is a gap, heard as a click. So audio
code runs on its own high-priority thread and follows strict rules:

- **No locks.** If the game thread holds a lock the audio thread wants,
  the audio waits for the game, and a slow game frame becomes a click.
- **No allocations.** The allocator may take a lock, or ask the operating
  system for memory.
- **No file reads, no logging.** Both can block for a long time.

Everything the mixer needs (voices, buses, the reverb's delay lines) is
allocated once, when it starts.

## Talking to a thread you can't wait for

The game still needs to say "play this" and "fade that". It does it
through a **single-producer, single-consumer ring**: a fixed array plus
two counters. The game writes a command and then moves the `tail` counter.
The audio thread reads up to `tail` and then moves `head`. Each side only
writes its own counter, and the atomic store and load make sure a command
is fully written before the other side sees the counter move. No locks are
needed. When the ring is full, the command is dropped and counted, rather
than making anyone wait.

News goes the other way on a second ring: "voice 42 ended", "nothing
plays this sound any more, you can free it". Freeing memory the audio
thread might still be reading is the classic audio bug. Waiting for the
audio thread's "done with it" avoids it.

## What one block does

1. **Voices.** Each playing sound reads its samples. A sound at a
   different rate, or pitched, falls between samples, so the mixer
   **resamples**:
   - **Linear interpolation** (a straight line between two samples) dulls
     and aliases.
   - **Cubic Hermite** fits a smooth curve through four samples. It's
     cheap, and it keeps a 1 kHz sine within about -60 dB of the true
     curve, which the tests check.
2. **Gain.** Volume, distance attenuation and panning multiply each
   sample. Jumping to a new gain at the start of a block clicks ("zipper
   noise"), so gains slide from the last block's value to the new one
   across the block.
3. **Buses.** Sounds are added into their bus. Each bus filters, ducks and
   scales its sum, then adds it into the master. Buses are how a game says
   "all the dialogue" or "all the music" at once.
4. **The master** is clamped to the [-1, 1] range the device takes.

## Panning at constant power

To place a sound to one side, play it louder on that side. If the left
and right gains were `1 − p` and `p`, a sound in the middle would get 0.5
on each side, and it would sound quieter in the middle than at either
side. That's because loudness follows **power**, the sum of the squares:
0.5² + 0.5² is only 0.5. Using `cos θ` and `sin θ` of one angle keeps
`cos² + sin² = 1`, so the sound keeps the same loudness as it sweeps
across.

## Three small DSP pieces

- **One-pole low-pass:** `y += a · (x − y)`. Each output moves part of the
  way toward the input, so fast wiggles (high frequencies) get smoothed
  away. The share `a` comes from the cutoff frequency. It's one multiply
  and two adds per sample.
- **Ducking:** follow the loudness of one bus (rising fast, falling over
  0.3 s) and lower another bus by that much. That's how music dips under
  dialogue.
- **Freeverb:** a room is mostly echoes, many of them, quieter and duller
  each time.
  - **Comb filters** are delay lines fed back into themselves. Each one is
    a train of echoes, and its damping makes later echoes duller.
  - Eight combs of slightly different lengths are added together, then
    passed through **all-pass filters**. Those smear the echoes into a
    smooth wash without colouring the sound.
  - The right side's delays are a little longer than the left's, which
    gives the reverb its width.

## Sample-exact timing

A game frame lasts 7 to 33 ms, and a sound started "this frame" could be
heard anywhere in that window. Footsteps started that way sound uneven:
some land early, some late.

So sounds carry **timestamps** instead: step 4's footsteps say exactly
when the foot landed, on the input clock. The mixer keeps a mapping from
that clock to output samples. Each time the device asks for a block, it
notes which sample it's at and when. Callbacks arrive in bursts, so it
smooths the mapping rather than trusting each one.

A sound timestamped `t` starts at the sample for `t + delay`, even in the
middle of a block. The delay (50 ms) is the same for every sound, so
footsteps half a second apart are heard exactly half a second apart.
Nobody notices a constant 50 ms, but anyone notices uneven steps. If a
sound arrives too late for its sample anyway (a long hitch), it plays at
once and the mixer counts it, so a test or a profiler can tell.

The Metronome sample checks the whole chain. Clicks are scheduled from
frames through the real game loop and mixed offline, and every click lands
on exactly the frame it was scheduled for.
