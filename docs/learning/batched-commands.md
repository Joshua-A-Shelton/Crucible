# Batched commands: C# render steps

A learning note for M6 step 2 (ADR 0058).

## Why not call Slag from C#?

Every call from C# into native code costs something (tens of
nanoseconds), and so does the reverse. A UI frame can have hundreds of
draws, each with a pipeline, constants and a scissor: per-call costs add
up, and worse, recording runs on job threads, where managed code would
need the runtime's attention (garbage collection, thread setup) in the
middle of native work.

A command buffer of our own avoids both. C# writes each command as a few
bytes of plain data into native memory, with no call at all; the step
crosses into native code a handful of times per pass (adding it,
declaring what it uses). Later, on a job thread, native code reads the
bytes back and makes the real Slag calls. This is the same idea as a GPU
command buffer one level up: record now, execute later.

## The shape of a record

Each record is a small header (what it is, how long it is) and then its
arguments, all 4-byte aligned:

```
| type | words | arguments ...     | extra bytes (push constants, label text) |
```

The length lets a reader step over records it doesn't care about, and
lets the checker refuse one that runs past the end. The argument structs
are declared once, in C++, and the binding generator mirrors them in C#
and checks their layouts at start-up, so the two sides can't disagree
about where a field is.

## Check once, play back fast

C# code is where mistakes happen (a draw before setting a pipeline, a
compute pipeline in a graphics pass), and mistakes in GPU commands crash
drivers instead of throwing. So the records are checked as soon as the
step returns, on the main thread, where a mistake can be reported with
the step's and pass's names and fail the frame cleanly. Playback then
needs no checks: it's a loop over a switch. One walk over a few hundred
small records is cheap next to drawing them.

## Write-combined memory

Upload memory, which the GPU reads straight from system memory, is
usually *write-combined*: the CPU doesn't cache it, and gathers writes
into whole lines before sending them. Writing it in order, all of it, is
fast; reading it, or writing scattered bytes, is very slow. C# filling an
array of structs field by field is exactly the slow pattern. So frame
data is collected in ordinary memory while steps run, and copied into the
upload buffer with one sequential copy before the frame is recorded.
That also keeps the spans C# was given valid while the data grows: the
chunks they point into never move.

## Calling C# from native code without holding C# objects

Native code calls C# steps through one function pointer (an
`[UnmanagedCallersOnly]` method) with a step's ID, never a pointer to a
C# object. The engine's C# keeps the table from IDs to step objects.
That way a game can be unloaded (hot reload): its steps are dropped from
the table and the schedule, and nothing native keeps the game's types
alive.
