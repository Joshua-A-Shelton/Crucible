# Native modules use the engine's C++ through the shared core (ADR 0074), so
# every function a public header declares must be exported: marked
# CRUCIBLE_EXPORT, or a class marked as a whole. This finds declarations at
# namespace scope (the headers don't indent namespaces, so they start a line)
# that lack it. Inline functions, templates, and the like are compiled into
# the module itself and need nothing. Run as a test:
# cmake -DHEADER_DIR=<dir> -P this file.

file(GLOB headers "${HEADER_DIR}/*.h")
set(violations "")
foreach(header IN LISTS headers)
    file(STRINGS "${header}" lines)
    set(previous "")
    foreach(line IN LISTS lines)
        if(line MATCHES "^[A-Za-z_][^;{}=]*[a-z][A-Za-z0-9_]*\\(" AND
           NOT line MATCHES "^(inline|template|constexpr|consteval|static|static_assert|using|typedef|return|struct|class|enum|union|namespace|extern|friend|CRUCIBLE_EXPORT)[ <(]" AND
           NOT line MATCHES "^[A-Z0-9_]+\\(" AND
           NOT previous MATCHES "^(template|inline|constexpr)")
            get_filename_component(name "${header}" NAME)
            string(APPEND violations "  ${name}: ${line}\n")
        endif()
        if(NOT line STREQUAL "")
            set(previous "${line}")
        endif()
    endforeach()
endforeach()

if(violations)
    message(FATAL_ERROR "Public declarations not exported (add CRUCIBLE_EXPORT, ADR 0074):\n${violations}")
endif()
message(STATUS "exports: ok")
