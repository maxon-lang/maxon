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

# Look for LLVM in MAXON_LLVM_DIR CMake variable, LLVM_DIR env variable, or default to ./llvm-project
if(DEFINED MAXON_LLVM_DIR)
    set(LLVM_BUILD_DIR "${MAXON_LLVM_DIR}")
elseif(DEFINED ENV{LLVM_DIR})
    set(LLVM_BUILD_DIR "$ENV{LLVM_DIR}")
else()
    set(LLVM_BUILD_DIR "${CMAKE_SOURCE_DIR}/llvm-project")
endif()

if(EXISTS "${LLVM_BUILD_DIR}/lib/cmake/llvm/LLVMConfig.cmake")
    message(STATUS "Using LLVM from ${LLVM_BUILD_DIR}")
    set(LLVM_CMAKE_DIR "${LLVM_BUILD_DIR}/lib/cmake/llvm")
    # LLVM may reference LibXml2, create dummy target if not found
    if(NOT TARGET LibXml2::LibXml2)
        add_library(LibXml2::LibXml2 INTERFACE IMPORTED)
    endif()
    find_package(LLVM REQUIRED CONFIG PATHS ${LLVM_CMAKE_DIR} NO_DEFAULT_PATH)
    message(STATUS "Found LLVM ${LLVM_PACKAGE_VERSION}")
    message(STATUS "LLVM include directories: ${LLVM_INCLUDE_DIRS}")
else()
    message(FATAL_ERROR "LLVM not found in ${LLVM_BUILD_DIR}. Please run 'make llvm' first.")
endif()

# Add LLVM definitions and include directories
# Use SYSTEM to suppress warnings from LLVM headers
include_directories(SYSTEM ${LLVM_INCLUDE_DIRS})
separate_arguments(LLVM_DEFINITIONS_LIST NATIVE_COMMAND ${LLVM_DEFINITIONS})
add_definitions(${LLVM_DEFINITIONS_LIST})

# Source files
set(SOURCES
    main.cpp
    file_utils.cpp
    compiler.cpp
    test_utils.cpp
    test_regenerate.cpp
    test_runner.cpp
    temp_runner.cpp
    self_test.cpp
    lexer.cpp
    parser.cpp
    parser/parser_utils.cpp
    parser/parser_expr.cpp
    parser/parser_stmt.cpp
    parser/parser_decl.cpp
    codegen.cpp
    codegen/codegen_types.cpp
    codegen/codegen_expr.cpp
    codegen/codegen_stmt.cpp
    codegen/codegen_function.cpp
    codegen/codegen_stdlib.cpp
    codegen/codegen_utils.cpp
    codegen/codegen_profile.cpp
    codegen/codegen_output.cpp
    codegen/codegen_debug.cpp
    semantic_analyzer.cpp
    semantic_analyzer/semantic_analyzer_expr.cpp
    semantic_analyzer/semantic_analyzer_stmt.cpp
    error_formatter.cpp
    ../maxon-runtime/libxml2-stub.cpp
)

# Create executable
add_executable(maxon ${SOURCES})

# Create grammar generator executable
add_executable(grammar_generator grammar_generator.cpp lexer.cpp)

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

# Platform-specific LLD libraries
if(WIN32)
    set(lld_libs lldCOFF lldCommon)
else()
    set(lld_libs lldELF lldCommon)
endif()

# LLD include directory from LLVM build
set(LLD_INCLUDE_DIR "${LLVM_BUILD_DIR}/include")
target_include_directories(maxon SYSTEM PRIVATE ${LLD_INCLUDE_DIR})

target_link_libraries(maxon ${llvm_libs} ${lld_libs})

# Add LLD include directory to grammar_generator
target_include_directories(grammar_generator SYSTEM PRIVATE ${LLD_INCLUDE_DIR})

target_link_libraries(grammar_generator ${llvm_libs} ${lld_libs})

# Set output directory to project root bin for easier access and stdlib resolution
set_target_properties(maxon PROPERTIES
    RUNTIME_OUTPUT_DIRECTORY ${CMAKE_SOURCE_DIR}/bin
)

set_target_properties(grammar_generator PROPERTIES
    RUNTIME_OUTPUT_DIRECTORY ${CMAKE_SOURCE_DIR}/bin
)

# Installation
install(TARGETS maxon DESTINATION bin)
