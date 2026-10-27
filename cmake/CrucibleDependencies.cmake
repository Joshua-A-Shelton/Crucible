# Pinned third-party dependencies (CLAUDE.md §3). Each is fetched at configure
# time and verified by hash. For offline builds, point
# FETCHCONTENT_SOURCE_DIR_<NAME> at a local copy.

include(FetchContent)

# ---------------------------------------------------------------------------
# Slag (zlib)
# Pinned tag by default. During development, configure with
# -DFETCHCONTENT_SOURCE_DIR_SLAG=<path to a Slag checkout> to change Slag and
# Crucible side by side (ADR 0015, docs/milestones.md M0).
# ---------------------------------------------------------------------------

FetchContent_Declare(Slag
    URL https://github.com/Joshua-A-Shelton/Slag/archive/refs/tags/v1.0.0.tar.gz
    URL_HASH SHA256=200be92484fb716a832d84a26e792b482d75f002fd92797e93e192ad839119c6)
FetchContent_MakeAvailable(Slag)

# ---------------------------------------------------------------------------
# SDL3 (zlib): the platform layer (ADR 0018), linked into the core behind
# Crucible's own platform API. Same version and hash as Slag's samples.
# Built static, with only the subsystems in use so far: video and events.
# Gamepads (joystick, HIDAPI, haptic) come with UI navigation in M6, audio in
# M8, dialogs with the editor in M7. Slag creates the Vulkan surface itself,
# so SDL's Vulkan support stays off.
# ---------------------------------------------------------------------------

FetchContent_Declare(SDL3
    URL https://github.com/libsdl-org/SDL/releases/download/release-3.4.18/SDL3-3.4.18.tar.gz
    URL_HASH SHA256=9c75cf16330322c217dedd2e0609f1124f1b54b8633e763467b4684d0f4334a3)
set(SDL_SHARED OFF CACHE BOOL "" FORCE)
set(SDL_STATIC ON CACHE BOOL "" FORCE)
set(SDL_TEST_LIBRARY OFF CACHE BOOL "" FORCE)
set(SDL_EXAMPLES OFF CACHE BOOL "" FORCE)
set(SDL_INSTALL OFF CACHE BOOL "" FORCE)
foreach(_crucible_sdl_off SDL_AUDIO SDL_CAMERA SDL_GPU SDL_RENDER SDL_HAPTIC SDL_HIDAPI SDL_JOYSTICK SDL_SENSOR
                          SDL_POWER SDL_DIALOG SDL_OPENGL SDL_OPENGLES SDL_VULKAN)
    set(${_crucible_sdl_off} OFF CACHE BOOL "" FORCE)
endforeach()
FetchContent_MakeAvailable(SDL3)

# ---------------------------------------------------------------------------
# doctest (MIT): tests only, never linked into the engine (ADR 0029)
# ---------------------------------------------------------------------------

if(CRUCIBLE_BUILD_TESTS)
    FetchContent_Declare(doctest
        URL https://github.com/doctest/doctest/archive/refs/tags/v2.5.3.tar.gz
        URL_HASH SHA256=174ebc4e769928959614789c5b4e9c3d0a0f81a62bb608756b127bfebfb21331)
    FetchContent_MakeAvailable(doctest)
    include("${doctest_SOURCE_DIR}/scripts/cmake/doctest.cmake")
endif()
