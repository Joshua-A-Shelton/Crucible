# Compiler settings for every Crucible C++ target, matching Slag's: warnings
# on, and no exceptions or RTTI (ADR 0030).

# crucible_set_compile_options(<target>)
function(crucible_set_compile_options target)
    target_compile_features(${target} PUBLIC cxx_std_20)
    if(MSVC)
        target_compile_options(${target} PRIVATE /W4 /permissive- /EHs-c- /GR-)
        target_compile_definitions(${target} PRIVATE _HAS_EXCEPTIONS=0)
    else()
        target_compile_options(${target} PRIVATE -Wall -Wextra -Wpedantic -Wno-missing-field-initializers
                                                 -fno-exceptions -fno-rtti)
    endif()
endfunction()
