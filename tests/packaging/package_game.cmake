# A game packaged (ADR 0100, 0101): a copy of its project set to build the
# scene with the runtime given, made by the editor's --build, then copied
# somewhere else and run as a player would, with no options and with no .NET
# to find but its own (JIT) or none at all (NativeAOT).
#
#   cmake -DEDITOR=<CrucibleEditor> -DENGINE=<engine assemblies>
#         -DPROJECT=<game.crproject> -DGAME=<name> -DSCENE=<scene ID>
#         -DASSETS=<assets cooked> -DENTITIES=<entities at the top>
#         -DRUNTIME=Jit|NativeAot -DOUT=<folder> -P package_game.cmake

foreach(variable EDITOR ENGINE PROJECT GAME SCENE ASSETS ENTITIES RUNTIME OUT)
    if(NOT ${variable})
        message(FATAL_ERROR "package_game: ${variable} is required")
    endif()
endforeach()

file(REMOVE_RECURSE "${OUT}")
file(MAKE_DIRECTORY "${OUT}")

# The project copied without its builds, its build settings set.
get_filename_component(projectFolder "${PROJECT}" DIRECTORY)
get_filename_component(projectFile "${PROJECT}" NAME)
file(COPY "${projectFolder}/" DESTINATION "${OUT}/project")
file(REMOVE_RECURSE "${OUT}/project/Build" "${OUT}/project/Cache/Build")
file(READ "${OUT}/project/${projectFile}" settings)
string(JSON settings SET "${settings}" Build "{\"Scenes\": [\"${SCENE}\"], \"Include\": [], \"Runtime\": \"${RUNTIME}\"}")
file(WRITE "${OUT}/project/${projectFile}" "${settings}")

set(package "${OUT}/package")
execute_process(
    COMMAND "${EDITOR}" --managed-dir "${ENGINE}" --headless --frames 100000 --project "${OUT}/project/${projectFile}"
        --build "${package}" --game-arg "settings=${OUT}/settings.json"
    OUTPUT_VARIABLE built ERROR_VARIABLE built RESULT_VARIABLE result)
message("${built}")
if(NOT result EQUAL 0 OR NOT built MATCHES "Built ${GAME}: ${ASSETS} assets")
    message(FATAL_ERROR "package_game: the build failed (${result})")
endif()

if(WIN32)
    set(executable "${GAME}.exe")
else()
    set(executable "${GAME}")
endif()
set(present "${executable}" "Data/Game.crpak")
set(absent "")
if(RUNTIME STREQUAL "Jit")
    if(WIN32)
        list(APPEND present "Crucible.Core.dll" "nethost.dll" "D3D12/D3D12Core.dll" "Data/hostfxr.dll" "Data/coreclr.dll")
    else()
        list(APPEND present "Crucible.Core.so" "Data/libhostfxr.so" "Data/libcoreclr.so")
    endif()
    list(APPEND present "Data/${GAME}.dll" "Data/${GAME}.runtimeconfig.json" "Data/Crucible.Engine.dll")
else()
    # One executable: no core beside it, no assemblies, no runtime, and its
    # debug information left in the project's Cache.
    if(WIN32)
        list(APPEND present "D3D12/D3D12Core.dll")
        list(APPEND absent "Crucible.Core.dll" "nethost.dll" "${GAME}.pdb")
    else()
        list(APPEND absent "Crucible.Core.so" "${GAME}.debug")
    endif()
    list(APPEND absent "Data/${GAME}.dll" "Data/Crucible.Engine.dll")
endif()
foreach(file ${present})
    if(NOT EXISTS "${package}/${file}")
        message(FATAL_ERROR "package_game: the package has no ${file}")
    endif()
endforeach()
foreach(file ${absent})
    if(EXISTS "${package}/${file}")
        message(FATAL_ERROR "package_game: the package has ${file}, which it shouldn't")
    endif()
endforeach()

# On Windows a game opens no console: its executable is a GUI program (the
# PE header's Subsystem, 2) while the editor's launcher is a console one (3).
if(WIN32)
    file(READ "${package}/${executable}" lfanew OFFSET 60 LIMIT 4 HEX)
    string(REGEX REPLACE "^(..)(..)(..)(..)$" "\\4\\3\\2\\1" lfanew "${lfanew}")
    math(EXPR subsystemAt "0x${lfanew} + 92")
    file(READ "${package}/${executable}" subsystem OFFSET ${subsystemAt} LIMIT 2 HEX)
    if(NOT subsystem STREQUAL "0200")
        message(FATAL_ERROR "package_game: ${executable} isn't a GUI program (Subsystem ${subsystem})")
    endif()
endif()

if(RUNTIME STREQUAL "Jit")
    # ReadyToRun puts machine code beside the IL, so the engine's assembly grows.
    file(SIZE "${ENGINE}/Crucible.Engine.dll" ilSize)
    file(SIZE "${package}/Data/Crucible.Engine.dll" packagedSize)
    math(EXPR grown "${packagedSize} * 2 / 3")
    if(NOT grown GREATER ilSize)
        message(FATAL_ERROR "package_game: the engine isn't ReadyToRun (${ilSize} bytes built, ${packagedSize} packaged)")
    endif()
    set(loaded "loaded ${GAME}.dll")
else()
    set(loaded "${GAME} compiled in")
endif()

# Somewhere else, under a name with a space, with no .NET on the PATH and
# nothing pointing at one.
set(elsewhere "${OUT}/elsewhere/${GAME} Game")
file(COPY "${package}/" DESTINATION "${elsewhere}")
if(WIN32)
    set(path "C:\\Windows\\System32;C:\\Windows")
else()
    set(path "/usr/bin:/bin")
endif()
execute_process(
    COMMAND "${CMAKE_COMMAND}" -E env --unset=DOTNET_ROOT --unset=LD_LIBRARY_PATH "PATH=${path}"
        "${elsewhere}/${executable}" --headless --frames 120
    WORKING_DIRECTORY "${OUT}"
    OUTPUT_VARIABLE ran ERROR_VARIABLE ran RESULT_VARIABLE result)
message("${ran}")
foreach(expected "${loaded}" "reading ${ASSETS} assets from Game.crpak"
        "opened ${SCENE}: ${ENTITIES} entities at the top" "ran 120 frames")
    string(FIND "${ran}" "${expected}" at)
    if(at EQUAL -1)
        message(FATAL_ERROR "package_game: the packaged ${GAME} didn't print \"${expected}\"")
    endif()
endforeach()
# A component nothing registered means the game, or its C++ module, didn't start.
if(NOT result EQUAL 0 OR ran MATCHES "unhandled exception|nothing registered")
    message(FATAL_ERROR "package_game: the packaged ${GAME} failed (${result})")
endif()
message("package_game: the ${GAME} package (${RUNTIME}) ran from elsewhere")
