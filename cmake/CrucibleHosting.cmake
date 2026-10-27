# Finds nethost, the small static library that ships with the .NET SDK and
# locates hostfxr, plus the hosting headers (ADR 0011). Defines the imported
# target crucible::nethost.
#
# The SDK keeps them in its AppHost pack:
#   <dotnet root>/packs/Microsoft.NETCore.App.Host.<rid>/<version>/runtimes/<rid>/native/
# The runtime identifier varies (linux-x64, win-x64, and distribution builds
# such as arch-x64), so the pack is found by pattern. The newest version wins.

get_filename_component(_crucible_dotnet_real "${DOTNET_EXECUTABLE}" REALPATH)
get_filename_component(CRUCIBLE_DOTNET_ROOT "${_crucible_dotnet_real}" DIRECTORY)

file(GLOB _crucible_nethost_headers
    "${CRUCIBLE_DOTNET_ROOT}/packs/Microsoft.NETCore.App.Host.*/*/runtimes/*/native/nethost.h")
if(NOT _crucible_nethost_headers)
    message(FATAL_ERROR "nethost not found under ${CRUCIBLE_DOTNET_ROOT}/packs; is the .NET SDK installed?")
endif()
list(SORT _crucible_nethost_headers COMPARE NATURAL ORDER DESCENDING)
list(GET _crucible_nethost_headers 0 _crucible_nethost_header)
get_filename_component(CRUCIBLE_NETHOST_DIR "${_crucible_nethost_header}" DIRECTORY)

# On Windows the static libnethost.lib is built against the static C runtime
# (/MT), which can't be mixed with the DLL runtime Crucible and Slag use
# (/MD), so Windows uses nethost.dll, which crucible_copy_nethost puts next to
# the executable. Linux has one C runtime and links nethost statically.
if(WIN32)
    add_library(crucible_nethost SHARED IMPORTED GLOBAL)
    set_target_properties(crucible_nethost PROPERTIES
        IMPORTED_IMPLIB "${CRUCIBLE_NETHOST_DIR}/nethost.lib"
        IMPORTED_LOCATION "${CRUCIBLE_NETHOST_DIR}/nethost.dll"
        INTERFACE_INCLUDE_DIRECTORIES "${CRUCIBLE_NETHOST_DIR}")
else()
    add_library(crucible_nethost STATIC IMPORTED GLOBAL)
    set_target_properties(crucible_nethost PROPERTIES
        IMPORTED_LOCATION "${CRUCIBLE_NETHOST_DIR}/libnethost.a"
        INTERFACE_INCLUDE_DIRECTORIES "${CRUCIBLE_NETHOST_DIR}"
        INTERFACE_COMPILE_DEFINITIONS NETHOST_USE_AS_STATIC)
    target_link_libraries(crucible_nethost INTERFACE ${CMAKE_DL_LIBS})
endif()
add_library(crucible::nethost ALIAS crucible_nethost)

# crucible_copy_nethost(<target>): copies nethost.dll next to <target> on Windows.
function(crucible_copy_nethost target)
    if(WIN32)
        add_custom_command(TARGET ${target} POST_BUILD
            COMMAND "${CMAKE_COMMAND}" -E copy_if_different
                "${CRUCIBLE_NETHOST_DIR}/nethost.dll" "$<TARGET_FILE_DIR:${target}>"
            VERBATIM)
    endif()
endfunction()

message(STATUS "Crucible: nethost from ${CRUCIBLE_NETHOST_DIR}")
