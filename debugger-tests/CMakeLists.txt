cmake_minimum_required(VERSION 3.20)
project(MaxonDebuggerTests)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# On Windows, use native debugging APIs (no LLDB required)
# On Linux/macOS, use LLDB
if(NOT WIN32)
    # Find LLDB (only required on non-Windows platforms)
    find_package(LLDB QUIET)

    if(NOT LLDB_FOUND)
        # Get LLVM directory from CMake variable, environment, or default
        if(DEFINED MAXON_LLVM_DIR)
            set(LLVM_BUILD_DIR "${MAXON_LLVM_DIR}")
        elseif(DEFINED ENV{LLVM_DIR})
            set(LLVM_BUILD_DIR "$ENV{LLVM_DIR}")
        else()
            set(LLVM_BUILD_DIR "${CMAKE_SOURCE_DIR}/llvm-project")
        endif()

        # Try to find LLDB manually
        find_path(LLDB_INCLUDE_DIR
            NAMES lldb/API/LLDB.h
            PATHS
                "${LLVM_BUILD_DIR}/include"
                $ENV{LLVM_HOME}/include
                /usr/include
                /usr/local/include
        )

        find_library(LLDB_LIBRARY
            NAMES lldb liblldb
            PATHS
                "${LLVM_BUILD_DIR}/lib"
                $ENV{LLVM_HOME}/lib
                /usr/lib
                /usr/local/lib
        )

        if(LLDB_INCLUDE_DIR AND LLDB_LIBRARY)
            set(LLDB_FOUND TRUE)
            message(STATUS "Found LLDB: ${LLDB_LIBRARY}")
        else()
            message(STATUS "LLDB not found. Debugger tests will be skipped.")
            message(STATUS "To build debugger tests, install LLDB with headers (lldb-dev package or build from source)")
            
            # Create a dummy target that prints a message
            add_custom_target(debugger-test-runner
                COMMAND ${CMAKE_COMMAND} -E echo "Debugger tests skipped: LLDB not found"
            )
            return()
        endif()
    endif()
endif()

# Platform-specific source files
if(WIN32)
    set(PLATFORM_SOURCES
        debugger_windows.cpp
        debugger_windows.h
    )
    set(PLATFORM_LIBS dbghelp)
else()
    set(PLATFORM_SOURCES
        debugger_lldb.cpp
        debugger_lldb.h
    )
    set(PLATFORM_LIBS ${LLDB_LIBRARY})
endif()

# Create executable
add_executable(debugger-test-runner
    main.cpp
    debugger_test.cpp
    debugger_test.h
    debugger_interface.h
    debugger_factory.cpp
    ${PLATFORM_SOURCES}
)

# Include directories
if(WIN32)
    target_include_directories(debugger-test-runner PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}
    )
else()
    target_include_directories(debugger-test-runner PRIVATE
        ${LLDB_INCLUDE_DIR}
        ${CMAKE_CURRENT_SOURCE_DIR}
    )
endif()

# Link libraries
target_link_libraries(debugger-test-runner PRIVATE
    ${PLATFORM_LIBS}
)

# Set output directory
set_target_properties(debugger-test-runner PROPERTIES
    RUNTIME_OUTPUT_DIRECTORY ${CMAKE_CURRENT_SOURCE_DIR}/bin
)

# Windows-specific settings
if(WIN32)
    target_compile_definitions(debugger-test-runner PRIVATE
        _CRT_SECURE_NO_WARNINGS
        NOMINMAX
    )
endif()

message(STATUS "Debugger test runner will be built")
