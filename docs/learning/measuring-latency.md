# Measuring input latency

Crucible's goal 4 is that what's on screen matches the game state as
closely as possible (ADR 0003). This note explains what the latency harness
measures, what it can't, and how to read its numbers.

## The path of one input

```
key pressed ─► OS input stack ─► SDL queue ─► Input phase reads it
   ─► the frame is recorded and submitted ─► GPU renders it
   ─► present ─► compositor ─► display scans out ─► pixels change
```

Each arrow costs time. Software inside the engine can only timestamp the
middle of that path, from "SDL has the event" to "the present completed":

| Stage | What it is | How it's stamped |
|---|---|---|
| arrival | SDL put the event in its queue | SDL's event timestamp |
| read | the Input phase polled it | `TimestampNow` in the Input phase |
| submitted | `ClearAndPresent` returned: submitted and presented | `TimestampNow` after the call |
| gpu_done | the GPU finished the frame | a native thread waiting on the frame semaphore |
| present_done | the present completed | the *next* frame's pacing wait returning |

All of them use one clock, SDL's nanosecond counter, so they can be
subtracted directly.

**present_done needs explaining.** With max frame latency 1,
`waitForNextFrame` before frame N+1 waits until frame N's present has
completed (Vulkan `present_wait`, D3D12's frame latency waitable). So the
moment it returns is when frame N was presented. That only works when
pacing is on and the device has Slag's frame latency capability; otherwise
the column is empty.

**What software can't see:** the OS input stack before SDL (USB polling,
the kernel, the window system), the compositor's own delay, and the
display's scanout and pixel response. Measuring those needs either a
present-timing API (when a present actually reached the screen, which Slag
doesn't have yet) or hardware: a photodiode on the screen and a button
wired to a logger, as NVIDIA's LDAT does.

## Synthetic input

A real person pressing keys makes every run different. Instead, a native
thread pushes marker events into SDL's queue at random intervals (23 ms on
average, not a multiple of common refresh rates), so inputs land at every
point of a frame, exactly as real ones do. Real key and mouse presses
during a run are recorded too.

Because an input can arrive at any moment while the frame is waiting, the
"read" stage averages about half a frame: an input that arrives just after
the Input phase waits a whole frame, one that arrives just before waits
almost nothing.

## Running it

```
crucible_launcher_aot --frames 600 --latency run.csv
crucible_launcher_aot --frames 600 --present-mode fifo --latency fifo.csv
crucible_launcher_aot --frames 60000 --no-pacing --latency unpaced.csv
```

`run.csv` has one row per input, with each stage's latency in ms;
`run.frames.csv` has one row per frame, with each stage measured from the
frame's start. The summary printed at the end gives the median, the 95th
percentile (p95: 95% of inputs were faster), and the maximum per stage.

## First results (2026-10-26)

RADV on KDE Wayland, a 144 Hz monitor (6.94 ms per refresh), a frame that
only clears the screen:

| Setup | read | gpu_done | present_done | frame interval |
|---|---|---|---|---|
| Mailbox, paced | 3.9 | 4.1 | 10.8 (p95 13.6) | 6.95 |
| Fifo, paced | 3.5 | 3.8 | 10.5 (p95 13.7) | 6.95 |
| Mailbox, unpaced | 0.05 | 0.19 | not known | 0.10 |

How to read it:

- **Paced, the present completes about one refresh after the input is
  read.** That's the cost of waiting for the display: render just in time,
  then wait for the next vertical blank.
- **Paced Fifo and paced Mailbox are the same.** RADV gave Fifo four images
  when asked for two, but with max frame latency 1 the extra images never
  fill up, so they add no queue.
- **Unpaced Mailbox reads input almost immediately,** because it renders
  about ten thousand frames per second, but nothing tells us when its frames
  reached the screen. The display still only changes at a vertical blank, so
  the true gap to paced is less than the read times suggest, and it costs a
  GPU running flat out. Settling it needs present timing or a photodiode.

### On Windows (RTX 2070 SUPER, 60 Hz monitor, Mailbox, paced)

| Backend | read | gpu_done | present_done | frame interval (median, p95) |
|---|---|---|---|---|
| Vulkan | 8.1 | 8.8 | 24.8 | 16.7, 18.1 |
| D3D12 | 8.0 | 8.7 | 8.8 | 0.8, 16.2 |

Vulkan behaves as on Linux: one frame per refresh, the present completing
about a refresh after the input is read. D3D12 doesn't: DXGI's frame
latency waitable, with Mailbox-style presents, lets frames through in
bursts and only holds back once per refresh, so "present done" there is
when DXGI would take another frame, not when one reached the screen. The
present column is only trustworthy on Vulkan until Slag has present
timing.

## How accurate are input timestamps?

The harness measures from "SDL has the event", and its synthetic markers
carry SDL's own timestamp, so they can't say how well SDL's timestamps match
when real input arrived. A second check does: it injects real OS input at a
known moment (`/dev/uinput` on Linux, `SendInput` on Windows), which crosses
the whole input stack, and compares the timestamp SDL reports. It polls
8 ms late, as a game polls once a frame, so a timestamp of when SDL
*processed* the event shows up as about 8 ms, while one of when it
*arrived* stays near 0.

Results are in ADR 0018's amendment. The short version: Wayland and
Windows' raw input give arrival times accurate to well under a
millisecond (Wayland keyboards to the millisecond); Windows' window
messages are only accurate to about +-7 ms, so Crucible turns on SDL's raw
keyboard there; X11 gives processing times, so Crucible flags them.

Run it by hand on a desktop (it may need a click to get focus):

```
crucible_native_tests --no-skip --test-suite=input-timing
```
