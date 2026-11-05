// Window drags on Wayland (ADR 0066). SDL doesn't start drags or say which
// press a drag starts from, so the platform keeps a small connection of its
// own on SDL's display, with its own event queue: a pointer for press
// serials, a data device for the drags arriving over our windows, and
// xdg-toplevel-drag, which has the compositor move a window with the drag
// (a torn-off panel following the pointer). SDL is patched to leave drags
// of Crucible's data type alone (cmake/PatchSdl.cmake); without that, its
// own data device would decline them.
//
// libwayland-client is looked up at run time, as SDL does, so the library
// starts without it: each function this file calls goes through a pointer
// of the same name with a CRUCIBLE_ prefix. The protocols' interface
// descriptions are our own copies (cmake/WaylandProtocol.cmake), plain data.
//
// Protocol errors end the whole connection, SDL's too, so requests follow
// the protocols' rules carefully: a toplevel drag is destroyed only after
// its drag ended, and a drag in progress is never cancelled by us.

#define wl_proxy_marshal_flags (*CRUCIBLE_wl_proxy_marshal_flags)
#define wl_proxy_add_listener (*CRUCIBLE_wl_proxy_add_listener)
#define wl_proxy_destroy (*CRUCIBLE_wl_proxy_destroy)
#define wl_proxy_get_version (*CRUCIBLE_wl_proxy_get_version)
#define wl_proxy_get_user_data (*CRUCIBLE_wl_proxy_get_user_data)
#define wl_proxy_set_user_data (*CRUCIBLE_wl_proxy_set_user_data)
#define wl_proxy_create_wrapper (*CRUCIBLE_wl_proxy_create_wrapper)
#define wl_proxy_wrapper_destroy (*CRUCIBLE_wl_proxy_wrapper_destroy)
#define wl_proxy_set_queue (*CRUCIBLE_wl_proxy_set_queue)
#define wl_display_create_queue (*CRUCIBLE_wl_display_create_queue)
#define wl_event_queue_destroy (*CRUCIBLE_wl_event_queue_destroy)
#define wl_display_dispatch_queue_pending (*CRUCIBLE_wl_display_dispatch_queue_pending)
#define wl_display_roundtrip_queue (*CRUCIBLE_wl_display_roundtrip_queue)
#define wl_display_flush (*CRUCIBLE_wl_display_flush)

#include <wayland-client-core.h>

#include "wayland-protocol.h"
#include "xdg-shell-protocol.h"
#include "xdg-toplevel-drag-v1-protocol.h"

// The interface descriptions, defined here (one translation unit).
#include "wayland-protocol.c"
#include "xdg-shell-protocol.c"
#include "xdg-toplevel-drag-v1-protocol.c"

#include "PlatformInternal.h"

#include <dlfcn.h>
#include <unistd.h>

#include <algorithm>
#include <cstdio>
#include <cstdlib>
#include <cstring>

namespace crucible::platform::wayland {

namespace {

// The data type of Crucible's window drags: nothing is transferred, the
// process already knows what's dragged.
constexpr const char* dragMimeType = "application/x-crucible-window-drag";

struct State
{
    void* library = nullptr;
    wl_display* display = nullptr; // SDL's
    wl_event_queue* queue = nullptr;
    wl_display* wrapper = nullptr; // the display, on our queue
    wl_registry* registry = nullptr;
    wl_seat* seat = nullptr;
    wl_pointer* pointer = nullptr;
    wl_data_device_manager* manager = nullptr;
    wl_data_device* device = nullptr;
    xdg_toplevel_drag_manager_v1* dragManager = nullptr;

    // The pointer's surface, and the last press on it.
    wl_surface* pointerSurface = nullptr;
    wl_surface* pressSurface = nullptr;
    uint32_t pressSerial = 0;

    // Our drag, from start until the compositor says it ended.
    wl_data_source* source = nullptr;
    xdg_toplevel_drag_v1* toplevelDrag = nullptr;
    bool dropped = false; // on one of our windows

