cmake_minimum_required(VERSION 3.15)

project(maxon-lsp-tests VERSION 0.1.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Use Release configuration for LLVM libraries
set(CMAKE_BUILD_TYPE Release CACHE STRING "Build type" FORCE)

# Find LLVM (needed by lexer.cpp)
if(DEFINED MAXON_LLVM_DIR)
    set(LLVM_BUILD_DIR "${MAXON_LLVM_DIR}")
elseif(DEFINED ENV{LLVM_DIR})
    set(LLVM_BUILD_DIR "$ENV{LLVM_DIR}")
else()
    # Get to project root from tests directory (lsp-server/tests -> root)
    get_filename_component(PROJECT_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/../.." ABSOLUTE)
    set(LLVM_BUILD_DIR "${PROJECT_ROOT}/llvm-project")
endif()

if(EXISTS "${LLVM_BUILD_DIR}/lib/cmake/llvm/LLVMConfig.cmake")
    set(LLVM_CMAKE_DIR "${LLVM_BUILD_DIR}/lib/cmake/llvm")
    # LLVM may reference LibXml2, create dummy target if not found
    if(NOT TARGET LibXml2::LibXml2)
        add_library(LibXml2::LibXml2 INTERFACE IMPORTED)
    endif()
    find_package(LLVM REQUIRED CONFIG PATHS ${LLVM_CMAKE_DIR} NO_DEFAULT_PATH)
    message(STATUS "Found LLVM ${LLVM_PACKAGE_VERSION}")
else()
    message(FATAL_ERROR "LLVM not found in ${LLVM_BUILD_DIR}. Please ensure llvm-project exists.")
endif()

# Include directories
include_directories(
    ${CMAKE_CURRENT_SOURCE_DIR}/../include
    ${CMAKE_CURRENT_SOURCE_DIR}/../../maxon-bin
    ${LLVM_INCLUDE_DIRS}
)

# Add LLVM definitions
add_definitions(${LLVM_DEFINITIONS})

# Source files from LSP server (needed for testing)
set(LSP_LIB_SOURCES
    ../src/json_rpc.cpp
    ../src/document_manager.cpp
    ../src/analyzer.cpp
    ../src/formatter.cpp
    ../src/lsp_server.cpp
)

# Source files from Maxon compiler (needed for analyzer)
set(MAXON_SOURCES
    ../../maxon-bin/lexer.cpp
    ../../maxon-bin/parser.cpp
    ../../maxon-bin/parser/parser_utils.cpp
    ../../maxon-bin/parser/parser_expr.cpp
    ../../maxon-bin/parser/parser_stmt.cpp
    ../../maxon-bin/parser/parser_decl.cpp
    ../../maxon-bin/semantic_analyzer.cpp
    ../../maxon-bin/semantic_analyzer/semantic_analyzer_expr.cpp
    ../../maxon-bin/semantic_analyzer/semantic_analyzer_stmt.cpp
    ../../maxon-bin/file_utils.cpp
)

# Create a library from LSP sources for testing
add_library(lsp_lib STATIC ${LSP_LIB_SOURCES} ${MAXON_SOURCES})

# Link LLVM libraries
llvm_map_components_to_libnames(llvm_libs core support)
target_link_libraries(lsp_lib ${llvm_libs})

# Compiler flags
target_compile_options(lsp_lib PRIVATE
    -Wall
    -Wno-unused-parameter
    -Wno-sign-compare
)

# Test executables
add_executable(test_lsp_types test_lsp_types.cpp catch_amalgamated.cpp)
target_link_libraries(test_lsp_types lsp_lib)

add_executable(test_document_manager test_document_manager.cpp catch_amalgamated.cpp)
target_link_libraries(test_document_manager lsp_lib)

add_executable(test_json_rpc test_json_rpc.cpp catch_amalgamated.cpp)
target_link_libraries(test_json_rpc lsp_lib)

add_executable(test_analyzer test_analyzer.cpp catch_amalgamated.cpp)
target_link_libraries(test_analyzer lsp_lib)

add_executable(test_lsp_server test_lsp_server.cpp catch_amalgamated.cpp)
target_link_libraries(test_lsp_server lsp_lib)

add_executable(test_code_actions test_code_actions.cpp catch_amalgamated.cpp)
target_link_libraries(test_code_actions lsp_lib)

add_executable(test_hover test_hover.cpp catch_amalgamated.cpp)
target_link_libraries(test_hover lsp_lib)

add_executable(test_rename test_rename.cpp catch_amalgamated.cpp)
target_link_libraries(test_rename lsp_lib)

add_executable(test_formatter test_formatter.cpp catch_amalgamated.cpp)
target_link_libraries(test_formatter lsp_lib)

# Enable testing
enable_testing()

add_test(NAME LSPTypes COMMAND test_lsp_types)
add_test(NAME DocumentManager COMMAND test_document_manager)
add_test(NAME JsonRpc COMMAND test_json_rpc)
add_test(NAME Analyzer COMMAND test_analyzer)
add_test(NAME LSPServer COMMAND test_lsp_server)
add_test(NAME CodeActions COMMAND test_code_actions)
add_test(NAME Hover COMMAND test_hover)
add_test(NAME Rename COMMAND test_rename)
add_test(NAME Formatter COMMAND test_formatter)
