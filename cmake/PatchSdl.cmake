# Patches SDL's Wayland backend (ADR 0066), run once after it's fetched:
# SDL's data device declines every drag that isn't files or text, and the
# compositor takes the last answer from any of an app's data devices, so it
# would undo the platform layer's acceptance of its own window drags. Drags
# of Crucible's data type are left to the platform (PlatformWayland.cpp).
# Recheck on every SDL update (docs/open-items.md).
#
#   cmake -DFILE=<SDL>/src/video/wayland/SDL_waylandevents.c -P PatchSdl.cmake

file(READ "${FILE}" source)
string(FIND "${source}" "application/x-crucible-window-drag" patched)
if(patched GREATER -1)
    return()
endif()
set(anchor "    if (data_device->drag_offer && window && window->accepts_drag_and_drop) {")
string(FIND "${source}" "${anchor}" at)
if(at EQUAL -1)
    message(FATAL_ERROR "PatchSdl.cmake: SDL's data_device_handle_enter changed; update the patch")
endif()
string(REPLACE "${anchor}" "    // Crucible: window drags are the platform layer's own (cmake/PatchSdl.cmake).
    if (data_device->drag_offer && Wayland_data_offer_has_mime(data_device->drag_offer, \"application/x-crucible-window-drag\")) {
        data_device->dnd_window = NULL;
        return;
    }

${anchor}" source "${source}")
file(WRITE "${FILE}" "${source}")
