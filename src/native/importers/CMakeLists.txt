# The importers (ADR 0042): native code the editor's asset pipeline calls to
# turn source files into imported data. Editor-only, like Crucible.Assets:
# shipped games never carry it. Two forms, like the core:
#   crucible_importers         shared, beside the shared core; C# binds to it by name
#   crucible_importers_static  static, for the native tests

# bc7enc_rdo's sources, as a third-party library: its own warnings are its own.
add_library(crucible_bc7enc STATIC
    "${bc7enc_rdo_SOURCE_DIR}/bc7enc.cpp"
    "${bc7enc_rdo_SOURCE_DIR}/bc7decomp.cpp"
    "${bc7enc_rdo_SOURCE_DIR}/rgbcx.cpp"
    "${bc7enc_rdo_SOURCE_DIR}/ert.cpp")
target_include_directories(crucible_bc7enc SYSTEM PUBLIC "${bc7enc_rdo_SOURCE_DIR}")
target_compile_features(crucible_bc7enc PUBLIC cxx_std_20)
set_target_properties(crucible_bc7enc PROPERTIES POSITION_INDEPENDENT_CODE ON)
# ert.h uses uint8_t without including <cstdint>, which newer GCC no longer
# pulls in through other headers: force it in rather than patch their code.
if(MSVC)
    target_compile_options(crucible_bc7enc PRIVATE /W0 /FIcstdint)
else()
    # ERT's hash reads uint16_t words at odd addresses (ert.cpp), undefined
    # behaviour that x64 and ARM64 tolerate. Sanitizer builds skip alignment
    # checks here only, so they keep checking everything else; elsewhere the
    # flag does nothing.
    target_compile_options(crucible_bc7enc PRIVATE -w -include cstdint -fno-sanitize=alignment)
endif()

# meshoptimizer and MikkTSpace (ADR 0043), likewise.
file(GLOB CRUCIBLE_MESHOPT_SOURCES "${meshoptimizer_SOURCE_DIR}/src/*.cpp")
add_library(crucible_meshopt STATIC ${CRUCIBLE_MESHOPT_SOURCES})
target_include_directories(crucible_meshopt SYSTEM PUBLIC "${meshoptimizer_SOURCE_DIR}/src")
add_library(crucible_mikktspace STATIC "${mikktspace_SOURCE_DIR}/mikktspace.c")
target_include_directories(crucible_mikktspace SYSTEM PUBLIC "${mikktspace_SOURCE_DIR}")
# ufbx (ADR 0044) as C. It can't open files: Crucible hands it bytes.
add_library(crucible_ufbx STATIC "${ufbx_SOURCE_DIR}/ufbx.c")
target_include_directories(crucible_ufbx SYSTEM PUBLIC "${ufbx_SOURCE_DIR}")
target_compile_definitions(crucible_ufbx PUBLIC UFBX_NO_STDIO)
foreach(thirdParty crucible_meshopt crucible_mikktspace crucible_ufbx)
    set_target_properties(${thirdParty} PROPERTIES POSITION_INDEPENDENT_CODE ON)
    if(MSVC)
        target_compile_options(${thirdParty} PRIVATE /W0)
    else()
        target_compile_options(${thirdParty} PRIVATE -w)
    endif()
endforeach()

add_library(crucible_importers_objects OBJECT
    CgltfImplementation.cpp
    Common.cpp
    Fbx.cpp
    Gltf.cpp
    Mesh.cpp
    ShaderCompiler.cpp
    StbImage.cpp
    Texture.cpp
    "${CRUCIBLE_GENERATED_DIR}/native/ImportersBindings.g.cpp")
crucible_set_compile_options(crucible_importers_objects)
target_include_directories(crucible_importers_objects PUBLIC "${CMAKE_CURRENT_SOURCE_DIR}/include")
target_include_directories(crucible_importers_objects SYSTEM PRIVATE "${stb_SOURCE_DIR}" "${cgltf_SOURCE_DIR}")
target_compile_definitions(crucible_importers_objects PRIVATE CRUCIBLE_IMPORTERS_EXPORTS)
set_target_properties(crucible_importers_objects PROPERTIES
    POSITION_INDEPENDENT_CODE ON
    CXX_VISIBILITY_PRESET hidden
    VISIBILITY_INLINES_HIDDEN ON)
