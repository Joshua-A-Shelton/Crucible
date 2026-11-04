#pragma once

// Inside the platform layer: what Platform.cpp shares with its Wayland part
// (PlatformWayland.cpp, ADR 0066).

#include <crucible/Platform.h>

#include <SDL3/SDL.h>

namespace crucible::platform {

// Queues a drag event (DragMoved, DragDropped, DragCanceled) for the next
// pollEvents, in order with SDL's own events. Positions are pixels in the
// window (0 for none of ours).
void pushDragEvent(InputEventType type, SDL_WindowID window, float x, float y);

#if defined(CRUCIBLE_PLATFORM_WAYLAND)
namespace wayland {

// A connection of Crucible's own beside SDL's on SDL's display, for window
// drags SDL doesn't offer: it starts when SDL runs on Wayland, and does
// nothing otherwise (libwayland-client is loaded at run time).
void start();
void stop();
bool running();
WindowDragSupport dragSupport();
// From the source window's last press; `dragged` (or null) is attached so
// the compositor moves it, `offsetX`/`offsetY` (window coordinates) under the
// pointer. False if the drag couldn't start.
bool startDrag(SDL_Window* source, SDL_Window* dragged, float offsetX, float offsetY);
void stopDrag();
// Reads Crucible's queue after SDL has read the socket, turning drag events
// into pushDragEvent calls.
void dispatch();

} // namespace wayland
#endif

} // namespace crucible::platform
