#!/usr/bin/env python3
"""Makes M8's footstep (ADR 0084): Assets/Sounds/Footstep.wav in the Walker
project, a fifth of a second, mono, 48 kHz, 16-bit.

A footstep is a thump (a low tone dying away fast) and a scuff (noise,
low-passed, dying away faster), both starting at full level on the first
sample, so its onset is sharp and easy to find in a mix. The noise comes
from a fixed seed, so the file is the same every time.

Standard library only. Run it again after changing it, and commit the output.
"""

import math
import pathlib
import struct
import wave

HERE = pathlib.Path(__file__).resolve().parent
OUT = HERE / "Project" / "Assets" / "Sounds" / "Footstep.wav"

RATE = 48000
FRAMES = RATE // 5


def main():
    seed = 12345
    low = 0.0
    samples = []
    for i in range(FRAMES):
        t = i / RATE
        thump = math.sin(2 * math.pi * 70 * t) * math.exp(-t / 0.04)
        seed = (seed * 1103515245 + 12345) % 2**31
        noise = seed / 2**30 - 1
        low += 0.25 * (noise - low)  # one-pole low-pass, about 2 kHz
        scuff = low * math.exp(-t / 0.025)
        # The first sample is the loudest: the thump starts at its peak.
        value = 0.55 * math.cos(2 * math.pi * 70 * t) * math.exp(-t / 0.04) + 0.25 * thump + 0.6 * scuff
        samples.append(max(-1.0, min(1.0, value)))
    OUT.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(OUT), "wb") as file:
        file.setnchannels(1)
        file.setsampwidth(2)
        file.setframerate(RATE)
        file.writeframes(b"".join(struct.pack("<h", round(s * 32767)) for s in samples))
    print(f"Footstep: {FRAMES} frames, peak {max(abs(s) for s in samples):.2f}")


if __name__ == "__main__":
    main()
