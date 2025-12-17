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

# Include vendor directories
include_directories(${CMAKE_SOURCE_DIR}/vendor)

# Add Zydis disassembler library
option(ZYDIS_BUILD_TOOLS "" OFF)
option(ZYDIS_BUILD_EXAMPLES "" OFF)
add_subdirectory(${CMAKE_SOURCE_DIR}/vendor/zydis zydis EXCLUDE_FROM_ALL)

# Source files - MIR backend only (no LLVM)
set(SOURCES
    main.cpp
    file_utils.cpp
    call_graph.cpp
    compiler.cpp
    grammar_generator.cpp
    test_utils.cpp
    test_regenerate.cpp
    test_runner.cpp
    temp_runner.cpp
    build_runner.cpp
    self_test.cpp
    docs_generator.cpp
    lexer.cpp
    parser.cpp
    parser/parser_utils.cpp
    parser/parser_expr.cpp
    parser/parser_stmt.cpp
    parser/parser_decl.cpp
    # Incremental parsing support
    incremental_parser.cpp
    # MIR-based code generator (next generation)
    codegen_ng.cpp
    codegen_ng/codegen_ng_expr.cpp
    codegen_ng/codegen_ng_stmt.cpp
    codegen_ng/codegen_ng_function.cpp
    codegen_ng/codegen_ng_output.cpp
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
    backend/x86_disassembler.cpp
    backend/regalloc.cpp
    backend/pe_writer.cpp
    backend/elf_writer.cpp
    backend/dwarf.cpp
    backend/coff_lib_reader.cpp
    # Semantic analysis
    semantic_analyzer.cpp
    semantic_analyzer/semantic_analyzer_expr.cpp
    semantic_analyzer/semantic_analyzer_stmt.cpp
    semantic_analyzer/semantic_analyzer_cache.cpp
    semantic_analyzer/semantic_analyzer_mutation.cpp
    # Constant expression evaluator
    const_eval.cpp
    intrinsics.cpp
    error_formatter.cpp
    # Compiler API for LSP
    compiler_api.cpp
    # Embedded LSP server
    lsp/json_rpc.cpp
    lsp/transport.cpp
    lsp/document_manager.cpp
    lsp/lsp_server.cpp
    lsp/lsp_json.cpp
    # LSP feature providers
    lsp/features/completion.cpp
    lsp/features/diagnostics.cpp
    lsp/features/hover.cpp
    lsp/features/signature_help.cpp
    lsp/features/definition.cpp
    lsp/features/document_symbols.cpp
    lsp/features/references.cpp
    lsp/features/rename.cpp
    lsp/features/code_actions.cpp
    lsp/features/formatting.cpp
    lsp/features/folding.cpp
    lsp/features/linked_editing.cpp
    lsp/features/semantic_tokens.cpp
    # Type system
    types/type_conversion.cpp
    # Vendor libraries
    ${CMAKE_SOURCE_DIR}/vendor/md4c/md4c.c
    ${CMAKE_SOURCE_DIR}/vendor/md4c/md4c-html.c
    ${CMAKE_SOURCE_DIR}/vendor/md4c/entity.c
)

# Create executable
add_executable(maxon ${SOURCES})

# Link Zydis disassembler library
target_link_libraries(maxon PRIVATE Zydis)

# Use precompiled headers for faster compilation
if(CMAKE_VERSION VERSION_GREATER_EQUAL 3.16)
    target_precompile_headers(maxon PRIVATE pch.h)
    # Exclude C files from PCH
    set_source_files_properties(
        ${CMAKE_SOURCE_DIR}/vendor/md4c/md4c.c
        ${CMAKE_SOURCE_DIR}/vendor/md4c/md4c-html.c
        ${CMAKE_SOURCE_DIR}/vendor/md4c/entity.c
        PROPERTIES SKIP_PRECOMPILE_HEADERS ON
    )
endif()

# Set output directory to project root bin for easier access and stdlib resolution
set_target_properties(maxon PROPERTIES
    RUNTIME_OUTPUT_DIRECTORY ${CMAKE_SOURCE_DIR}/bin
)

# Handle rebuilding while VS Code has maxon.exe running:
# PRE_BUILD: Rename existing maxon.exe to maxon.exe.old (allows new build to proceed)
# POST_BUILD: Kill running process, delete .old file
set(MAXON_RUNTIME "${CMAKE_SOURCE_DIR}/bin/maxon${CMAKE_EXECUTABLE_SUFFIX}")
set(MAXON_BACKUP "${MAXON_RUNTIME}.old")

add_custom_command(TARGET maxon PRE_BUILD
    COMMAND ${CMAKE_COMMAND} -E rm -f "${MAXON_BACKUP}"
    COMMAND ${CMAKE_COMMAND} -E rename "${MAXON_RUNTIME}" "${MAXON_BACKUP}" || ${CMAKE_COMMAND} -E true
    COMMENT "Renaming existing maxon binary before rebuild"
)

add_custom_command(TARGET maxon POST_BUILD
    COMMAND taskkill /IM maxon.exe /F 2>nul || ${CMAKE_COMMAND} -E true
    COMMAND ${CMAKE_COMMAND} -E rm -f "${MAXON_BACKUP}"
    COMMENT "Killing old maxon.exe process and cleaning up"
)

# Installation
install(TARGETS maxon DESTINATION bin)
