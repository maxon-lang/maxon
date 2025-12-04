cmake_minimum_required(VERSION 3.15)

project(Maxon VERSION 0.1.0 LANGUAGES CXX C)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Export compile commands for IntelliSense
set(CMAKE_EXPORT_COMPILE_COMMANDS ON)

# Option to build components
option(BUILD_COMPILER "Build the Maxon compiler" ON)

# Build the Maxon compiler (includes embedded LSP server)
if(BUILD_COMPILER)
    message(STATUS "Configuring Maxon compiler...")
    add_subdirectory(maxon-bin)
endif()

# Custom targets for convenience
add_custom_target(lsp-extension
    COMMAND ${CMAKE_COMMAND} -E echo "Building VS Code extension..."
    COMMAND npm install
    COMMAND npm run compile
    WORKING_DIRECTORY ${CMAKE_SOURCE_DIR}/vscode-extension
    COMMENT "Building Maxon VS Code extension"
)

# Install targets
if(TARGET maxon)
    install(TARGETS maxon DESTINATION bin)
endif()
