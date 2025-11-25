cmake_minimum_required(VERSION 3.15)

project(maxon-backend-tests VERSION 0.1.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Use Release configuration
set(CMAKE_BUILD_TYPE Release CACHE STRING "Build type")

# Get project root
get_filename_component(PROJECT_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/../.." ABSOLUTE)

# Include directories
include_directories(
    ${CMAKE_CURRENT_SOURCE_DIR}/..
    ${PROJECT_ROOT}/lsp-server/tests  # For catch_amalgamated.hpp
)

# Catch2 source (use from lsp-server/tests)
set(CATCH_SOURCE ${PROJECT_ROOT}/lsp-server/tests/catch_amalgamated.cpp)

# Backend source files
set(BACKEND_SOURCES
    ../mir/mir.cpp
    ../mir/mir_builder.cpp
    ../mir/optimizer.cpp
    ../backend/x86_encoding.cpp
    ../backend/x86_codegen.cpp
    ../backend/regalloc.cpp
    ../backend/elf_writer.cpp
    ../backend/pe_writer.cpp
    ../backend/dwarf.cpp
)

# Create a library from backend sources
add_library(backend_lib STATIC ${BACKEND_SOURCES})

# Compiler flags - warnings as errors
if(MSVC)
    target_compile_options(backend_lib PRIVATE /W3 /WX /wd4267 /wd4244)
else()
    target_compile_options(backend_lib PRIVATE -Wall -Werror -Wno-unused-parameter -Wno-sign-compare -Wno-unused-variable -Wno-unused-but-set-variable -Wno-deprecated-declarations -Wno-unused-private-field)
endif()

# Common test compile options (includes Catch2 warning suppressions)
if(MSVC)
    set(TEST_COMPILE_OPTIONS /W3 /WX /wd4267 /wd4244)
else()
    set(TEST_COMPILE_OPTIONS -Wall -Werror -Wno-unused-parameter -Wno-sign-compare -Wno-deprecated-literal-operator)
endif()

# Test executables
add_executable(test_mir test_mir.cpp ${CATCH_SOURCE})
target_link_libraries(test_mir backend_lib)
target_compile_options(test_mir PRIVATE ${TEST_COMPILE_OPTIONS})

add_executable(test_x86_encoding test_x86_encoding.cpp ${CATCH_SOURCE})
target_link_libraries(test_x86_encoding backend_lib)
target_compile_options(test_x86_encoding PRIVATE ${TEST_COMPILE_OPTIONS})

add_executable(test_regalloc test_regalloc.cpp ${CATCH_SOURCE})
target_link_libraries(test_regalloc backend_lib)
target_compile_options(test_regalloc PRIVATE ${TEST_COMPILE_OPTIONS})

add_executable(test_executable_writers test_executable_writers.cpp ${CATCH_SOURCE})
target_link_libraries(test_executable_writers backend_lib)
target_compile_options(test_executable_writers PRIVATE ${TEST_COMPILE_OPTIONS})

add_executable(test_dwarf test_dwarf.cpp ${CATCH_SOURCE})
target_link_libraries(test_dwarf backend_lib)
target_compile_options(test_dwarf PRIVATE ${TEST_COMPILE_OPTIONS})

add_executable(test_end_to_end test_end_to_end.cpp ${CATCH_SOURCE})
target_link_libraries(test_end_to_end backend_lib)
target_compile_options(test_end_to_end PRIVATE ${TEST_COMPILE_OPTIONS})

add_executable(test_optimizer test_optimizer.cpp ${CATCH_SOURCE})
target_link_libraries(test_optimizer backend_lib)
target_compile_options(test_optimizer PRIVATE ${TEST_COMPILE_OPTIONS})

# Combined test runner
add_executable(run_all_backend_tests
    test_mir.cpp
    test_x86_encoding.cpp
    test_regalloc.cpp
    test_executable_writers.cpp
    test_dwarf.cpp
    test_optimizer.cpp
    ${CATCH_SOURCE}
)
target_link_libraries(run_all_backend_tests backend_lib)
target_compile_options(run_all_backend_tests PRIVATE ${TEST_COMPILE_OPTIONS})

# Enable testing
enable_testing()

add_test(NAME MIR COMMAND test_mir)
add_test(NAME X86Encoding COMMAND test_x86_encoding)
add_test(NAME RegisterAllocation COMMAND test_regalloc)
add_test(NAME ExecutableWriters COMMAND test_executable_writers)
add_test(NAME DWARF COMMAND test_dwarf)
add_test(NAME Optimizer COMMAND test_optimizer)
add_test(NAME AllBackendTests COMMAND run_all_backend_tests)
