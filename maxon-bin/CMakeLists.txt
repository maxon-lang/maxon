cmake_minimum_required(VERSION 3.13)
project(MaxonCompiler)

# Enable ccache if available for faster rebuilds
find_program(CCACHE_PROGRAM ccache)
if(CCACHE_PROGRAM)
    message(STATUS "Using ccache for faster rebuilds")
    set(CMAKE_C_COMPILER_LAUNCHER "${CCACHE_PROGRAM}")
    set(CMAKE_CXX_COMPILER_LAUNCHER "${CCACHE_PROGRAM}")
endif()

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Enable Link-Time Optimization for smaller binaries
set(CMAKE_INTERPROCEDURAL_OPTIMIZATION TRUE)

# Enable parallel compilation and optimization flags
if(MSVC)
	add_compile_options(/W3)
else()
	add_compile_options(
		-Wall
		-Werror
		-Wno-unused-parameter
		-Wno-sign-compare
	)
endif()

# SIMD optimization flags (required for SIMD lexer/parser)
if(MSVC)
    add_compile_options(/arch:AVX2)
else()
    add_compile_options(-mavx2 -msse4.2)
endif()
message(STATUS "SIMD optimizations enabled (AVX2)")

# Use precompiled headers for faster compilation
if(CMAKE_VERSION VERSION_GREATER_EQUAL 3.16)
    set(CMAKE_PCH_INSTANTIATE_TEMPLATES ON)
endif()

# Source files - MIR backend only (no LLVM)
set(SOURCES
    main.cpp
    file_utils.cpp
    call_graph.cpp
    compiler.cpp
    test_utils.cpp
    test_regenerate.cpp
    test_runner.cpp
    temp_runner.cpp
    self_test.cpp
    docs_generator.cpp
    lexer.cpp
    parser.cpp
    parser/parser_utils.cpp
    parser/parser_expr.cpp
    parser/parser_stmt.cpp
    parser/parser_decl.cpp
    # MIR-based code generator
    codegen_mir.cpp
    codegen_mir/codegen_mir_expr.cpp
    codegen_mir/codegen_mir_expr_string.cpp
    codegen_mir/codegen_mir_expr_array.cpp
    codegen_mir/codegen_mir_expr_map.cpp
    codegen_mir/codegen_mir_optional.cpp
    codegen_mir/codegen_mir_stmt.cpp
    codegen_mir/codegen_mir_stmt_decl.cpp
    codegen_mir/codegen_mir_stmt_assign.cpp
    codegen_mir/codegen_mir_stmt_loop.cpp
    codegen_mir/codegen_mir_function.cpp
    codegen_mir/codegen_mir_output.cpp
    codegen_mir/codegen_mir_safeffi.cpp
    codegen_mir/intrinsic_codegen.cpp
    codegen_mir/codegen_mir_intrinsics.cpp
    # MIR infrastructure
    mir/mir.cpp
    mir/mir_builder.cpp
    mir/mir_parser.cpp
    mir/optimizer.cpp
    mir/optimizer_utils.cpp
    mir/dominance.cpp
    mir/opt_constant_folding.cpp
    mir/opt_constant_propagation.cpp
    mir/opt_dead_code_elim.cpp
    mir/opt_unreachable_block_elim.cpp
    mir/opt_strength_reduction.cpp
    mir/opt_algebraic_simplification.cpp
    mir/opt_function_inlining.cpp
    mir/opt_integer_division.cpp
    mir/opt_redundant_load_store.cpp
    mir/opt_copy_propagation.cpp
    mir/opt_phi_elimination.cpp
    mir/opt_mem2reg.cpp
    mir/memory_ssa.cpp
    mir/ssa_verifier.cpp
    # Custom x86-64 backend
    backend/x86_codegen.cpp
    backend/x86_reg_alloc.cpp
    backend/x86_encoding.cpp
    backend/regalloc.cpp
    backend/pe_writer.cpp
    backend/elf_writer.cpp
    backend/dwarf.cpp
    backend/coff_lib_reader.cpp
    # Semantic analysis
    semantic_analyzer.cpp
    semantic_analyzer/semantic_analyzer_expr.cpp
    semantic_analyzer/semantic_analyzer_stmt.cpp
    intrinsics.cpp
    error_formatter.cpp
    # Type system
    types/type_conversion.cpp
    # Vendor libraries
    vendor/md4c.c
    vendor/md4c-html.c
    vendor/entity.c
)

# Create executable
add_executable(maxon ${SOURCES})

# Create grammar generator executable
add_executable(grammar_generator
    grammar_generator.cpp
    lexer.cpp
    parser.cpp
    parser/parser_utils.cpp
    parser/parser_expr.cpp
    parser/parser_stmt.cpp
    parser/parser_decl.cpp
    file_utils.cpp
)

# Use precompiled headers for faster compilation
if(CMAKE_VERSION VERSION_GREATER_EQUAL 3.16)
    target_precompile_headers(maxon PRIVATE pch.h)
    # Exclude C files from PCH
    set_source_files_properties(vendor/md4c.c vendor/md4c-html.c vendor/entity.c PROPERTIES SKIP_PRECOMPILE_HEADERS ON)
endif()

# Set output directory to project root bin for easier access and stdlib resolution
set_target_properties(maxon PROPERTIES
    RUNTIME_OUTPUT_DIRECTORY ${CMAKE_SOURCE_DIR}/bin
)

set_target_properties(grammar_generator PROPERTIES
    RUNTIME_OUTPUT_DIRECTORY ${CMAKE_SOURCE_DIR}/bin
)

# Installation
install(TARGETS maxon DESTINATION bin)
