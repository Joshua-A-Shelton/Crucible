# Engine messages: from a native warning to the editor's Log

A learning note for editor usability step 1 (ADR 0087).

## Why not just print?

`fprintf(stderr, ...)` is the simplest way for native code to complain, and
for a long time it was how Crucible's core did. The trouble shows up as
soon as someone uses the editor instead of a terminal: the warning that
would have explained everything ("this collider's model has no cooked hull
data") is printed somewhere they never look.

So the core has one place every message goes through. It still prints (a
terminal and CI logs are still useful), but it also keeps the message for
whoever wants it: the editor, or a native tool with its own sink.

## A ring, not a list

Messages can come from any thread (a job, the audio callback's neighbours,
Box3D's tasks), and from places where allocating is a bad idea, like an
allocation that just failed. So the core keeps them in a fixed **ring
buffer**: 256 slots, each with room for 1023 bytes of text, made once.
Writing a message copies it into the next slot; when the ring is full the
oldest message is overwritten. Nothing allocates, ever.

A mutex guards the ring, but it's held only while one message is copied in
or out. Messages are rare, so the lock is never contended in practice, and
none of this is on a hot path. The sink, if any, is called **after** the
lock is released, so a sink that is slow (or writes a message itself)
can't stall or deadlock other threads.

## Crossing into C#

C# doesn't get a callback from native code here: that would mean C# code
running on whatever thread wrote the message, in the middle of a job.
Instead the editor **pulls**: once a frame, on the main thread, it takes
everything since the last frame and raises an event for each message. A
message written during a frame shows up in the Log at the start of the next
one, which is soon enough for a person reading it.

Only one part of the program should pull, because taking messages removes
them. In the editor, that's the editor itself.

## Import settings: holding edits until Apply

A component's field changes the scene immediately, with undo. An import
setting is different: changing it means importing the asset again, which
can take a while for a big model, and doing that on every keystroke or
slider drag would be miserable. So import settings follow the pattern most
engines use: edits are **held** in the inspector, marked as changed, until
Apply saves the sidecar and reimports once (or Revert throws them away).

The same field editors serve both. They only need something that owns the
values: one owner writes to a component with undo, the other writes to a
held copy of the settings.
