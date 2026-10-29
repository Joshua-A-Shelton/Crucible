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

# The optional render graph module (Slag ADR 0072), which the renderer is
# built on (ADR 0015, 0048).
set(SLAG_BUILD_RENDERGRAPH ON)
FetchContent_Declare(Slag
    URL https://github.com/Joshua-A-Shelton/Slag/archive/refs/tags/v1.0.2.tar.gz
    URL_HASH SHA256=4ceb89d5dc4f28adeb6eb8194cf4dd4c34f7412d575167d29aec9f62935a512c)
FetchContent_MakeAvailable(Slag)

# The D3D12 Agility SDK (Slag's pinned D3D12 runtime) loads only in an
# executable that exports its version and folder, so every executable that
# creates a device calls this. The version is read from Slag's own scope so
# it can't drift from the SDK Slag was built with.
if(WIN32)
    get_directory_property(CRUCIBLE_AGILITY_SDK_VERSION DIRECTORY "${slag_SOURCE_DIR}"
        DEFINITION SLAG_AGILITY_SDK_VERSION)
endif()
function(crucible_use_d3d12_agility target)
    if(NOT WIN32 OR NOT CRUCIBLE_AGILITY_SDK_VERSION)
        return()
    endif()
    target_sources(${target} PRIVATE "${PROJECT_SOURCE_DIR}/src/native/launcher/D3D12Agility.cpp")
    target_compile_definitions(${target} PRIVATE CRUCIBLE_AGILITY_SDK_VERSION=${CRUCIBLE_AGILITY_SDK_VERSION})
    slag_copy_agility_runtime(${target})
endfunction()

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
# zstd (BSD-3-Clause; shipped games include its notice): compresses the
# sections of Crucible's binary files (ADR 0039). Built static, library only:
# no programs, tests, legacy formats, or dictionary builder, and no threads of
# its own, since sections are (de)compressed as jobs (ADR 0014).
# zstd's CMake asks for 3.10, which leaves policy CMP0091 off and with it
# CMAKE_MSVC_RUNTIME_LIBRARY; turning the policy on gives zstd the static
# runtime like everything else (ADR 0031).
# ---------------------------------------------------------------------------

FetchContent_Declare(zstd
    URL https://github.com/facebook/zstd/releases/download/v1.5.7/zstd-1.5.7.tar.gz
    URL_HASH SHA256=eb33e51f49a15e023950cd7825ca74a4a2b43db8354825ac24fc1b7ee09e6fa3
    SOURCE_SUBDIR build/cmake)
set(CMAKE_POLICY_DEFAULT_CMP0091 NEW)
foreach(_crucible_zstd_off ZSTD_BUILD_PROGRAMS ZSTD_BUILD_TESTS ZSTD_BUILD_SHARED ZSTD_BUILD_CONTRIB
                           ZSTD_LEGACY_SUPPORT ZSTD_MULTITHREAD_SUPPORT ZSTD_BUILD_DICTBUILDER)
    set(${_crucible_zstd_off} OFF CACHE BOOL "" FORCE)
endforeach()
set(ZSTD_BUILD_STATIC ON CACHE BOOL "" FORCE)
FetchContent_MakeAvailable(zstd)
unset(CMAKE_POLICY_DEFAULT_CMP0091)
# Linked into the shared core too, so position-independent.
set_target_properties(libzstd_static PROPERTIES POSITION_INDEPENDENT_CODE ON)

# ---------------------------------------------------------------------------
# Editor-only importers (ADR 0022, 0042), built into crucible_importers and
# never shipped. Neither project tags releases, so both are pinned by commit.
#   stb_image (MIT or public domain): decodes PNG, JPEG, TGA, and Radiance HDR.
#   bc7enc_rdo (MIT or public domain; its optional ISPC encoder, Apache 2.0,
#     and LodePNG aren't used): BC7, BC4, and BC5 encoding and decoding, and
#     ERT, its rate-distortion pass. Its own build makes a command-line tool
#     with OpenMP, so only its sources are fetched (SOURCE_SUBDIR skips it).
# ---------------------------------------------------------------------------

FetchContent_Declare(stb
    URL https://github.com/nothings/stb/archive/003501f6d7a8668ce8b6d875e34dd97ad0d5d5b5.tar.gz
    URL_HASH SHA256=a6c31950b3bdf05eb92258768ba2a11bd44449490574b1aac67028452288c40d)
