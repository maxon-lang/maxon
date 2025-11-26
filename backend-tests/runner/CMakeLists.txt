cmake_minimum_required(VERSION 3.15)

project(backend-test-runner VERSION 0.1.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Find LLVM (for llvm-objdump path)
if(DEFINED MAXON_LLVM_DIR)
    set(LLVM_DIR_ABS ${MAXON_LLVM_DIR})
else()
    set(LLVM_DIR_ABS "${CMAKE_SOURCE_DIR}/../../llvm-project")
endif()

add_executable(backend-test-runner
    main.cpp
)

# Pass LLVM directory as compile definition
target_compile_definitions(backend-test-runner PRIVATE
    LLVM_BIN_DIR="${LLVM_DIR_ABS}/bin"
)

# Install to bin directory
install(TARGETS backend-test-runner DESTINATION bin)