    // A drag over our windows: its offer, the window, and the last position (pixels).
    wl_data_offer* offer = nullptr;
    SDL_WindowID target = 0;
    float x = 0.0f;
    float y = 0.0f;
};

State state;

// CRUCIBLE_DEBUG_DRAGS=1 prints the drag's events, for finding out what a
// compositor sends.
bool debugging()
{
    static const bool on = [] {
        const char* value = std::getenv("CRUCIBLE_DEBUG_DRAGS");
        return value != nullptr && value[0] == '1';
    }();
    return on;
}

#define CRUCIBLE_DRAG_DEBUG(...)                                                                                       \
    do                                                                                                                 \
    {                                                                                                                  \
        if (debugging())                                                                                               \
        {                                                                                                              \
            std::fprintf(stderr, "crucible drags: " __VA_ARGS__);                                                      \
            std::fputc('\n', stderr);                                                                                  \
        }                                                                                                              \
    } while (false)

template <typename Function>
bool load(Function& function, const char* name)
{
    function = reinterpret_cast<Function>(dlsym(state.library, name));
    return function != nullptr;
}

bool loadLibrary()
{
    state.library = dlopen("libwayland-client.so.0", RTLD_NOW | RTLD_LOCAL);
    if (state.library == nullptr)
    {
        return false;
    }
    const bool loaded = load(CRUCIBLE_wl_proxy_marshal_flags, "wl_proxy_marshal_flags") &&
                        load(CRUCIBLE_wl_proxy_add_listener, "wl_proxy_add_listener") &&
                        load(CRUCIBLE_wl_proxy_destroy, "wl_proxy_destroy") &&
                        load(CRUCIBLE_wl_proxy_get_version, "wl_proxy_get_version") &&
                        load(CRUCIBLE_wl_proxy_get_user_data, "wl_proxy_get_user_data") &&
                        load(CRUCIBLE_wl_proxy_set_user_data, "wl_proxy_set_user_data") &&
                        load(CRUCIBLE_wl_proxy_create_wrapper, "wl_proxy_create_wrapper") &&
                        load(CRUCIBLE_wl_proxy_wrapper_destroy, "wl_proxy_wrapper_destroy") &&
                        load(CRUCIBLE_wl_proxy_set_queue, "wl_proxy_set_queue") &&
                        load(CRUCIBLE_wl_display_create_queue, "wl_display_create_queue") &&
                        load(CRUCIBLE_wl_event_queue_destroy, "wl_event_queue_destroy") &&
                        load(CRUCIBLE_wl_display_dispatch_queue_pending, "wl_display_dispatch_queue_pending") &&
                        load(CRUCIBLE_wl_display_roundtrip_queue, "wl_display_roundtrip_queue") &&
                        load(CRUCIBLE_wl_display_flush, "wl_display_flush");
    if (!loaded)
    {
        dlclose(state.library);
        state.library = nullptr;
    }
    return loaded;
}

// The SDL window showing a surface, or null.
SDL_Window* windowOf(wl_surface* surface)
{
    if (surface == nullptr)
    {
        return nullptr;
    }
    int count = 0;
    SDL_Window** windows = SDL_GetWindows(&count);
    SDL_Window* found = nullptr;
    for (int i = 0; i < count && found == nullptr; ++i)
    {
        if (SDL_GetPointerProperty(SDL_GetWindowProperties(windows[i]), SDL_PROP_WINDOW_WAYLAND_SURFACE_POINTER,
                                   nullptr) == surface)
        {
            found = windows[i];
        }
    }
    SDL_free(windows);
    return found;
}

wl_surface* surfaceOf(SDL_Window* window)
{
    return static_cast<wl_surface*>(
        SDL_GetPointerProperty(SDL_GetWindowProperties(window), SDL_PROP_WINDOW_WAYLAND_SURFACE_POINTER, nullptr));
}

// Surface coordinates are logical; drag events carry pixels, like the pointer's.
void moveTo(SDL_Window* window, wl_fixed_t x, wl_fixed_t y)
{
    const float density = window != nullptr ? std::max(SDL_GetWindowPixelDensity(window), 1e-3f) : 1.0f;
    state.x = static_cast<float>(wl_fixed_to_double(x)) * density;
    state.y = static_cast<float>(wl_fixed_to_double(y)) * density;
}

void dropOffer()
{
    if (state.offer != nullptr)
    {
        wl_data_offer_destroy(state.offer);
        state.offer = nullptr;
    }
    state.target = 0;
}

// The drag is over: its objects go (a toplevel drag only now, which the
// protocol requires).
void endDrag()
{
    if (state.toplevelDrag != nullptr)
    {
        xdg_toplevel_drag_v1_destroy(state.toplevelDrag);
        state.toplevelDrag = nullptr;
    }
    if (state.source != nullptr)
    {
        wl_data_source_destroy(state.source);
        state.source = nullptr;
    }
}

// ---------------------------------------------------------------------------
// The pointer: which press a drag starts from
// ---------------------------------------------------------------------------

void pointerEnter(void*, wl_pointer*, uint32_t, wl_surface* surface, wl_fixed_t, wl_fixed_t)
{
    state.pointerSurface = surface;
}

void pointerLeave(void*, wl_pointer*, uint32_t, wl_surface*)
{
    state.pointerSurface = nullptr;
}

void pointerMotion(void*, wl_pointer*, uint32_t, wl_fixed_t, wl_fixed_t) {}

void pointerButton(void*, wl_pointer*, uint32_t serial, uint32_t, uint32_t, uint32_t buttonState)
{
    if (buttonState == WL_POINTER_BUTTON_STATE_PRESSED)
    {
        state.pressSerial = serial;
        state.pressSurface = state.pointerSurface;
    }
}

void pointerAxis(void*, wl_pointer*, uint32_t, uint32_t, wl_fixed_t) {}
void pointerFrame(void*, wl_pointer*) {}
void pointerAxisSource(void*, wl_pointer*, uint32_t) {}
void pointerAxisStop(void*, wl_pointer*, uint32_t, uint32_t) {}
void pointerAxisDiscrete(void*, wl_pointer*, uint32_t, int32_t) {}

const wl_pointer_listener pointerListener = {
    .enter = pointerEnter,
    .leave = pointerLeave,
    .motion = pointerMotion,
    .button = pointerButton,
    .axis = pointerAxis,
    .frame = pointerFrame,
    .axis_source = pointerAxisSource,
    .axis_stop = pointerAxisStop,
    .axis_discrete = pointerAxisDiscrete,
};

// Release requests exist from these versions on; before them, only the proxy goes.
void releasePointer()
{
    if (wl_pointer_get_version(state.pointer) >= WL_POINTER_RELEASE_SINCE_VERSION)
    {
        wl_pointer_release(state.pointer);
    }
    else
    {
        wl_pointer_destroy(state.pointer);
    }
    state.pointer = nullptr;
}

void seatCapabilities(void*, wl_seat* seat, uint32_t capabilities)
{
    const bool hasPointer = (capabilities & WL_SEAT_CAPABILITY_POINTER) != 0;
    if (hasPointer && state.pointer == nullptr)
    {
        state.pointer = wl_seat_get_pointer(seat);
        wl_pointer_add_listener(state.pointer, &pointerListener, nullptr);
    }
    else if (!hasPointer && state.pointer != nullptr)
    {
        releasePointer();
    }
}

void seatName(void*, wl_seat*, const char*) {}

const wl_seat_listener seatListener = {.capabilities = seatCapabilities, .name = seatName};

// ---------------------------------------------------------------------------
// Drags arriving over our windows
// ---------------------------------------------------------------------------

void deviceDataOffer(void*, wl_data_device*, wl_data_offer*)
{
    // Each offer is taken or destroyed in enter or selection.
}

void deviceEnter(void*, wl_data_device*, uint32_t serial, wl_surface* surface, wl_fixed_t x, wl_fixed_t y,
                 wl_data_offer* offer)
{
    dropOffer();
    state.offer = offer;
    SDL_Window* window = windowOf(surface);
    CRUCIBLE_DRAG_DEBUG("enter surface %p (window %u) offer %p, ours %d, at %.1f %.1f", static_cast<void*>(surface),
                        window != nullptr ? SDL_GetWindowID(window) : 0u, static_cast<void*>(offer),
                        state.source != nullptr, wl_fixed_to_double(x), wl_fixed_to_double(y));
    // Only our own drags: another app's files are SDL's to accept or
    // decline (whichever device answers last wins), and another Crucible's
    // panel isn't ours to dock.
    if (offer == nullptr || state.source == nullptr || window == nullptr)
    {
        return;
    }
    state.target = SDL_GetWindowID(window);
    wl_data_offer_accept(offer, serial, dragMimeType);
    if (wl_data_offer_get_version(offer) >= WL_DATA_OFFER_SET_ACTIONS_SINCE_VERSION)
    {
        wl_data_offer_set_actions(offer, WL_DATA_DEVICE_MANAGER_DND_ACTION_MOVE,
                                  WL_DATA_DEVICE_MANAGER_DND_ACTION_MOVE);
    }
    moveTo(window, x, y);
    pushDragEvent(InputEventType::DragMoved, state.target, state.x, state.y);
}

void deviceLeave(void*, wl_data_device*)
{
    CRUCIBLE_DRAG_DEBUG("leave (window %u)", state.target);
    const bool wasOver = state.target != 0;
    dropOffer();
    if (wasOver && state.source != nullptr)
    {
        pushDragEvent(InputEventType::DragMoved, 0, 0.0f, 0.0f);
    }
}

void deviceMotion(void*, wl_data_device*, uint32_t, wl_fixed_t x, wl_fixed_t y)
{
    if (state.target != 0)
    {
        moveTo(SDL_GetWindowFromID(state.target), x, y);
        pushDragEvent(InputEventType::DragMoved, state.target, state.x, state.y);
    }
}

void deviceDrop(void*, wl_data_device*)
{
    CRUCIBLE_DRAG_DEBUG("drop on window %u at %.1f %.1f", state.target, state.x, state.y);
    if (state.target != 0 && state.offer != nullptr)
    {
        state.dropped = true;
        pushDragEvent(InputEventType::DragDropped, state.target, state.x, state.y);
        if (wl_data_offer_get_version(state.offer) >= WL_DATA_OFFER_FINISH_SINCE_VERSION)
        {
            wl_data_offer_finish(state.offer);
        }
    }
    dropOffer();
}

void deviceSelection(void*, wl_data_device*, wl_data_offer* offer)
{
    // The clipboard is SDL's.
    if (offer != nullptr)
    {
        wl_data_offer_destroy(offer);
    }
}

const wl_data_device_listener deviceListener = {
    .data_offer = deviceDataOffer,
    .enter = deviceEnter,
    .leave = deviceLeave,
    .motion = deviceMotion,
    .drop = deviceDrop,
    .selection = deviceSelection,
};

// ---------------------------------------------------------------------------
// Our drag's source
// ---------------------------------------------------------------------------

void sourceTarget(void*, wl_data_source*, const char* mimeType)
{
    CRUCIBLE_DRAG_DEBUG("source target %s", mimeType != nullptr ? mimeType : "(none)");
}

void sourceSend(void*, wl_data_source*, const char*, int32_t fd)
{
    close(fd); // nothing to send
}

void sourceCancelled(void*, wl_data_source*)
{
    CRUCIBLE_DRAG_DEBUG("source cancelled (dropped on us: %d)", state.dropped);
    if (!state.dropped)
    {
        pushDragEvent(InputEventType::DragCanceled, 0, 0.0f, 0.0f);
    }
    endDrag();
}

void sourceDropPerformed(void*, wl_data_source*)
{
    CRUCIBLE_DRAG_DEBUG("source drop performed");
    // The toplevel drag may go now; the source waits for dnd_finished.
    if (state.toplevelDrag != nullptr)
    {
        xdg_toplevel_drag_v1_destroy(state.toplevelDrag);
        state.toplevelDrag = nullptr;
    }
}

void sourceFinished(void*, wl_data_source*)
{
    CRUCIBLE_DRAG_DEBUG("source finished");
    endDrag();
}

void sourceAction(void*, wl_data_source*, uint32_t action)
{
    CRUCIBLE_DRAG_DEBUG("source action %u", action);
}

const wl_data_source_listener sourceListener = {
    .target = sourceTarget,
    .send = sourceSend,
    .cancelled = sourceCancelled,
    .dnd_drop_performed = sourceDropPerformed,
    .dnd_finished = sourceFinished,
    .action = sourceAction,
};

// ---------------------------------------------------------------------------
// Globals
// ---------------------------------------------------------------------------

void registryGlobal(void*, wl_registry* registry, uint32_t name, const char* interface, uint32_t version)
{
    if (std::strcmp(interface, "wl_seat") == 0 && state.seat == nullptr)
    {
        // Version 5 at most: the pointer listener has exactly its events.
        state.seat = static_cast<wl_seat*>(
            wl_registry_bind(registry, name, &crucible_wl_seat_interface, std::min<uint32_t>(version, 5)));
        wl_seat_add_listener(state.seat, &seatListener, nullptr);
    }
    else if (std::strcmp(interface, "wl_data_device_manager") == 0 && state.manager == nullptr)
    {
        state.manager = static_cast<wl_data_device_manager*>(wl_registry_bind(
            registry, name, &crucible_wl_data_device_manager_interface, std::min<uint32_t>(version, 3)));
    }
    else if (std::strcmp(interface, "xdg_toplevel_drag_manager_v1") == 0 && state.dragManager == nullptr)
    {
        state.dragManager = static_cast<xdg_toplevel_drag_manager_v1*>(
            wl_registry_bind(registry, name, &crucible_xdg_toplevel_drag_manager_v1_interface, 1));
    }
}

void registryGlobalRemove(void*, wl_registry*, uint32_t) {}

const wl_registry_listener registryListener = {.global = registryGlobal, .global_remove = registryGlobalRemove};

} // namespace

void start()
{
    const char* driver = SDL_GetCurrentVideoDriver();
    if (driver == nullptr || std::strcmp(driver, "wayland") != 0 || state.display != nullptr)
    {
        return;
    }
    state.display = static_cast<wl_display*>(
        SDL_GetPointerProperty(SDL_GetGlobalProperties(), SDL_PROP_GLOBAL_VIDEO_WAYLAND_WL_DISPLAY_POINTER, nullptr));
    if (state.display == nullptr || !loadLibrary())
    {
        state.display = nullptr;
        std::fprintf(stderr, "crucible: no Wayland window drags (libwayland-client not loaded)\n");
        return;
    }
    state.queue = wl_display_create_queue(state.display);
    state.wrapper = static_cast<wl_display*>(wl_proxy_create_wrapper(state.display));
    wl_proxy_set_queue(reinterpret_cast<wl_proxy*>(state.wrapper), state.queue);
    state.registry = wl_display_get_registry(state.wrapper);
    wl_registry_add_listener(state.registry, &registryListener, nullptr);
    // The globals, then the seat's capabilities (its pointer).
    wl_display_roundtrip_queue(state.display, state.queue);
    wl_display_roundtrip_queue(state.display, state.queue);
    if (state.seat != nullptr && state.manager != nullptr)
    {
        state.device = wl_data_device_manager_get_data_device(state.manager, state.seat);
        wl_data_device_add_listener(state.device, &deviceListener, nullptr);
    }
    wl_display_flush(state.display);
}

void stop()
{
    if (state.display == nullptr)
    {
        return;
    }
    dropOffer();
    // A drag still going can't be ended cleanly; its objects go with the queue.
    if (state.toplevelDrag != nullptr)
    {
        wl_proxy_destroy(reinterpret_cast<wl_proxy*>(state.toplevelDrag));
    }
    if (state.source != nullptr)
    {
        wl_data_source_destroy(state.source);
    }
    if (state.device != nullptr)
    {
        if (wl_data_device_get_version(state.device) >= WL_DATA_DEVICE_RELEASE_SINCE_VERSION)
        {
            wl_data_device_release(state.device);
        }
        else
        {
            wl_data_device_destroy(state.device);
        }
    }
    if (state.dragManager != nullptr)
    {
        xdg_toplevel_drag_manager_v1_destroy(state.dragManager);
    }
    if (state.manager != nullptr)
    {
        wl_data_device_manager_destroy(state.manager);
    }
    if (state.pointer != nullptr)
    {
        releasePointer();
    }
    if (state.seat != nullptr)
    {
        if (wl_seat_get_version(state.seat) >= WL_SEAT_RELEASE_SINCE_VERSION)
        {
            wl_seat_release(state.seat);
        }
        else
        {
            wl_seat_destroy(state.seat);
        }
    }
    if (state.registry != nullptr)
    {
        wl_registry_destroy(state.registry);
    }
    wl_proxy_wrapper_destroy(state.wrapper);
    wl_display_flush(state.display);
    wl_event_queue_destroy(state.queue);
    dlclose(state.library);
    state = State{};
}

bool running()
{
    return state.display != nullptr;
}

WindowDragSupport dragSupport()
{
    if (state.device == nullptr)
    {
        return WindowDragSupport::None;
    }
    return state.dragManager != nullptr ? WindowDragSupport::Full : WindowDragSupport::DropOnly;
}

bool startDrag(SDL_Window* source, SDL_Window* dragged, float offsetX, float offsetY)
{
    wl_surface* origin = source != nullptr ? surfaceOf(source) : nullptr;
    // A drag starts from the press holding the pointer on the window it was in.
    if (state.device == nullptr || state.source != nullptr || origin == nullptr || state.pressSerial == 0 ||
        state.pressSurface != origin)
    {
        return false;
    }
    state.dropped = false;
    state.source = wl_data_device_manager_create_data_source(state.manager);
    wl_data_source_add_listener(state.source, &sourceListener, nullptr);
    wl_data_source_offer(state.source, dragMimeType);
    if (wl_data_source_get_version(state.source) >= WL_DATA_SOURCE_SET_ACTIONS_SINCE_VERSION)
    {
        wl_data_source_set_actions(state.source, WL_DATA_DEVICE_MANAGER_DND_ACTION_MOVE);
    }
    // Made, and the window attached, before start_drag: the protocol wants
    // an existing window attached before the drag starts, and a new one
    // before it's first drawn.
    if (state.dragManager != nullptr && dragged != nullptr)
    {
        auto* toplevel = static_cast<xdg_toplevel*>(SDL_GetPointerProperty(
            SDL_GetWindowProperties(dragged), SDL_PROP_WINDOW_WAYLAND_XDG_TOPLEVEL_POINTER, nullptr));
        if (toplevel != nullptr)
        {
            state.toplevelDrag = xdg_toplevel_drag_manager_v1_get_xdg_toplevel_drag(state.dragManager, state.source);
            xdg_toplevel_drag_v1_attach(state.toplevelDrag, toplevel, static_cast<int32_t>(offsetX),
                                        static_cast<int32_t>(offsetY));
        }
    }
    wl_data_device_start_drag(state.device, state.source, origin, nullptr, state.pressSerial);
    CRUCIBLE_DRAG_DEBUG("started from window %u (surface %p), dragging window %u (toplevel drag %p)",
                        SDL_GetWindowID(source), static_cast<void*>(origin),
                        dragged != nullptr ? SDL_GetWindowID(dragged) : 0u, static_cast<void*>(state.toplevelDrag));
    wl_display_flush(state.display);
    return true;
}

bool startMove(SDL_Window* window)
{
    wl_surface* surface = window != nullptr ? surfaceOf(window) : nullptr;
    auto* toplevel = window != nullptr
                         ? static_cast<xdg_toplevel*>(SDL_GetPointerProperty(
                               SDL_GetWindowProperties(window), SDL_PROP_WINDOW_WAYLAND_XDG_TOPLEVEL_POINTER, nullptr))
                         : nullptr;
    // From the press holding the pointer in this window, on our seat (the
    // same seat as SDL's, bound again).
    if (toplevel == nullptr || state.seat == nullptr || state.pressSerial == 0 || state.pressSurface != surface)
    {
        return false;
    }
    xdg_toplevel_move(toplevel, state.seat, state.pressSerial);
    wl_display_flush(state.display);
    CRUCIBLE_DRAG_DEBUG("move of window %u", SDL_GetWindowID(window));
    return true;
}

void stopDrag()
{
    // Nothing to do: a Wayland drag can't be cancelled by the app without
    // breaking the toplevel drag's rules; the compositor ends it (Escape,
    // or the release).
}

void dispatch()
{
    if (state.display != nullptr)
    {
        wl_display_dispatch_queue_pending(state.display, state.queue);
        wl_display_flush(state.display);
    }
}

} // namespace crucible::platform::wayland
