# A sample warmed up for the tests (ADR 0110), run by CTest as a fixture's
# setup (samples.walker-warm, samples.diorama-warm): the sample copied
# without its Cache or packages, opened (everything imported, its code
# built), then opened again, when nothing imports and the build is skipped
# as up to date. The checks then copy this warm copy, Cache and all.
#
#   cmake -DEDITOR=<CrucibleEditor> -DENGINE=<engine assemblies>
#         -DPROJECT=<sample.crproject> -DGAME=<its assembly's name>
#         -DOUT=<warm folder> -P warm_project.cmake

foreach(variable EDITOR ENGINE PROJECT GAME OUT)
    if(NOT ${variable})
        message(FATAL_ERROR "warm_project: ${variable} is required")
    endif()
endforeach()

get_filename_component(projectFolder "${PROJECT}" DIRECTORY)
get_filename_component(projectFile "${PROJECT}" NAME)
file(REMOVE_RECURSE "${OUT}")
file(COPY "${projectFolder}/" DESTINATION "${OUT}")
file(REMOVE_RECURSE "${OUT}/Cache" "${OUT}/Build")

function(open_warm run)
    execute_process(
        COMMAND "${EDITOR}" --managed-dir "${ENGINE}" --headless --frames 5 --project "${OUT}/${projectFile}"
            --game-arg "settings=${OUT}-settings.json"
        OUTPUT_VARIABLE opened ERROR_VARIABLE opened RESULT_VARIABLE result)
    message("${opened}")
    if(NOT result EQUAL 0)
        message(FATAL_ERROR "warm_project: the ${run} open failed (${result})")
    endif()
    set(opened "${opened}" PARENT_SCOPE)
endfunction()

# "Building Walker…", not "Building Walker.Core…" (its C++ module): the
# ellipsis is a "." in a Windows console's code page.
set(building "Building ${GAME}(…|\\.)\r?\n")

# Cold: everything imports, and the game builds and loads.
open_warm(first)
if(NOT opened MATCHES "[0-9]+ assets, [1-9][0-9]* imported")
    message(FATAL_ERROR "warm_project: the first open imported nothing")
endif()
if(NOT opened MATCHES "${building}" OR NOT opened MATCHES "Loaded ([A-Za-z0-9_.]+, )*${GAME}\\.dll")
    message(FATAL_ERROR "warm_project: the first open didn't build and load ${GAME}.dll")
endif()

# Warm: nothing imports, and the build is skipped.
open_warm(second)
if(NOT opened MATCHES "[0-9]+ assets, 0 imported")
    message(FATAL_ERROR "warm_project: the second open imported again")
endif()
if(opened MATCHES "${building}" OR NOT opened MATCHES "${GAME}\\.dll is up to date" OR NOT opened MATCHES "Loaded ([A-Za-z0-9_.]+, )*${GAME}\\.dll")
    message(FATAL_ERROR "warm_project: the second open didn't skip ${GAME}'s build as up to date")
endif()
message("warm_project: ${GAME} warm in ${OUT}")
