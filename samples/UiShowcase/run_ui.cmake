# The M6 demonstration's test (ADR 0064), run by CTest as `samples.ui`:
#   cmake -DLAUNCHER=... -DENGINE=... -DGAME=... -DPROJECT=... -DOUT=... -P run_ui.cmake
# Runs the UI showcase headless on Vulkan, capturing the menu at each DPI
# scale; on Windows, runs it again on D3D12 and compares the captures.
# Fails on any failed check.

function(run_ui backend)
    set(arguments --managed-dir "${ENGINE}" --headless --backend ${backend} --frames 100000 --game "${GAME}"
        --game-arg "project=${PROJECT}" --game-arg "capture=${OUT}/${backend}" ${ARGN})
    execute_process(COMMAND "${LAUNCHER}" ${arguments}
        OUTPUT_VARIABLE output ERROR_VARIABLE output RESULT_VARIABLE result)
    message("${output}")
    if(NOT result EQUAL 0 OR NOT output MATCHES "UiShowcase: all [0-9]+ checks passed")
        message(FATAL_ERROR "The UI showcase failed on ${backend}.")
    endif()
endfunction()

file(MAKE_DIRECTORY "${OUT}")
run_ui(vulkan)
if(WIN32)
    run_ui(d3d12 --game-arg "compare=${OUT}/vulkan")
endif()