target_link_libraries(crucible_importers_objects PRIVATE crucible_bc7enc crucible_meshopt crucible_mikktspace crucible_ufbx)
# Slang (ADR 0047), from its release binaries: a shared library the
# importers load, with DXC beside it on Windows for DXIL.
target_link_libraries(crucible_importers_objects PUBLIC slang::slang)
if(WIN32)
    # Slang's compiler and the modules it loads (not slang-llvm, for CPU
    # targets only), and DXC.
    set(CRUCIBLE_SHADER_COMPILER_DLLS
        "${slang_SOURCE_DIR}/bin/slang.dll"
        "${slang_SOURCE_DIR}/bin/slang-compiler.dll"
        "${slang_SOURCE_DIR}/bin/slang-glslang.dll"
        "${slang_SOURCE_DIR}/bin/slang-glsl-module.dll"
        "${slang_SOURCE_DIR}/bin/slang-rt.dll"
        "${dxc_SOURCE_DIR}/bin/x64/dxcompiler.dll"
        "${dxc_SOURCE_DIR}/bin/x64/dxil.dll")
endif()
# The core's headers; which form of the core is linked is up to each form below.
target_include_directories(crucible_importers_objects PUBLIC
    $<TARGET_PROPERTY:crucible_core_objects,INTERFACE_INCLUDE_DIRECTORIES>)

file(GLOB CRUCIBLE_IMPORTERS_HEADERS CONFIGURE_DEPENDS "${CMAKE_CURRENT_SOURCE_DIR}/include/crucible/importers/*.h")
crucible_generate_bindings(
    TARGET crucible_importers_bindings
    OUTPUT_CPP "${CRUCIBLE_GENERATED_DIR}/native/ImportersBindings.g.cpp"
    OUTPUT_CS "${CRUCIBLE_GENERATED_DIR}/managed/ImportersBindings.g.cs"
    INCLUDE_DIRECTORIES "${CMAKE_CURRENT_SOURCE_DIR}/include" "${PROJECT_SOURCE_DIR}/src/native/core/include"
    HEADERS ${CRUCIBLE_IMPORTERS_HEADERS}
    # The core's types (Status, Result) are the engine's in C#.
    EXTERN_HEADERS "${PROJECT_SOURCE_DIR}/src/native/core/include/crucible/Result.h"
        "${PROJECT_SOURCE_DIR}/src/native/core/include/crucible/MeshFile.h"
        "${PROJECT_SOURCE_DIR}/src/native/core/include/crucible/TextureFile.h"
    LIBRARY crucible_importers
    INTEROP_NAMESPACE Crucible.Assets.Interop
    EXPORT_MACRO CRUCIBLE_IMPORTERS_EXPORT
    EXPORT_HEADER crucible/importers/ImportersExport.h)

add_library(crucible_importers SHARED)
target_link_libraries(crucible_importers PUBLIC crucible_importers_objects PRIVATE crucible_core)
if(WIN32)
    # Beside the importers, where C# tests and the editor find them.
    add_custom_command(TARGET crucible_importers POST_BUILD
        COMMAND "${CMAKE_COMMAND}" -E copy_if_different ${CRUCIBLE_SHADER_COMPILER_DLLS} "$<TARGET_FILE_DIR:crucible_core>")
endif()
# Beside the shared core, so whatever finds one finds the other.
set_target_properties(crucible_importers PROPERTIES
    LIBRARY_OUTPUT_DIRECTORY "$<TARGET_FILE_DIR:crucible_core>"
    RUNTIME_OUTPUT_DIRECTORY "$<TARGET_FILE_DIR:crucible_core>")
if(NOT WIN32 AND NOT APPLE)
    target_link_options(crucible_importers PRIVATE "LINKER:--exclude-libs,ALL")
endif()

add_library(crucible_importers_static STATIC)
target_link_libraries(crucible_importers_static PUBLIC crucible_importers_objects crucible_core_static)

# shaderfax, the build's shader compiler (ADR 0047): the importers' compile
# path as a command-line tool, for the engine's own shaders.
# It links the core's objects without the renderer, which embeds what
# shaderfax compiles (ADR 0048's build order).
add_executable(shaderfax Shaderfax.cpp)
crucible_set_compile_options(shaderfax)
target_link_libraries(shaderfax PRIVATE crucible_importers_objects crucible_core_objects crucible_engine_shaders_stub)
if(WIN32)
    add_custom_command(TARGET shaderfax POST_BUILD
        COMMAND "${CMAKE_COMMAND}" -E copy_if_different ${CRUCIBLE_SHADER_COMPILER_DLLS} "$<TARGET_FILE_DIR:shaderfax>")
endif()
