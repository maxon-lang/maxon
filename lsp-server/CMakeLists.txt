cmake_minimum_required(VERSION 3.15)

project(maxon-lsp-server VERSION 0.1.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Look for pre-built LLVM (same as main compiler)
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
include_directories(SYSTEM ${LLVM_INCLUDE_DIRS})
separate_arguments(LLVM_DEFINITIONS_LIST NATIVE_COMMAND ${LLVM_DEFINITIONS})
add_definitions(${LLVM_DEFINITIONS_LIST})

# Include directories
include_directories(
    ${CMAKE_CURRENT_SOURCE_DIR}/../maxon-bin
    ${CMAKE_CURRENT_SOURCE_DIR}/include
)

# Source files
set(MAXON_SOURCES
    ../maxon-bin/lexer.cpp
    ../maxon-bin/parser.cpp
    ../maxon-bin/semantic_analyzer.cpp
    ../maxon-bin/semantic_analyzer/semantic_analyzer_expr.cpp
    ../maxon-bin/semantic_analyzer/semantic_analyzer_stmt.cpp
)

set(LSP_SOURCES
    src/main.cpp
    src/lsp_server.cpp
    src/json_rpc.cpp
    src/document_manager.cpp
    src/analyzer.cpp
)

# Create executable
add_executable(maxon-lsp-server ${LSP_SOURCES} ${MAXON_SOURCES})

# Link LLVM libraries (minimal set needed for lexer)
llvm_map_components_to_libnames(llvm_libs 
    Core
    Support
)

# Link LLD libraries (required by lexer headers)
set(LLD_DIR "${LLVM_BUILD_DIR}/lib/cmake/lld")
if(NOT EXISTS "${LLD_DIR}/LLDConfig.cmake")
    message(FATAL_ERROR "LLD not found. Please rebuild LLVM with LLD support.")
endif()

find_package(LLD REQUIRED CONFIG PATHS ${LLD_DIR} NO_DEFAULT_PATH)
set(lld_libs lldCommon)

# Add LLD include directory
set(LLD_INCLUDE_DIR "C:/Users/Eric/Dev/llvm-project/lld/include")
target_include_directories(maxon-lsp-server SYSTEM PRIVATE ${LLD_INCLUDE_DIR})

target_link_libraries(maxon-lsp-server ${llvm_libs} ${lld_libs})

# Compiler flags for Clang
target_compile_options(maxon-lsp-server PRIVATE
    -Wall
    -Wno-unused-parameter
    -Wno-sign-compare
)

# Set output directory to project root bin for easier access
set_target_properties(maxon-lsp-server PROPERTIES
    RUNTIME_OUTPUT_DIRECTORY ${CMAKE_SOURCE_DIR}/bin
)

set(LSP_SERVER_RUNTIME "${CMAKE_SOURCE_DIR}/bin/maxon-lsp-server${CMAKE_EXECUTABLE_SUFFIX}")
set(LSP_SERVER_BACKUP "${LSP_SERVER_RUNTIME}.old")

add_custom_command(TARGET maxon-lsp-server PRE_BUILD
    COMMAND ${CMAKE_COMMAND}
        -DOUTPUT_PATH="${LSP_SERVER_RUNTIME}"
        -DBACKUP_PATH="${LSP_SERVER_BACKUP}"
        -P ${CMAKE_CURRENT_SOURCE_DIR}/rename_old_maxon_lsp_server.cmake
    COMMENT "Renaming existing maxon-lsp-server binary before rebuild"
)

add_custom_command(TARGET maxon-lsp-server POST_BUILD
    COMMAND ${CMAKE_COMMAND}
        -DOUTPUT_PATH="${LSP_SERVER_RUNTIME}"
        -DBACKUP_PATH="${LSP_SERVER_BACKUP}"
        -P ${CMAKE_CURRENT_SOURCE_DIR}/finalize_maxon_lsp_server.cmake
    COMMENT "Killing running process and cleaning up old binary after rebuild"
)

# Installation
install(TARGETS maxon-lsp-server DESTINATION bin)

# Add tests subdirectory
add_subdirectory(tests)
