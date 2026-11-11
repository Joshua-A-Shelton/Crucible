#!/bin/sh
# The Sound importer's test files (ADR 0083): a 440 Hz tone, half a second,
# with a quarter second of silence before and after. Made with ffmpeg (and
# its libvorbis encoder); run it again after changing it, and commit the
# output.
#
#   tone.wav   mono, 48 kHz, 16-bit
#   tone.flac  stereo (the right side at half the left's level), 44.1 kHz
#   tone.ogg   mono, 48 kHz, Vorbis quality 6
set -e
cd "$(dirname "$0")"
tone="sine=frequency=440:sample_rate=48000:duration=0.5"
shape="volume=4,adelay=250,apad=pad_dur=0.25"
ffmpeg -y -v error -f lavfi -i "$tone" -af "$shape" -c:a pcm_s16le tone.wav
ffmpeg -y -v error -f lavfi -i "$tone" -af "$shape,pan=stereo|c0=c0|c1=0.5*c0,aresample=44100" -c:a flac tone.flac
ffmpeg -y -v error -f lavfi -i "$tone" -af "$shape" -c:a libvorbis -q:a 6 tone.ogg
