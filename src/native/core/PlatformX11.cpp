// Window moves on X11 (ADR 0066): the window manager's _NET_WM_MOVERESIZE,
// which SDL has no call for. libX11 is looked up at run time, as SDL does,
// so the library starts without it. Crucible's headers come before Xlib's,
// whose macros (Status, None, Bool) would clash with them.

#include "PlatformInternal.h"

#include <X11/Xlib.h>

#include <dlfcn.h>

namespace crucible::platform::x11 {

namespace {

struct Functions
{
    void* library = nullptr;
    decltype(&XInternAtom) internAtom = nullptr;
    decltype(&XSendEvent) sendEvent = nullptr;
    decltype(&XUngrabPointer) ungrabPointer = nullptr;
    decltype(&XDefaultRootWindow) defaultRootWindow = nullptr;
    decltype(&XFlush) flush = nullptr;
};

template <typename Function>
bool load(void* library, Function& function, const char* name)
{
    function = reinterpret_cast<Function>(dlsym(library, name));
    return function != nullptr;
}

// Loaded on first use, and kept: SDL keeps its own copy loaded too.
const Functions* functions()
{
    static Functions loaded = [] {
        Functions f;
        f.library = dlopen("libX11.so.6", RTLD_NOW | RTLD_LOCAL);
        if (f.library == nullptr || !load(f.library, f.internAtom, "XInternAtom") ||
            !load(f.library, f.sendEvent, "XSendEvent") || !load(f.library, f.ungrabPointer, "XUngrabPointer") ||
            !load(f.library, f.defaultRootWindow, "XDefaultRootWindow") || !load(f.library, f.flush, "XFlush"))
        {
            f.internAtom = nullptr;
        }
        return f;
    }();
    return loaded.internAtom != nullptr ? &loaded : nullptr;
}

} // namespace

bool startMove(SDL_Window* window, float rootX, float rootY)
{
    const Functions* x = functions();
    SDL_PropertiesID properties = SDL_GetWindowProperties(window);
    auto* display =
        static_cast<Display*>(SDL_GetPointerProperty(properties, SDL_PROP_WINDOW_X11_DISPLAY_POINTER, nullptr));
    const auto handle = static_cast<::Window>(SDL_GetNumberProperty(properties, SDL_PROP_WINDOW_X11_WINDOW_NUMBER, 0));
    if (x == nullptr || display == nullptr || handle == 0)
    {
        return false;
    }
    constexpr long moveresizeMove = 8;    // _NET_WM_MOVERESIZE_MOVE
    constexpr long sourceApplication = 1; // a normal application asking
    XEvent event{};
    event.xclient.type = ClientMessage;
    event.xclient.window = handle;
    event.xclient.message_type = x->internAtom(display, "_NET_WM_MOVERESIZE", False);
    event.xclient.format = 32;
    event.xclient.data.l[0] = static_cast<long>(rootX);
    event.xclient.data.l[1] = static_cast<long>(rootY);
    event.xclient.data.l[2] = moveresizeMove;
    event.xclient.data.l[3] = Button1;
    event.xclient.data.l[4] = sourceApplication;
    // The press's implicit grab is ours; the window manager needs the pointer.
    x->ungrabPointer(display, CurrentTime);
    const bool sent = x->sendEvent(display, x->defaultRootWindow(display), False,
                                   SubstructureRedirectMask | SubstructureNotifyMask, &event) != 0;
    x->flush(display);
    return sent;
}

} // namespace crucible::platform::x11
