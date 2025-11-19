cmake_minimum_required(VERSION 3.15)

project(maxon-lsp-tests VERSION 0.1.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Include directories
include_directories(
    ${CMAKE_CURRENT_SOURCE_DIR}/../include
    ${CMAKE_CURRENT_SOURCE_DIR}/../../maxon-bin
)

# Source files from LSP server (needed for testing)
set(LSP_LIB_SOURCES
    ../src/json_rpc.cpp
    ../src/document_manager.cpp
    ../src/analyzer.cpp
    ../src/lsp_server.cpp
)

# Source files from Maxon compiler (needed for analyzer)
set(MAXON_SOURCES
    ../../maxon-bin/lexer.cpp
    ../../maxon-bin/parser.cpp
    ../../maxon-bin/semantic_analyzer.cpp
)

# Create a library from LSP sources for testing
add_library(lsp_lib STATIC ${LSP_LIB_SOURCES} ${MAXON_SOURCES})

# Compiler flags
target_compile_options(lsp_lib PRIVATE
    -Wall
    -Wno-unused-parameter
    -Wno-sign-compare
)

# Test executables
add_executable(test_lsp_types test_lsp_types.cpp)
target_link_libraries(test_lsp_types lsp_lib)

add_executable(test_document_manager test_document_manager.cpp)
target_link_libraries(test_document_manager lsp_lib)

add_executable(test_json_rpc test_json_rpc.cpp)
target_link_libraries(test_json_rpc lsp_lib)

add_executable(test_analyzer test_analyzer.cpp)
target_link_libraries(test_analyzer lsp_lib)

add_executable(test_lsp_server test_lsp_server.cpp)
target_link_libraries(test_lsp_server lsp_lib)

add_executable(test_code_actions test_code_actions.cpp)
target_link_libraries(test_code_actions lsp_lib)

add_executable(test_hover test_hover.cpp)
target_link_libraries(test_hover lsp_lib)

# Enable testing
enable_testing()

add_test(NAME LSPTypes COMMAND test_lsp_types)
add_test(NAME DocumentManager COMMAND test_document_manager)
add_test(NAME JsonRpc COMMAND test_json_rpc)
add_test(NAME Analyzer COMMAND test_analyzer)
add_test(NAME LSPServer COMMAND test_lsp_server)
add_test(NAME CodeActions COMMAND test_code_actions)
add_test(NAME Hover COMMAND test_hover)
