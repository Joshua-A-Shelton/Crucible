// Window moves on Windows (ADR 0066): a caption press posted to the window,
// so Windows runs its own move (with Snap) from the button still held. SDL
// has no call for it. Its own file, so windows.h stays out of the rest.

#include "PlatformInternal.h"

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>

namespace crucible::platform::windows {

bool startMove(SDL_Window* window)
{
    auto* hwnd = static_cast<HWND>(
        SDL_GetPointerProperty(SDL_GetWindowProperties(window), SDL_PROP_WINDOW_WIN32_HWND_POINTER, nullptr));
    POINT cursor{};
    if (hwnd == nullptr || !GetCursorPos(&cursor))
    {
        return false;
    }
    // Posted, not sent: the move's loop runs in the next poll, not inside
    // whatever handled the press.
    ReleaseCapture();
    return PostMessageW(hwnd, WM_NCLBUTTONDOWN, HTCAPTION, MAKELPARAM(cursor.x, cursor.y)) != 0;
}

} // namespace crucible::platform::windows