FetchContent_Declare(bc7enc_rdo
    URL https://github.com/richgel999/bc7enc_rdo/archive/b9438627eef73a1157e84201b6fa6eb2ffd6d9f0.tar.gz
    URL_HASH SHA256=bbb33d1dbb6178a3a2c044956c3cc0b8ea9fc2f2903824379dd86f87d53b64c6
    SOURCE_SUBDIR do-not-build)
#   cgltf v1.15 (MIT): parses glTF 2.0, .gltf and .glb (ADR 0022, 0043).
#   meshoptimizer v1.3 (MIT): welding, vertex cache, overdraw, and fetch
#     optimization, and LOD simplification (ADR 0043). Built from its sources
#     like bc7enc_rdo, so its own build (and its options) stay out.
FetchContent_Declare(cgltf
    URL https://github.com/jkuhlmann/cgltf/archive/refs/tags/v1.15.tar.gz
    URL_HASH SHA256=84e352092e5cd6aab7f66de62ddb66beb5e6f18d412ca9d12950d7a55bfef25a
    SOURCE_SUBDIR do-not-build)
FetchContent_Declare(meshoptimizer
    URL https://github.com/zeux/meshoptimizer/archive/refs/tags/v1.3.tar.gz
    URL_HASH SHA256=ca4f47d451201593904b9e3067fa54b65db94bc34ab5173864950862b748a145
    SOURCE_SUBDIR do-not-build)
#   MikkTSpace (zlib): tangents for meshes without them, the reference that
#     normal-map bakers (Blender, Substance) match (ADR 0043). Pinned by
#     commit; its licence is in its source.
FetchContent_Declare(mikktspace
    URL https://github.com/mmikk/MikkTSpace/archive/3e895b49d05ea07e4c2133156cfa94369e19e409.tar.gz
    URL_HASH SHA256=aeba65ddee85a679133d510d71d00b108f603752f90b15b9ecf7777033f6e351
    SOURCE_SUBDIR do-not-build)
#   ufbx v0.23.1 (MIT or public domain): reads FBX, converting axes, units,
#     and geometry transforms at load (ADR 0022, 0044). Its data/ folder
#     doubles as the FBX importer's test models.
FetchContent_Declare(ufbx
    URL https://github.com/ufbx/ufbx/archive/refs/tags/v0.23.1.tar.gz
    URL_HASH SHA256=21edd1021dfb430e37aa6214c9b3bbaa5634b1859cbbef7231e738af4f19c956
    SOURCE_SUBDIR do-not-build)
FetchContent_MakeAvailable(stb bc7enc_rdo cgltf meshoptimizer mikktspace ufbx)

# ---------------------------------------------------------------------------
# Shader compilers (ADR 0025, 0047): build time and the editor, never shipped.
#   Slang 2026.18.2 (Apache 2.0 with LLVM exception), its release binaries:
#     the library behind crucible_importers' shader compiler and
#     shaderfax. The version Slag is tested with.
#   DXC v1.9.2609 (University of Illinois/NCSA, approved for the editor and
#     build only), Windows only: Slang loads it from beside itself to
#     produce DXIL. Like Slag, Crucible builds DXIL only on Windows.
# Both are pinned to the same archives as Slag's CI.
# ---------------------------------------------------------------------------

if(WIN32)
    FetchContent_Declare(slang
        URL https://github.com/shader-slang/slang/releases/download/v2026.18.2/slang-2026.18.2-windows-x86_64.zip
        URL_HASH SHA256=747602aec6b3623658d55fea87492d71828e15d16802d7941204fde418ceee8e
        SOURCE_SUBDIR do-not-build)
    FetchContent_Declare(dxc
        URL https://github.com/microsoft/DirectXShaderCompiler/releases/download/v1.9.2609/dxc_2026_09_29.zip
        URL_HASH SHA256=ad31b1fc8443175d204f77a611fdb3ef2ec42759bdc2f1167368de24a4a7e7f1
        SOURCE_SUBDIR do-not-build)
    FetchContent_MakeAvailable(slang dxc)
else()
    FetchContent_Declare(slang
        URL https://github.com/shader-slang/slang/releases/download/v2026.18.2/slang-2026.18.2-linux-x86_64.tar.gz
        URL_HASH SHA256=8a097d4365e1cab10265d0b0d77b461b5d30576f99ddc0521a35723c34cad816
        SOURCE_SUBDIR do-not-build)
    FetchContent_MakeAvailable(slang)
endif()
# The Linux archive keeps the package in lib/cmake/slang, the Windows one in cmake.
find_package(slang CONFIG REQUIRED PATHS "${slang_SOURCE_DIR}/lib/cmake/slang" "${slang_SOURCE_DIR}/cmake" NO_DEFAULT_PATH)

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
