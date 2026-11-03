# The M5 demonstration's test (ADR 0056), run by CTest as `samples.diorama`:
#   cmake -DLAUNCHER=... -DENGINE=... -DGAME=... -DPROJECT=... -DOUT=... -P run_diorama.cmake
# Renders the Diorama headless on Vulkan, capturing the frame; on Windows,
# renders it again on D3D12 and compares the two within ADR 0056's
# tolerance. Fails on any failed check.

function(run_diorama backend capture)
    set(arguments --managed-dir "${ENGINE}" --headless --backend ${backend} --game "${GAME}"
        --game-arg "project=${PROJECT}" --game-arg "capture=${OUT}/${capture}.png" ${ARGN})
    execute_process(COMMAND "${LAUNCHER}" ${arguments}
        OUTPUT_VARIABLE output ERROR_VARIABLE output RESULT_VARIABLE result)
    message("${output}")
    if(NOT result EQUAL 0 OR NOT output MATCHES "Diorama: all [0-9]+ checks passed")
        message(FATAL_ERROR "The Diorama failed on ${backend}.")
    endif()
endfunction()

file(MAKE_DIRECTORY "${OUT}")
run_diorama(vulkan vulkan)
if(WIN32)
    run_diorama(d3d12 d3d12 --game-arg "compare=${OUT}/vulkan.png")
endif()
