cmake_minimum_required(VERSION 3.15)

project(Maxon VERSION 0.1.0 LANGUAGES CXX C)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Option to build components
option(BUILD_COMPILER "Build the Maxon compiler" ON)
option(BUILD_LSP "Build the Maxon LSP server" ON)

# Build the Maxon compiler
if(BUILD_COMPILER)
    message(STATUS "Configuring Maxon compiler...")
    add_subdirectory(maxon-bin)
endif()

# Build the LSP server
if(BUILD_LSP AND EXISTS "${CMAKE_SOURCE_DIR}/lsp-server/CMakeLists.txt")
    message(STATUS "Configuring Maxon LSP server...")
    add_subdirectory(lsp-server)
endif()

# Custom targets for convenience
add_custom_target(lsp-extension
    COMMAND ${CMAKE_COMMAND} -E echo "Building VS Code extension..."
    COMMAND npm install
    COMMAND npm run compile
    WORKING_DIRECTORY ${CMAKE_SOURCE_DIR}/lsp-server/vscode-extension
    COMMENT "Building Maxon VS Code extension"
)

# Install targets
if(TARGET maxon)
    install(TARGETS maxon DESTINATION bin)
endif()
if(TARGET maxon-lsp)
    install(TARGETS maxon-lsp DESTINATION bin)
endif()
