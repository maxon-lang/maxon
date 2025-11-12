cmake_minimum_required(VERSION 3.15)

project(maxon-lsp-server VERSION 0.1.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Include directories
include_directories(
    ${CMAKE_CURRENT_SOURCE_DIR}/../maxon-bin
    ${CMAKE_CURRENT_SOURCE_DIR}/include
)

# Find JSON library (nlohmann/json)
find_package(nlohmann_json QUIET)
if(NOT nlohmann_json_FOUND)
    # If not found, we'll include it as a header-only library
    message(STATUS "nlohmann/json not found, will use single header include")
endif()

# Source files
set(MAXON_SOURCES
    ../maxon-bin/lexer.cpp
    ../maxon-bin/parser.cpp
    ../maxon-bin/semantic_analyzer.cpp
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

# Compiler flags for Clang
target_compile_options(maxon-lsp-server PRIVATE
    -Wall
    -Wno-unused-parameter
    -Wno-sign-compare
)

# Link libraries
if(nlohmann_json_FOUND)
    target_link_libraries(maxon-lsp-server PRIVATE nlohmann_json::nlohmann_json)
endif()

# Set output directory
set_target_properties(maxon-lsp-server PROPERTIES
    RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin
)

# Copy to project root bin directory for easier access and stdlib resolution
# Rename existing file if it's locked (running), then copy new one
add_custom_command(TARGET maxon-lsp-server POST_BUILD
    COMMAND powershell -NoProfile -Command "if (Test-Path '${CMAKE_SOURCE_DIR}/bin/$<TARGET_FILE_NAME:maxon-lsp-server>') { Move-Item -Force '${CMAKE_SOURCE_DIR}/bin/$<TARGET_FILE_NAME:maxon-lsp-server>' '${CMAKE_SOURCE_DIR}/bin/$<TARGET_FILE_NAME:maxon-lsp-server>.old' -ErrorAction SilentlyContinue }"
    COMMAND ${CMAKE_COMMAND} -E copy_if_different
        $<TARGET_FILE:maxon-lsp-server>
        ${CMAKE_SOURCE_DIR}/bin/$<TARGET_FILE_NAME:maxon-lsp-server>
    COMMENT "Copying maxon-lsp-server to project root bin directory"
)

# Installation
install(TARGETS maxon-lsp-server DESTINATION bin)
