# Generates a Wayland protocol's client header and interface code (ADR 0066)
# for the platform layer's own Wayland connection, with every interface
# renamed crucible_<name>_interface, so they never clash with SDL's copies in
# the same library, and the header including only wayland-client-core.h
# (its functions are looked up at run time). The code is meant to be
# included into one C++ file.
#
#   cmake -DSCANNER=<wayland-scanner> -DXML=<protocol.xml> -DHEADER=<out.h> -DCODE=<out.c>
#         -P WaylandProtocol.cmake

foreach(output HEADER CODE)
    set(mode client-header)
    if(output STREQUAL "CODE")
        set(mode private-code)
    endif()
    execute_process(COMMAND "${SCANNER}" ${mode} "${XML}" "${${output}}.tmp" RESULT_VARIABLE failed)
    if(failed)
        message(FATAL_ERROR "wayland-scanner ${mode} failed for ${XML}")
    endif()
    file(READ "${${output}}.tmp" text)
    string(REGEX REPLACE "([^a-z_])((wl|xdg)_[a-z0-9_]+_interface)" "\\1crucible_\\2" text "${text}")
    string(REPLACE "#include \"wayland-client.h\"" "#include <wayland-client-core.h>" text "${text}")
    # Included into one C++ file, where a const object's visibility attribute is ignored (and warned about).
    string(REPLACE "__attribute__ ((visibility(\"hidden\")))" "" text "${text}")
    file(WRITE "${${output}}" "${text}")
    file(REMOVE "${${output}}.tmp")
endforeach()
