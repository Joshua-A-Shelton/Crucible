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

if(WIN32)
    set(_crucible_nethost_lib "${CRUCIBLE_NETHOST_DIR}/libnethost.lib")
else()
    set(_crucible_nethost_lib "${CRUCIBLE_NETHOST_DIR}/libnethost.a")
endif()

add_library(crucible_nethost STATIC IMPORTED GLOBAL)
add_library(crucible::nethost ALIAS crucible_nethost)
set_target_properties(crucible_nethost PROPERTIES
    IMPORTED_LOCATION "${_crucible_nethost_lib}"
    INTERFACE_INCLUDE_DIRECTORIES "${CRUCIBLE_NETHOST_DIR}"
    INTERFACE_COMPILE_DEFINITIONS NETHOST_USE_AS_STATIC)
if(NOT WIN32)
    target_link_libraries(crucible_nethost INTERFACE ${CMAKE_DL_LIBS})
endif()

message(STATUS "Crucible: nethost from ${CRUCIBLE_NETHOST_DIR}")
