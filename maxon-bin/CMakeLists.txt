cmake_minimum_required(VERSION 3.13)
project(MaxonCompiler)

# Enable ccache if available for faster rebuilds
find_program(CCACHE_PROGRAM ccache)
if(CCACHE_PROGRAM)
    message(STATUS "Using ccache for faster rebuilds")
    set(CMAKE_C_COMPILER_LAUNCHER "${CCACHE_PROGRAM}")
    set(CMAKE_CXX_COMPILER_LAUNCHER "${CCACHE_PROGRAM}")
endif()

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Enable parallel compilation and optimization flags
add_compile_options(
	-Wall
	-Werror
	-Wno-unused-parameter
	-Wno-sign-compare
)

# Use precompiled headers for faster compilation
if(CMAKE_VERSION VERSION_GREATER_EQUAL 3.16)
    set(CMAKE_PCH_INSTANTIATE_TEMPLATES ON)
endif()

# Look for pre-built LLVM in C:/Users/Eric/Dev/llvm-project/build directory
set(LLVM_BUILD_DIR "C:/Users/Eric/Dev/llvm-project/build")
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
# Use SYSTEM to suppress warnings from LLVM headers
include_directories(SYSTEM ${LLVM_INCLUDE_DIRS})
separate_arguments(LLVM_DEFINITIONS_LIST NATIVE_COMMAND ${LLVM_DEFINITIONS})
add_definitions(${LLVM_DEFINITIONS_LIST})

# Source files
set(SOURCES
    main.cpp
    lexer.cpp
    parser.cpp
    codegen.cpp
    semantic_analyzer.cpp
    error_formatter.cpp
)

# Create executable
add_executable(maxon ${SOURCES})

# Use precompiled headers for faster compilation
if(CMAKE_VERSION VERSION_GREATER_EQUAL 3.16)
    target_precompile_headers(maxon PRIVATE pch.h)
endif()

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
    linker
    lto
    object
    binaryformat
)

# Link LLD libraries (required)
set(LLD_DIR "${LLVM_BUILD_DIR}/lib/cmake/lld")
if(NOT EXISTS "${LLD_DIR}/LLDConfig.cmake")
    message(FATAL_ERROR "LLD not found. Please rebuild LLVM with LLD support.")
endif()

find_package(LLD REQUIRED CONFIG PATHS ${LLD_DIR} NO_DEFAULT_PATH)
message(STATUS "Found LLD - using library interface")
set(lld_libs lldCOFF lldCommon)

# Add LLD include directory
set(LLD_INCLUDE_DIR "C:/Users/Eric/Dev/llvm-project/lld/include")
target_include_directories(maxon SYSTEM PRIVATE ${LLD_INCLUDE_DIR})

target_link_libraries(maxon ${llvm_libs} ${lld_libs})

# Set output directory to project root bin for easier access and stdlib resolution
set_target_properties(maxon PROPERTIES
    RUNTIME_OUTPUT_DIRECTORY ${CMAKE_SOURCE_DIR}/bin
)

# Installation
install(TARGETS maxon DESTINATION bin)
