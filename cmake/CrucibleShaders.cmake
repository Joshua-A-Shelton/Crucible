# Compiles Slang shaders at build time (ADR 0047) with crucible_shaderc.
#
#   crucible_add_shader(<target> SOURCE <file.slang>
#                       [CPP_NAMESPACE <ns>] [CS_NAMESPACE <ns>] [CS_OUTPUT_DIR <dir>])
#
# Writes <name>.shdr (a SHDR binary file, DXIL on Windows and SPIR-V) and
# <name>.g.h / <name>.g.cs (the mirrors of its [GpuShared] structs) into
# ${CMAKE_CURRENT_BINARY_DIR}/shaders, which <target> gets as an include
# directory and as CRUCIBLE_SHADER_DIR. CS_OUTPUT_DIR puts the C# mirror
# elsewhere, for a C# project to compile. Shaders import Crucible's library
# from shaders/ in the source tree.

set(CRUCIBLE_SHADER_LIBRARY_DIR "${PROJECT_SOURCE_DIR}/shaders")

function(crucible_add_shader target)
    cmake_parse_arguments(ARG "" "SOURCE;CPP_NAMESPACE;CS_NAMESPACE;CS_OUTPUT_DIR" "" ${ARGN})
    if(NOT ARG_SOURCE)
        message(FATAL_ERROR "crucible_add_shader: SOURCE is required")
    endif()
    if(NOT ARG_CPP_NAMESPACE)
        set(ARG_CPP_NAMESPACE "crucible::shaders")
    endif()
    if(NOT ARG_CS_NAMESPACE)
        set(ARG_CS_NAMESPACE "Crucible.Shaders")
    endif()
    get_filename_component(_source "${ARG_SOURCE}" ABSOLUTE)
    get_filename_component(_name "${ARG_SOURCE}" NAME_WE)
    set(_dir "${CMAKE_CURRENT_BINARY_DIR}/shaders")
    set(_cs_dir "${_dir}")
    if(ARG_CS_OUTPUT_DIR)
        set(_cs_dir "${ARG_CS_OUTPUT_DIR}")
    endif()
    set(_outputs "${_dir}/${_name}.shdr" "${_dir}/${_name}.g.h" "${_cs_dir}/${_name}.g.cs")
    file(GLOB_RECURSE _library CONFIGURE_DEPENDS "${CRUCIBLE_SHADER_LIBRARY_DIR}/*.slang")
    add_custom_command(OUTPUT ${_outputs}
        COMMAND "${CMAKE_COMMAND}" -E make_directory "${_dir}" "${_cs_dir}"
        COMMAND crucible_shaderc "${_source}" --output "${_dir}/${_name}.shdr"
            --include "${CRUCIBLE_SHADER_LIBRARY_DIR}" --include "${CMAKE_CURRENT_SOURCE_DIR}"
            --cpp "${_dir}/${_name}.g.h" --cpp-namespace "${ARG_CPP_NAMESPACE}"
            --cs "${_cs_dir}/${_name}.g.cs" --cs-namespace "${ARG_CS_NAMESPACE}"
        DEPENDS "${_source}" ${_library} crucible_shaderc
        COMMENT "Slang ${_name}"
        VERBATIM)
    set(_shader_target "${target}_shader_${_name}")
    add_custom_target(${_shader_target} DEPENDS ${_outputs})
    add_dependencies(${target} ${_shader_target})
    target_include_directories(${target} PRIVATE "${_dir}")
    target_compile_definitions(${target} PRIVATE "CRUCIBLE_SHADER_DIR=\"${_dir}\"")
endfunction()
