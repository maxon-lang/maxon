cmake_minimum_required(VERSION 3.15)

project(maxon-lsp-tests VERSION 0.1.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Get project root
get_filename_component(PROJECT_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/../../.." ABSOLUTE)

# Include directories
include_directories(
    ${CMAKE_CURRENT_SOURCE_DIR}/..
    ${CMAKE_CURRENT_SOURCE_DIR}/../..
    ${PROJECT_ROOT}
    ${PROJECT_ROOT}/vendor
    ${PROJECT_ROOT}/vendor/catch2
)

# LSP source files
set(LSP_SOURCES
    ../lsp_server.cpp
    ../lsp_json.cpp
    ../json_rpc.cpp
    ../document_manager.cpp
    ../transport.cpp
    ../features/completion.cpp
    ../features/hover.cpp
    ../features/signature_help.cpp
    ../features/definition.cpp
    ../features/references.cpp
    ../features/document_symbols.cpp
    ../features/rename.cpp
    ../features/code_actions.cpp
    ../features/code_lens.cpp
    ../features/formatting.cpp
    ../features/folding.cpp
    ../features/diagnostics.cpp
    ../features/linked_editing.cpp
    ../features/semantic_tokens.cpp
)

# Maxon compiler source files (needed for analysis)
set(MAXON_SOURCES
    ../../lexer.cpp
    ../../parser.cpp
    ../../parser/parser_utils.cpp
    ../../parser/parser_expr.cpp
    ../../parser/parser_stmt.cpp
    ../../parser/parser_decl.cpp
    ../../semantic_analyzer.cpp
    ../../semantic_analyzer/semantic_analyzer_expr.cpp
    ../../semantic_analyzer/semantic_analyzer_stmt.cpp
    ../../semantic_analyzer/semantic_analyzer_mutation.cpp
    ../../types/type_conversion.cpp
    ../../file_utils.cpp
    ../../intrinsics.cpp
    ../../compiler_api.cpp
    ../../call_graph.cpp
)

# Create library from sources for testing
add_library(lsp_test_lib STATIC ${LSP_SOURCES} ${MAXON_SOURCES})

# Compiler flags
target_compile_options(lsp_test_lib PRIVATE
    -Wall
    -Wno-unused-parameter
    -Wno-sign-compare
    -mavx2
    -msse4.2
)

# Test executable
add_executable(test_lsp_server test_lsp_server.cpp ${PROJECT_ROOT}/vendor/catch2/catch_amalgamated.cpp)
target_link_libraries(test_lsp_server lsp_test_lib)

# Enable testing
enable_testing()

# Run with --reporter compact to show each test case
add_test(NAME LSPServer COMMAND test_lsp_server --reporter compact)
set_tests_properties(LSPServer PROPERTIES TIMEOUT 120)
