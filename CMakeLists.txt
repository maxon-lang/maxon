cmake_minimum_required(VERSION 3.13)
project(MaxonCompiler)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Look for pre-built LLVM in the llvm-build directory
set(LLVM_BUILD_DIR "${CMAKE_CURRENT_SOURCE_DIR}/llvm-build")
if(EXISTS "${LLVM_BUILD_DIR}/lib/cmake/llvm/LLVMConfig.cmake")
    message(STATUS "Using pre-built LLVM from llvm-build directory")
    set(LLVM_DIR "${LLVM_BUILD_DIR}/lib/cmake/llvm")
    find_package(LLVM REQUIRED CONFIG PATHS ${LLVM_DIR} NO_DEFAULT_PATH)
    message(STATUS "Found LLVM ${LLVM_PACKAGE_VERSION}")
    message(STATUS "LLVM include directories: ${LLVM_INCLUDE_DIRS}")
else()
    message(FATAL_ERROR "Pre-built LLVM not found in ${LLVM_BUILD_DIR}. Please run build-llvm.ps1 first.")
endif()

# Add LLVM definitions and include directories
include_directories(${LLVM_INCLUDE_DIRS})
separate_arguments(LLVM_DEFINITIONS_LIST NATIVE_COMMAND ${LLVM_DEFINITIONS})
add_definitions(${LLVM_DEFINITIONS_LIST})

# Source files
set(SOURCES
    main.cpp
    lexer.cpp
    parser.cpp
    codegen.cpp
)

# Create executable
add_executable(maxonc ${SOURCES})

# Link LLVM libraries
llvm_map_components_to_libnames(llvm_libs 
    Core
    Support
    native
    irreader
    codegen
    mc
    mcparser
    option
    bitwriter
    target
    x86codegen
    x86asmparser
    x86desc
    x86info
)

target_link_libraries(maxonc ${llvm_libs})

# Set output directory
set_target_properties(maxonc PROPERTIES
    RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin
)

# Installation
install(TARGETS maxonc DESTINATION bin)
