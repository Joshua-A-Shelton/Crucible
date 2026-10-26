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
# doctest (MIT): tests only, never linked into the engine (ADR 0029)
# ---------------------------------------------------------------------------

if(CRUCIBLE_BUILD_TESTS)
    FetchContent_Declare(doctest
        URL https://github.com/doctest/doctest/archive/refs/tags/v2.5.3.tar.gz
        URL_HASH SHA256=174ebc4e769928959614789c5b4e9c3d0a0f81a62bb608756b127bfebfb21331)
    FetchContent_MakeAvailable(doctest)
    include("${doctest_SOURCE_DIR}/scripts/cmake/doctest.cmake")
endif()
