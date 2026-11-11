# Audio codecs: what a sound costs, and when to pay

A learning note for M8 step 6 (ADR 0083).

## The size of sound

One second of CD-quality stereo is 44,100 frames × 2 channels × 2 bytes,
about 172 KB. A game has thousands of effects and an hour of music, so how
sound is stored matters. Each codec trades memory against the work of
turning it back into samples, and that work happens on the audio thread
(which has a hard deadline) or somewhere ahead of it.

| Encoding | Size | Decoding | Good for |
|---|---|---|---|
| PCM | 1× | none | short, frequent effects: footsteps, UI |
| IMA ADPCM | ¼ | a few adds a sample, on the audio thread | longer effects, ambience |
| Vorbis | about 1/10 | real work: decoded ahead, on background jobs | music, long ambience |

## PCM

Pulse-code modulation stores every sample as a number, 16 bits here. It
starts instantly and costs nothing to play, and any sample can be read at
any time, which matters for resampling.

## IMA ADPCM: storing the change, not the value

Neighbouring samples are close, so storing the **difference** from the last
sample takes fewer bits than storing the sample. ADPCM (*adaptive
differential* PCM) goes further:

- Each sample is a 4-bit step: a sign and a size of 0 to 7 **step units**.
- The step unit **adapts**. A big step means the signal is moving fast, so
  the unit grows. A small step shrinks it. 89 step sizes cover quiet hiss
  to full-scale square waves.
- The **encoder runs the decoder** alongside itself. It picks each nibble
  by what the decoder will reconstruct, not by the true signal, so errors
  never pile up.

That gives a quarter of PCM's size at about 30 to 40 dB signal to noise.
That's audible on a pure tone in a quiet room, but not in a footstep or an
explosion.

Samples are stored in **blocks**. Each block begins with an exact sample
and a step index, so decoding can start at any block. That's how a voice
starts mid-sound, or loops, without decoding from the beginning.

## Vorbis: compressing what the ear ignores

Vorbis is **perceptual**: it throws away what hearing won't miss.

1. It turns short overlapping windows of sound into frequencies (a modified
   discrete cosine transform).
2. It works out which frequencies are hidden behind louder neighbours
   (masking).
3. It spends bits only where the ear will notice.

That gets about ten times smaller than PCM at near-transparent quality.
The cost is decoding: inverse transforms, overlapping windows, and
entropy-coded data. That's too much to risk on the audio thread for every
voice.

## Streaming ahead

So a Vorbis voice is decoded ahead of time, into a **ring buffer**:

- **The writer:** a background job decodes until the ring is full, about a
  second ahead.
- **The reader:** the audio thread reads frames out of the ring as the voice
  plays, and marks the ones it's done with.
- **The counts:** each side moves only its own count (frames written,
  frames released), so they never wait for each other. It's the same
  single-producer, single-consumer idea as the mixer's command queue.
- **Topping up:** the main thread checks each frame and starts a new job
  when a ring falls below half.
- **Starving:** if a job ever falls a full second behind (a long hitch),
  the voice plays silence and the mixer counts it. Counting it is better
  than waiting, because waiting is never allowed on the audio thread.

## What import can change

The importer can process a sound before encoding it:
- **Mono:** spatial sounds are panned as mono anyway, so a stereo source
  only doubles their memory.
- **Trim silence:** a footstep with 100 ms of silence at its start is
  scheduled exactly but heard 100 ms late.
- **Normalize:** brings sources recorded at different levels to a common
  peak.

None of this is possible for Vorbis that's kept as it is. Changing samples
means re-encoding, and re-encoding a lossy format loses quality twice. The
importer says so rather than quietly ignoring the setting.
