# The binding generator (ADR 0017): builds Crucible.BindGen and provides
# crucible_generate_bindings() to run it.
#
# The generator loads the system's libclang (ADR 0017's amendment), found next
# to the system Clang: libclang finds Clang's built-in headers relative to
# itself, so the two must come from the same installation.

# LLVM's Windows installer doesn't always add itself to PATH, so its default
# folder is searched too.
find_program(CRUCIBLE_CLANG NAMES clang
    HINTS "$ENV{ProgramFiles}/LLVM/bin"
    REQUIRED
    DOC "Clang, whose libclang the binding generator uses")
get_filename_component(_crucible_clang_real "${CRUCIBLE_CLANG}" REALPATH)
get_filename_component(_crucible_llvm_prefix "${_crucible_clang_real}" DIRECTORY)
get_filename_component(_crucible_llvm_prefix "${_crucible_llvm_prefix}" DIRECTORY)

if(WIN32)
    # The generator loads the DLL itself, not the import library find_library finds.
    find_file(CRUCIBLE_LIBCLANG
        NAMES libclang.dll
        HINTS "${_crucible_llvm_prefix}/bin"
        REQUIRED
        DOC "libclang, which the binding generator loads")
else()
    find_library(CRUCIBLE_LIBCLANG
        NAMES clang libclang.so.1
        HINTS "${_crucible_llvm_prefix}/lib"
        REQUIRED
        DOC "libclang, which the binding generator loads")
endif()
message(STATUS "Crucible: binding generator uses ${CRUCIBLE_LIBCLANG}")

set(CRUCIBLE_GENERATED_DIR "${CMAKE_BINARY_DIR}/generated")
set(CRUCIBLE_BINDGEN_PROJECT "${PROJECT_SOURCE_DIR}/tools/Crucible.BindGen/Crucible.BindGen.csproj")
set(CRUCIBLE_BINDGEN_DLL
    "${CRUCIBLE_DOTNET_ARTIFACTS_DIR}/bin/Crucible.BindGen/${CRUCIBLE_DOTNET_CONFIGURATION_DIR}/Crucible.BindGen.dll")

add_custom_target(crucible_bindgen_tool
    COMMAND ${CRUCIBLE_DOTNET} build "${CRUCIBLE_BINDGEN_PROJECT}"
        --configuration ${CRUCIBLE_DOTNET_CONFIGURATION}
        --nologo
        "-p:ArtifactsPath=${CRUCIBLE_DOTNET_ARTIFACTS_DIR}"
    BYPRODUCTS "${CRUCIBLE_BINDGEN_DLL}"
    WORKING_DIRECTORY "${PROJECT_SOURCE_DIR}"
    USES_TERMINAL
    VERBATIM)

# crucible_generate_bindings(OUTPUT_CPP <file> OUTPUT_CS <file>
#     INCLUDE_DIRECTORIES <dirs>... HEADERS <headers>...)
# Must be called in the directory of the target that compiles OUTPUT_CPP.
# The depfile lists every file clang read, so changing any header an exposed
# header includes reruns the generator.
function(crucible_generate_bindings)
    cmake_parse_arguments(PARSE_ARGV 0 bindings "" "OUTPUT_CPP;OUTPUT_CS" "INCLUDE_DIRECTORIES;HEADERS")
    set(depfile "${CRUCIBLE_GENERATED_DIR}/bindings.d")
    set(arguments
        --libclang "${CRUCIBLE_LIBCLANG}"
        --output-cpp "${bindings_OUTPUT_CPP}"
        --output-cs "${bindings_OUTPUT_CS}"
        --depfile "${depfile}")
    foreach(directory IN LISTS bindings_INCLUDE_DIRECTORIES)
        list(APPEND arguments --include-dir "${directory}")
    endforeach()
    foreach(header IN LISTS bindings_HEADERS)
        list(APPEND arguments --header "${header}")
    endforeach()

    add_custom_command(
        OUTPUT "${bindings_OUTPUT_CPP}" "${bindings_OUTPUT_CS}"
        COMMAND "${DOTNET_EXECUTABLE}" "${CRUCIBLE_BINDGEN_DLL}" ${arguments}
        DEPENDS crucible_bindgen_tool "${CRUCIBLE_BINDGEN_DLL}" ${bindings_HEADERS}
        DEPFILE "${depfile}"
        COMMENT "Generating C#/C++ bindings (crucible-bindgen)"
        VERBATIM)
    add_custom_target(crucible_bindings DEPENDS "${bindings_OUTPUT_CPP}" "${bindings_OUTPUT_CS}")
endfunction()
