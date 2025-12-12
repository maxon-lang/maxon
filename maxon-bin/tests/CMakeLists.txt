cmake_minimum_required(VERSION 3.15)

project(maxon-backend-tests VERSION 0.1.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Use Release configuration
set(CMAKE_BUILD_TYPE Release CACHE STRING "Build type")

# Enable SIMD optimizations (required for lexer)
if(MSVC)
    add_compile_options(/arch:AVX2)
else()
    add_compile_options(-mavx2 -msse4.2)
endif()

# Get project root
get_filename_component(PROJECT_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/../.." ABSOLUTE)

# Include directories
include_directories(
    ${CMAKE_CURRENT_SOURCE_DIR}/..
    ${PROJECT_ROOT}/vendor/catch2
    ${PROJECT_ROOT}/vendor
)

# Add Zydis disassembler library
option(ZYDIS_BUILD_TOOLS "" OFF)
option(ZYDIS_BUILD_EXAMPLES "" OFF)
add_subdirectory(${PROJECT_ROOT}/vendor/zydis zydis EXCLUDE_FROM_ALL)

# Catch2 source
set(CATCH_SOURCE ${PROJECT_ROOT}/vendor/catch2/catch_amalgamated.cpp)

# Backend source files (MIR infrastructure + x86 backend)
set(BACKEND_SOURCES
    ../mir/mir.cpp
    ../mir/mir_builder.cpp
    ../mir/mir_parser.cpp
    ../mir/optimizer.cpp
    ../mir/optimizer_utils.cpp
    ../mir/dominance.cpp
    ../mir/opt_constant_folding.cpp
    ../mir/opt_constant_propagation.cpp
    ../mir/opt_dead_code_elim.cpp
    ../mir/opt_unreachable_block_elim.cpp
    ../mir/opt_strength_reduction.cpp
    ../mir/opt_algebraic_simplification.cpp
    ../mir/opt_function_inlining.cpp
    ../mir/opt_integer_division.cpp
    ../mir/opt_redundant_load_store.cpp
    ../mir/opt_copy_propagation.cpp
    ../mir/opt_phi_elimination.cpp
    ../mir/opt_mem2reg.cpp
    ../mir/memory_ssa.cpp
    ../mir/ssa_verifier.cpp
    ../backend/x86_encoding.cpp
    ../backend/x86_codegen.cpp
    ../backend/x86_reg_alloc.cpp
    ../backend/x86_disassembler.cpp
    ../backend/regalloc.cpp
    ../backend/elf_writer.cpp
    ../backend/pe_writer.cpp
    ../backend/dwarf.cpp
    ../backend/coff_lib_reader.cpp
)

# Frontend source files (lexer, parser, semantic analyzer, codegen_mir)
set(FRONTEND_SOURCES
    ../lexer.cpp
    ../parser.cpp
    ../parser/parser_utils.cpp
    ../parser/parser_expr.cpp
    ../parser/parser_stmt.cpp
    ../parser/parser_decl.cpp
    ../semantic_analyzer.cpp
    ../semantic_analyzer/semantic_analyzer_expr.cpp
    ../semantic_analyzer/semantic_analyzer_stmt.cpp
    ../semantic_analyzer/semantic_analyzer_cache.cpp
    ../codegen_mir.cpp
    ../codegen_mir/codegen_mir_expr.cpp
    ../codegen_mir/codegen_mir_expr_string.cpp
    ../codegen_mir/codegen_mir_managed_array.cpp
    ../codegen_mir/codegen_mir_optional.cpp
    ../codegen_mir/codegen_mir_safeffi.cpp
    ../codegen_mir/codegen_mir_stmt.cpp
    ../codegen_mir/codegen_mir_stmt_decl.cpp
    ../codegen_mir/codegen_mir_stmt_assign.cpp
    ../codegen_mir/codegen_mir_stmt_loop.cpp
    ../codegen_mir/codegen_mir_function.cpp
    ../codegen_mir/codegen_mir_output.cpp
    ../codegen_mir/codegen_mir_intrinsics.cpp
    ../codegen_mir/intrinsic_codegen.cpp
    ../codegen_mir/managed_array_builder.cpp
    ../codegen_mir/managed_string_builder.cpp
    ../types/type_conversion.cpp
    ../file_utils.cpp
    ../compiler.cpp
    ../const_eval.cpp
    ../intrinsics.cpp
    ../error_formatter.cpp
)

# Create a library from backend sources
add_library(backend_lib STATIC ${BACKEND_SOURCES})
target_link_libraries(backend_lib Zydis)

# Create a library from frontend sources (depends on backend_lib for MIR types)
add_library(frontend_lib STATIC ${FRONTEND_SOURCES})
target_link_libraries(frontend_lib backend_lib)

# Compiler flags - warnings as errors
if(MSVC)
    target_compile_options(backend_lib PRIVATE /W3 /WX /wd4267 /wd4244)
    target_compile_options(frontend_lib PRIVATE /W3 /WX /wd4267 /wd4244)
else()
    target_compile_options(backend_lib PRIVATE -Wall -Werror -Wno-unused-parameter -Wno-sign-compare -Wno-unused-variable -Wno-unused-but-set-variable -Wno-deprecated-declarations -Wno-unused-private-field)
    target_compile_options(frontend_lib PRIVATE -Wall -Werror -Wno-unused-parameter -Wno-sign-compare -Wno-unused-variable -Wno-unused-but-set-variable -Wno-deprecated-declarations -Wno-unused-private-field)
endif()

# Common test compile options (includes Catch2 warning suppressions)
if(MSVC)
    set(TEST_COMPILE_OPTIONS /W3 /WX /wd4267 /wd4244)
else()
    set(TEST_COMPILE_OPTIONS -Wall -Werror -Wno-unused-parameter -Wno-sign-compare -Wno-deprecated-literal-operator)
endif()

#==============================================================================
# Test executables - organized by compilation stage
#==============================================================================

# Stage 1: MIR data structures
add_executable(test_mir test_mir.cpp ${CATCH_SOURCE})
target_link_libraries(test_mir backend_lib)
target_compile_options(test_mir PRIVATE ${TEST_COMPILE_OPTIONS})

# Stage 2: MIR text format parser
add_executable(test_mir_parser test_mir_parser.cpp ${CATCH_SOURCE})
target_link_libraries(test_mir_parser backend_lib)
target_compile_options(test_mir_parser PRIVATE ${TEST_COMPILE_OPTIONS})

# Stage 3: MIR optimizer passes
add_executable(test_optimizer test_optimizer.cpp ${CATCH_SOURCE})
target_link_libraries(test_optimizer backend_lib)
target_compile_options(test_optimizer PRIVATE ${TEST_COMPILE_OPTIONS})

# Stage 5: Register allocation
add_executable(test_regalloc test_regalloc.cpp ${CATCH_SOURCE})
target_link_libraries(test_regalloc backend_lib)
target_compile_options(test_regalloc PRIVATE ${TEST_COMPILE_OPTIONS})

# Stage 6: x86 instruction encoding
add_executable(test_x86_encoding test_x86_encoding.cpp ${CATCH_SOURCE})
target_link_libraries(test_x86_encoding backend_lib)
target_compile_options(test_x86_encoding PRIVATE ${TEST_COMPILE_OPTIONS})

# Stage 7: x86 code generation from MIR
add_executable(test_x86_codegen test_x86_codegen.cpp ${CATCH_SOURCE})
target_link_libraries(test_x86_codegen backend_lib)
target_compile_options(test_x86_codegen PRIVATE ${TEST_COMPILE_OPTIONS})

# Stage 8: Executable writers (PE/ELF)
add_executable(test_executable_writers test_executable_writers.cpp ${CATCH_SOURCE})
target_link_libraries(test_executable_writers backend_lib)
target_compile_options(test_executable_writers PRIVATE ${TEST_COMPILE_OPTIONS})

# Stage 9: Debug info (DWARF)
add_executable(test_dwarf test_dwarf.cpp ${CATCH_SOURCE})
target_link_libraries(test_dwarf backend_lib)
target_compile_options(test_dwarf PRIVATE ${TEST_COMPILE_OPTIONS})

# Stage 10: Build command (multi-file project compilation via CLI)
add_executable(test_build test_build.cpp ${CATCH_SOURCE})
target_compile_options(test_build PRIVATE ${TEST_COMPILE_OPTIONS})

#==============================================================================
# Combined test runner (all backend tests without frontend dependency)
#==============================================================================
add_executable(run_all_backend_tests
    test_mir.cpp
    test_mir_parser.cpp
    test_optimizer.cpp
    test_regalloc.cpp
    test_x86_encoding.cpp
    test_x86_codegen.cpp
    test_executable_writers.cpp
    test_dwarf.cpp
    ${CATCH_SOURCE}
)
target_link_libraries(run_all_backend_tests backend_lib)
target_compile_options(run_all_backend_tests PRIVATE ${TEST_COMPILE_OPTIONS})

#==============================================================================
# CTest registration
#==============================================================================
enable_testing()

# Backend-only tests (fast, no frontend dependency)
# Use --reporter compact for one-line-per-test output
add_test(NAME MIR COMMAND test_mir --reporter compact)
add_test(NAME MIRParser COMMAND test_mir_parser --reporter compact)
add_test(NAME Optimizer COMMAND test_optimizer --reporter compact)
add_test(NAME RegisterAllocation COMMAND test_regalloc --reporter compact)
add_test(NAME X86Encoding COMMAND test_x86_encoding --reporter compact)
add_test(NAME X86Codegen COMMAND test_x86_codegen --reporter compact)
add_test(NAME ExecutableWriters COMMAND test_executable_writers --reporter compact)
add_test(NAME DWARF COMMAND test_dwarf --reporter compact)

# Frontend + backend tests (slower, full pipeline)
add_test(NAME Build COMMAND test_build --reporter compact)

# Combined runner
add_test(NAME AllBackendTests COMMAND run_all_backend_tests --reporter compact)
