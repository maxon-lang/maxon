cmake_minimum_required(VERSION 3.15)

project(maxon-lsp-server VERSION 0.1.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Include directories
include_directories(
    ${CMAKE_CURRENT_SOURCE_DIR}/../maxon-bin
    ${CMAKE_CURRENT_SOURCE_DIR}/include
)

# Source files
set(MAXON_SOURCES
    ../maxon-bin/lexer.cpp
    ../maxon-bin/parser.cpp
    ../maxon-bin/parser/parser_utils.cpp
    ../maxon-bin/parser/parser_expr.cpp
    ../maxon-bin/parser/parser_stmt.cpp
    ../maxon-bin/parser/parser_decl.cpp
    ../maxon-bin/file_utils.cpp
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
    src/formatter.cpp
)

# Create executable
add_executable(maxon-lsp-server ${LSP_SOURCES} ${MAXON_SOURCES})

# Compiler flags
target_compile_options(maxon-lsp-server PRIVATE
    -Wall
    -Werror
    -Wno-unused-parameter
    -Wno-sign-compare
    -mavx2
    -msse4.2
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
