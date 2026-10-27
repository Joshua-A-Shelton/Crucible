# Test names become CTest names and filters (doctest_discover_tests): a ';'
# splits the name, so the filter matches nothing and the test "passes"
# without running, and a ',' makes doctest's label lookup miss, so the test
# loses its suite label. Run as a test: cmake -DTEST_DIR=<dir> -P this file.

file(GLOB_RECURSE sources "${TEST_DIR}/*.cpp")
set(violations "")
foreach(source IN LISTS sources)
    file(STRINGS "${source}" cases REGEX "TEST_CASE\\(\"[^\"]*[,;][^\"]*\"")
    foreach(line IN LISTS cases)
        file(RELATIVE_PATH relative "${TEST_DIR}" "${source}")
        string(STRIP "${line}" line)
        string(APPEND violations "  ${relative}: ${line}\n")
    endforeach()
endforeach()

if(violations)
    message(FATAL_ERROR "Test names may not contain ',' or ';':\n${violations}")
endif()
message(STATUS "test names: ok")
