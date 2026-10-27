# ADR 0032: the core runtime includes only standard headers that don't
# allocate. Run as a test: cmake -DCORE_DIR=<src/native/core> -P this file.
# A standard header is an include without a slash or a dot (<vector>); C
# headers (<malloc.h>), platform headers, and library headers have one.

set(allowed
    algorithm array atomic bit cassert cerrno chrono climits cmath compare concepts cstdarg cstddef cstdint cstdio
    cstdlib cstring initializer_list limits mutex new numeric optional random span string_view thread type_traits
    utility)

file(GLOB_RECURSE sources "${CORE_DIR}/*.h" "${CORE_DIR}/*.cpp")
set(violations "")
foreach(source IN LISTS sources)
    file(STRINGS "${source}" includes REGEX "^[ \t]*#[ \t]*include[ \t]*<[a-z_0-9]+>")
    foreach(line IN LISTS includes)
        string(REGEX REPLACE ".*<([a-z_0-9]+)>.*" "\\1" header "${line}")
        if(NOT header IN_LIST allowed)
            file(RELATIVE_PATH relative "${CORE_DIR}" "${source}")
            string(APPEND violations "  ${relative}: <${header}>\n")
        endif()
    endforeach()
endforeach()

if(violations)
    message(FATAL_ERROR "Standard headers the core may not include (ADR 0032; use Crucible's containers, or "
                        "extend the list in cmake/CheckCoreIncludes.cmake if the header doesn't allocate):\n"
                        "${violations}")
endif()
message(STATUS "core includes: ok (${CMAKE_CURRENT_LIST_FILE})")
