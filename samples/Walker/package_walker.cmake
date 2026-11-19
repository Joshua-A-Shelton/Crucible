# The Walker packaged (ADR 0100): the editor's --build makes the package, which
# is copied somewhere else and run as a player would, with no options and with
# no .NET to find but its own.
#
#   cmake -DEDITOR=<CrucibleEditor> -DENGINE=<engine assemblies>
#         -DPROJECT=<Walker.crproject> -DOUT=<folder> -P package_walker.cmake

foreach(variable EDITOR ENGINE PROJECT OUT)
    if(NOT ${variable})
        message(FATAL_ERROR "package_walker: ${variable} is required")
    endif()
endforeach()

file(REMOVE_RECURSE "${OUT}")
file(MAKE_DIRECTORY "${OUT}")
set(package "${OUT}/package")

execute_process(
    COMMAND "${EDITOR}" --managed-dir "${ENGINE}" --headless --frames 100000 --project "${PROJECT}"
        --build "${package}" --game-arg "settings=${OUT}/settings.json"
    OUTPUT_VARIABLE built ERROR_VARIABLE built RESULT_VARIABLE result)
message("${built}")
if(NOT result EQUAL 0 OR NOT built MATCHES "Built Walker: 8 assets")
    message(FATAL_ERROR "package_walker: the build failed (${result})")
endif()

if(WIN32)
    set(executable "Walker.exe")
    set(libraries "Crucible.Core.dll" "nethost.dll" "D3D12/D3D12Core.dll" "Data/hostfxr.dll" "Data/coreclr.dll")
else()
    set(executable "Walker")
    set(libraries "Crucible.Core.so" "Data/libhostfxr.so" "Data/libcoreclr.so")
endif()
foreach(file ${executable} ${libraries} "Data/Walker.dll" "Data/Walker.runtimeconfig.json" "Data/Crucible.Engine.dll"
        "Data/Game.crpak")
    if(NOT EXISTS "${package}/${file}")
        message(FATAL_ERROR "package_walker: the package has no ${file}")
    endif()
endforeach()

# ReadyToRun puts machine code beside the IL, so the engine's assembly grows.
file(SIZE "${ENGINE}/Crucible.Engine.dll" ilSize)
file(SIZE "${package}/Data/Crucible.Engine.dll" packagedSize)
math(EXPR grown "${packagedSize} * 2 / 3")
if(NOT grown GREATER ilSize)
    message(FATAL_ERROR "package_walker: the engine isn't ReadyToRun (${ilSize} bytes built, ${packagedSize} packaged)")
endif()

# Somewhere else, under a name with a space, with no .NET on the PATH and
# nothing pointing at one.
set(elsewhere "${OUT}/elsewhere/Walker Game")
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
foreach(expected "loaded Walker.dll" "reading 8 assets from Game.crpak"
        "opened 28fe2d80-33ef-4ee4-a409-cc43b935bcc6: 12 entities at the top" "ran 120 frames")
    string(FIND "${ran}" "${expected}" at)
    if(at EQUAL -1)
        message(FATAL_ERROR "package_walker: the packaged Walker didn't print \"${expected}\"")
    endif()
endforeach()
if(NOT result EQUAL 0 OR ran MATCHES "unhandled exception|not registered")
    message(FATAL_ERROR "package_walker: the packaged Walker failed (${result})")
endif()
message("package_walker: the Walker's package ran from elsewhere")
