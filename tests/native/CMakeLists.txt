add_executable(crucible_native_tests
    Main.cpp
    CoreTests.cpp
    ResultTests.cpp)

crucible_set_compile_options(crucible_native_tests)
target_link_libraries(crucible_native_tests PRIVATE crucible::core_static doctest::doctest)

doctest_discover_tests(crucible_native_tests PROPERTIES LABELS native)
