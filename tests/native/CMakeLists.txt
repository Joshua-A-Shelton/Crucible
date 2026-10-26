add_executable(crucible_native_tests
    Main.cpp
    CoreTests.cpp)

target_link_libraries(crucible_native_tests PRIVATE crucible::core doctest::doctest)

doctest_discover_tests(crucible_native_tests PROPERTIES LABELS native)
