# The M7 demonstration's test (ADR 0077), run by CTest as
# launcher.jit.demonstration:
#   cmake -DEDITOR=... -DENGINE=... -DREPOSITORY=... -DOUT=... -P compare_demonstration.cmake
# Runs the session headless in the editor on Vulkan; on Windows, runs it
# again on D3D12 and compares the captures. Fails on any failed check.

function(run_demonstration backend)
    set(folder "${OUT}/${backend}")
    file(REMOVE_RECURSE "${folder}")
    set(arguments --managed-dir "${ENGINE}" --headless --backend ${backend} --frames 100000
        --game-arg "check-demonstration=${folder}" --game-arg "demonstration-repo=${REPOSITORY}"
        --game-arg "settings=${OUT}/${backend}-settings.json" ${ARGN})
    execute_process(COMMAND "${EDITOR}" ${arguments}
        OUTPUT_VARIABLE output ERROR_VARIABLE output RESULT_VARIABLE result)
    message("${output}")
    if(NOT output MATCHES "DemonstrationChecks: all [0-9]+ checks passed")
        message(FATAL_ERROR "The demonstration failed on ${backend}.")
    endif()
endfunction()

file(MAKE_DIRECTORY "${OUT}")
run_demonstration(vulkan)
if(WIN32)
    run_demonstration(d3d12 --game-arg "compare=${OUT}/vulkan")
endif()
