const std = @import("std");
const Lexer = @import("1-lexer.zig").Lexer;
const Parser = @import("2-parser.zig").Parser;
const ast = @import("ast.zig");
const ast_to_ir = @import("4-ast_to_ir.zig");
const ir = @import("ir.zig");
const semantic_analysis = @import("3-semantic_analysis.zig");
const optimizer = @import("5-optimizer.zig");
const ir_codegen = @import("ir_codegen.zig");
const disasm = @import("disasm.zig");
const pe = @import("7-pe.zig");
pub const debug = @import("debug.zig");
pub const compile_error = @import("error.zig");

pub const CompileError = error{
    LexerError,
    ParserError,
    SemanticError,
    IrError,
    CodegenError,
    WriteError,
    StdlibNotFound,
};

/// Represents a source file with its path and content
pub const Source = struct {
    path: []const u8,
    content: []const u8,
};

/// Result of compilation with optional error info
pub const CompileResult = struct {
    error_info: ?compile_error.CompileError,

    /// Free allocated error message if present
    pub fn deinit(self: *CompileResult, allocator: std.mem.Allocator) void {
        if (self.error_info) |err| {
            if (err.message_allocated) {
                allocator.free(err.message);
            }
        }
    }
};

/// Public compile options exposed to CLI
pub const CompileOptions = struct {
    track_memory: bool = false,
    track_registers: bool = false,
    emit_ir: bool = false,
    emit_asm: bool = false,
};

/// Pipeline mode determines what the frontend produces
const PipelineMode = enum {
    metadata_only, // For parseSourcesForMetadata
    semantic_only, // For LSP analysis
    full_ir, // For compilation (current behavior)
};

/// Result from metadata-only mode
const MetadataResult = struct {
    types: []ast_to_ir.ExternalTypeInfo,
    funcs: []ast_to_ir.ExternalFuncSignature,
    interfaces: []ast_to_ir.ExternalInterfaceInfo,
    extensions: []ast_to_ir.ExternalExtensionInfo,
    enums: []const ast_to_ir.ExternalEnumInfo,
    allocator: std.mem.Allocator,

    pub fn deinit(self: *MetadataResult) void {
        for (self.funcs) |sig| {
            self.allocator.free(sig.name);
            if (sig.param_types.len > 0) {
                ast_to_ir.freeExternalParamTypes(self.allocator, sig.param_types);
            }
            if (sig.return_type_name) |rtn| self.allocator.free(rtn);
            // ExternalValueType doesn't have nested allocations - type name strings are in param_types
        }
        self.allocator.free(self.types);
        self.allocator.free(self.funcs);
        self.allocator.free(self.interfaces);
        self.allocator.free(self.extensions);
        self.allocator.free(self.enums);
    }
};

/// Unified result from runFrontend that varies based on mode
const PipelineResult = union(PipelineMode) {
    metadata_only: MetadataResult,
    semantic_only: semantic_analysis.SemanticInfo,
    full_ir: FrontendResult,

    pub fn deinit(self: *PipelineResult) void {
        switch (self.*) {
            .metadata_only => |*m| m.deinit(),
            .semantic_only => |*s| s.deinit(),
            .full_ir => |*f| f.deinit(),
        }
    }
};

/// Options for the compilation pipeline
const PipelineOptions = struct {
    mode: PipelineMode = .full_ir,
    source_file: ?[]const u8 = null,
    result: ?*CompileResult = null,
    // Parent allocator for error messages that need to survive arena cleanup
    parent_allocator: ?std.mem.Allocator = null,

    // For full_ir mode only:
    external_funcs: []const ast_to_ir.ExternalFuncSignature = &.{},
    external_types: []const ast_to_ir.ExternalTypeInfo = &.{},
    external_interfaces: []const ast_to_ir.ExternalInterfaceInfo = &.{},
    external_extensions: []const ast_to_ir.ExternalExtensionInfo = &.{},
    external_enums: []const ast_to_ir.ExternalEnumInfo = &.{},
    external_type_aliases: []const ast_to_ir.ExternalTypeAliasInfo = &.{},
    track_memory: bool = false,
    track_registers: bool = false,
    emit_ir: bool = false,

    // For semantic_only mode:
    stdlib_sources: []const []const u8 = &.{}, // Owned sources to transfer
};

/// Intermediate result from frontend compilation (lexing, parsing, analysis, IR generation)
const FrontendResult = struct {
    ir_module: ir.Module,
    func_signatures: ?[]const ast_to_ir.ExternalFuncSignature,
    allocator: std.mem.Allocator,

    fn deinit(self: *FrontendResult) void {
        self.ir_module.deinit();
        if (self.func_signatures) |sigs| {
            for (sigs) |sig| {
                self.allocator.free(sig.name);
                if (sig.param_types.len > 0) {
                    ast_to_ir.freeExternalParamTypes(self.allocator, sig.param_types);
                }
                if (sig.return_type_name) |rtn| {
                    self.allocator.free(rtn);
                }
                if (sig.return_value_type) |rvt| {
                    ast_to_ir.freeValueTypeAllocations(self.allocator, rvt);
                }
            }
            self.allocator.free(sigs);
        }
    }
};

/// Free external function signature allocations
fn freeExternalFuncs(allocator: std.mem.Allocator, funcs: []const ast_to_ir.ExternalFuncSignature) void {
    for (funcs) |sig| {
        allocator.free(sig.name);
        if (sig.param_types.len > 0) {
            ast_to_ir.freeExternalParamTypes(allocator, sig.param_types);
        }
        if (sig.return_type_name) |rtn| {
            allocator.free(rtn);
        }
        // ExternalValueType doesn't have nested allocations
    }
}

/// Run the frontend pipeline: lex -> parse -> (metadata/semantic/IR based on mode)
fn runFrontend(source: []const u8, allocator: std.mem.Allocator, options: PipelineOptions) !PipelineResult {
    // 1 - Lex
    var lexer = Lexer.init(source);
    const tokens = lexer.tokenize(allocator) catch return error.LexerError;
    defer allocator.free(tokens);

    // 2 - Parse
    var parser = if (options.source_file) |sf|
        Parser.initWithFile(tokens, allocator, sf)
    else
        Parser.init(tokens, allocator);
    defer parser.deinit();

    const program = parser.parse() catch |err| {
        if (options.result) |result| {
            result.error_info = parser.last_error;
            // Transfer ownership of the error message to result
            // Set message_allocated=false in parser so parser.deinit() won't free it
            if (parser.last_error != null) {
                parser.last_error.?.message_allocated = false;
            }
        }
        return err;
    };
    // Only free program for metadata_only and full_ir modes
    // semantic_only transfers ownership to SemanticInfo
    const should_free_program = options.mode != .semantic_only;
    defer if (should_free_program) ast.freeProgram(program, allocator);

    // Branch based on mode
    switch (options.mode) {
        .metadata_only => {
            // Extract and return metadata only
            const types = try ast_to_ir.extractTypeInfo(program, allocator);
            errdefer allocator.free(types);

            const funcs = try ast_to_ir.extractFunctionSignaturesFromAst(program, allocator);
            errdefer {
                for (funcs) |sig| {
                    allocator.free(sig.name);
                    if (sig.param_types.len > 0) ast_to_ir.freeExternalParamTypes(allocator, sig.param_types);
                    if (sig.return_type_name) |rtn| allocator.free(rtn);
                }
                allocator.free(funcs);
            }

            const interfaces = try ast_to_ir.extractInterfaceInfo(program, allocator);
            errdefer allocator.free(interfaces);

            const extensions = try ast_to_ir.extractExtensionInfo(program, allocator);
            errdefer allocator.free(extensions);

            const enums = try ast_to_ir.extractEnumDecls(program, allocator, options.source_file);
            errdefer allocator.free(enums);

            return .{ .metadata_only = .{
                .types = types,
                .funcs = funcs,
                .interfaces = interfaces,
                .extensions = extensions,
                .enums = enums,
                .allocator = allocator,
            } };
        },

        .semantic_only => {
            // Build external metadata from stdlib_sources
            // Use an arena for parsing stdlib so AST pointers remain valid
            var phase1_arena = std.heap.ArenaAllocator.init(allocator);
            defer phase1_arena.deinit();
            const phase1_allocator = phase1_arena.allocator();

            var external_types: std.ArrayListUnmanaged(ast_to_ir.ExternalTypeInfo) = .empty;
            defer external_types.deinit(allocator);
            var external_funcs: std.ArrayListUnmanaged(ast_to_ir.ExternalFuncSignature) = .empty;
            defer external_funcs.deinit(allocator);
            var external_interfaces: std.ArrayListUnmanaged(ast_to_ir.ExternalInterfaceInfo) = .empty;
            defer external_interfaces.deinit(allocator);

            // Parse stdlib sources (same pattern as original analyzeForLSP)
            for (options.stdlib_sources) |stdlib_content| {
                var stdlib_lexer = Lexer.init(stdlib_content);
                const stdlib_tokens = stdlib_lexer.tokenize(phase1_allocator) catch continue;
                var stdlib_parser = Parser.init(stdlib_tokens, phase1_allocator);
                const stdlib_program = stdlib_parser.parse() catch continue;

                // Extract metadata using main allocator (will be freed later)
                const type_info = ast_to_ir.extractTypeInfo(stdlib_program, allocator) catch continue;
                for (type_info) |t| try external_types.append(allocator, t);
                allocator.free(type_info);

                const func_sigs = ast_to_ir.extractFunctionSignaturesFromAst(stdlib_program, allocator) catch continue;
                for (func_sigs) |sig| try external_funcs.append(allocator, sig);
                allocator.free(func_sigs);

                const iface_info = ast_to_ir.extractInterfaceInfo(stdlib_program, allocator) catch continue;
                for (iface_info) |iface| try external_interfaces.append(allocator, iface);
                allocator.free(iface_info);
            }

            // Run semantic analysis
            var analyzer = semantic_analysis.SemanticAnalyzer.init(allocator);
            defer analyzer.deinit();

            // If analyze fails, free the external func data
            errdefer freeExternalFuncs(allocator, external_funcs.items);

            var semantic_info = analyzer.analyze(
                program,
                external_types.items,
                external_funcs.items,
                external_interfaces.items,
            ) catch return error.SemanticError;

            // Note: external_funcs ownership has been transferred to semantic_info
            // SemanticInfo.deinit() will free the allocated names and param_types

            semantic_info.stdlib_sources = options.stdlib_sources;
            semantic_info.program = program; // Transfer ownership to SemanticInfo
            semantic_info.expr_ptrs = parser.takeExprPtrs(); // Transfer expr ownership

            return .{ .semantic_only = semantic_info };
        },

        .full_ir => {
            // Extract function signatures (needed for cross-module compilation)
            const func_signatures = ast_to_ir.extractFunctionSignaturesFromAst(program, allocator) catch null;
            errdefer if (func_signatures) |sigs| {
                for (sigs) |sig| {
                    allocator.free(sig.name);
                    if (sig.param_types.len > 0) {
                        ast_to_ir.freeExternalParamTypes(allocator, sig.param_types);
                    }
                    if (sig.return_type_name) |rtn| {
                        allocator.free(rtn);
                    }
                    // ExternalValueType doesn't have nested allocations
                }
                allocator.free(sigs);
            };

            // 3 - Mutation analysis
            var mutation_analyzer = semantic_analysis.MutationAnalyzer.init(allocator);
            defer mutation_analyzer.deinit();
            mutation_analyzer.analyze(program) catch return error.IrError;

            // 4 - AST to IR
            var ir_error: ?compile_error.CompileError = null;
            var ir_module = ast_to_ir.convertWithExternals(
                program,
                allocator,
                &mutation_analyzer,
                options.source_file,
                options.external_funcs,
                options.external_types,
                options.external_interfaces,
                options.external_extensions,
                options.external_enums,
                options.external_type_aliases,
                .{ .track_memory = options.track_memory },
                &ir_error,
            ) catch |e| {
                debug.astToIr("AST to IR error: {}\n", .{e});
                if (options.result) |result| {
                    result.error_info = ir_error;
                    // Duplicate error message and file path to parent allocator so they survive arena cleanup
                    if (ir_error) |err| {
                        if (options.parent_allocator) |parent_alloc| {
                            if (err.message_allocated) {
                                result.error_info.?.message = parent_alloc.dupe(u8, err.message) catch err.message;
                            }
                            if (err.location.file) |file| {
                                result.error_info.?.location.file = parent_alloc.dupe(u8, file) catch file;
                            }
                        }
                    }
                }
                return error.IrError;
            };

            // 5 - Optimize
            optimizer.optimize(&ir_module, allocator) catch {
                ir_module.deinit();
                return error.IrError;
            };

            return .{ .full_ir = .{
                .ir_module = ir_module,
                .func_signatures = func_signatures,
                .allocator = allocator,
            } };
        },
    }
}

/// Compile source and return the IR as a string (for fragment generation)
pub fn compileToIr(source: []const u8, allocator: std.mem.Allocator) ![]const u8 {
    // Load all stdlib modules (same as main compile path)
    const stdlib_path = try findStdlibPath(allocator);
    defer allocator.free(stdlib_path);

    const stdlib_modules = try loadAllStdlibModules(stdlib_path, allocator);
    defer freeStdlibModules(stdlib_modules, allocator);

    // Build combined sources: stdlib first, then user source
    var all_sources = try allocator.alloc(Source, stdlib_modules.len + 1);
    defer allocator.free(all_sources);

    for (stdlib_modules, 0..) |mod, i| {
        all_sources[i] = mod;
    }
    all_sources[stdlib_modules.len] = .{ .path = "<test>", .content = source };

    // Use compileMultipleToIr to get proper IR with all stdlib types
    var result: CompileResult = .{ .error_info = null };
    return compileMultipleToIr(all_sources, allocator, &result);
}

/// Compile source and return the IR as a string, with error details exposed
pub fn compileToIrWithResult(source: []const u8, allocator: std.mem.Allocator, result: *CompileResult, source_file: ?[]const u8) ![]const u8 {
    // Load all stdlib modules (same as main compile path)
    const stdlib_path = try findStdlibPath(allocator);
    defer allocator.free(stdlib_path);

    const stdlib_modules = try loadAllStdlibModules(stdlib_path, allocator);
    defer freeStdlibModules(stdlib_modules, allocator);

    // Build combined sources: stdlib first, then user source
    var all_sources = try allocator.alloc(Source, stdlib_modules.len + 1);
    defer allocator.free(all_sources);

    for (stdlib_modules, 0..) |mod, i| {
        all_sources[i] = mod;
    }
    all_sources[stdlib_modules.len] = .{ .path = source_file orelse "<test>", .content = source };

    // Use compileMultipleToIr to get proper IR with all stdlib types
    return compileMultipleToIr(all_sources, allocator, result);
}

// ============================================================================
// Multi-file Compilation Helpers
// ============================================================================

/// Collected metadata from parsing multiple source files.
/// All allocations use the provided arena allocator - no manual cleanup needed.
const ParsedSourcesInfo = struct {
    types: std.ArrayListUnmanaged(ast_to_ir.ExternalTypeInfo),
    funcs: std.ArrayListUnmanaged(ast_to_ir.ExternalFuncSignature),
    interfaces: std.ArrayListUnmanaged(ast_to_ir.ExternalInterfaceInfo),
    extensions: std.ArrayListUnmanaged(ast_to_ir.ExternalExtensionInfo),
    enums: std.ArrayListUnmanaged(ast_to_ir.ExternalEnumInfo),
    type_aliases: std.ArrayListUnmanaged(ast_to_ir.ExternalTypeAliasInfo),
    arena: std.mem.Allocator, // Arena allocator - caller owns the arena

    fn init(arena: std.mem.Allocator) ParsedSourcesInfo {
        return .{
            .types = .empty,
            .funcs = .empty,
            .interfaces = .empty,
            .extensions = .empty,
            .enums = .empty,
            .type_aliases = .empty,
            .arena = arena,
        };
    }

    // No deinit needed - arena handles all cleanup

    /// Filter to only exported functions
    fn getExportedFuncs(self: *ParsedSourcesInfo) ![]const ast_to_ir.ExternalFuncSignature {
        var exported: std.ArrayListUnmanaged(ast_to_ir.ExternalFuncSignature) = .empty;
        for (self.funcs.items) |func| {
            if (func.is_exported) {
                try exported.append(self.arena, func);
            }
        }
        return exported.toOwnedSlice(self.arena);
    }

    /// Filter to only exported types
    fn getExportedTypes(self: *ParsedSourcesInfo) ![]const ast_to_ir.ExternalTypeInfo {
        var exported: std.ArrayListUnmanaged(ast_to_ir.ExternalTypeInfo) = .empty;
        for (self.types.items) |t| {
            if (t.is_exported) {
                try exported.append(self.arena, t);
            }
        }
        return exported.toOwnedSlice(self.arena);
    }

    /// Filter to only exported type aliases
    fn getExportedTypeAliases(self: *ParsedSourcesInfo) ![]const ast_to_ir.ExternalTypeAliasInfo {
        var exported: std.ArrayListUnmanaged(ast_to_ir.ExternalTypeAliasInfo) = .empty;
        for (self.type_aliases.items) |alias| {
            if (alias.is_exported) {
                try exported.append(self.arena, alias);
            }
        }
        return exported.toOwnedSlice(self.arena);
    }
};

/// Parse sources and extract type/function/interface/enum info.
/// All allocations use the arena in ParsedSourcesInfo.
/// Parse errors are printed immediately. Returns ParseError if any file failed to parse.
/// If parent_alloc is provided, error messages are duplicated to survive arena cleanup.
fn parseSourcesForMetadata(info: *ParsedSourcesInfo, sources: []const Source, result: ?*CompileResult, parent_alloc: ?std.mem.Allocator) !void {
    const arena = info.arena;
    var had_parse_error = false;

    for (sources) |source| {
        // Parse using arena so AST stays alive (interfaces need AST pointers)
        var lexer = Lexer.init(source.content);
        const tokens = lexer.tokenize(arena) catch {
            had_parse_error = true;
            continue;
        };

        var parser = Parser.initWithFile(tokens, arena, source.path);
        const program = parser.parse() catch {
            // Capture error info for last parse error
            if (result) |res| {
                res.error_info = parser.last_error;
                // If the error message was allocated with the arena, duplicate it
                // to the parent allocator so it survives arena cleanup
                if (parser.last_error) |err| {
                    if (err.message_allocated) {
                        if (parent_alloc) |alloc| {
                            res.error_info.?.message = alloc.dupe(u8, err.message) catch err.message;
                        }
                    }
                }
            }
            had_parse_error = true;
            continue;
        };

        // Extract metadata using arena - no need to free intermediate slices
        const type_info = ast_to_ir.extractTypeInfo(program, arena) catch continue;
        for (type_info) |t| {
            var t_with_path = t;
            t_with_path.source_path = source.path;
            try info.types.append(arena, t_with_path);
        }
        // No free needed - arena handles it

        const func_sigs = ast_to_ir.extractFunctionSignaturesFromAst(program, arena) catch continue;
        for (func_sigs) |sig| {
            var sig_with_path = sig;
            sig_with_path.source_path = source.path;
            try info.funcs.append(arena, sig_with_path);
        }
        // No free needed - arena handles it

        const iface_info = ast_to_ir.extractInterfaceInfo(program, arena) catch continue;
        for (iface_info) |iface| {
            try info.interfaces.append(arena, iface);
        }
        // No free needed - arena handles it

        const ext_info = ast_to_ir.extractExtensionInfo(program, arena) catch continue;
        for (ext_info) |ext| {
            try info.extensions.append(arena, ext);
        }
        // No free needed - arena handles it

        const enum_decls = ast_to_ir.extractEnumDecls(program, arena, source.path) catch continue;
        for (enum_decls) |enum_decl| {
            try info.enums.append(arena, enum_decl);
        }
        // No free needed - arena handles it

        const type_alias_decls = ast_to_ir.extractTypeAliases(program, arena, source.path) catch continue;
        for (type_alias_decls) |alias_decl| {
            try info.type_aliases.append(arena, alias_decl);
        }
        // No free needed - arena handles it
    }

    if (had_parse_error) {
        return error.ParseError;
    }
}

/// Compile multiple sources and return combined IR as string
fn compileMultipleToIr(sources: []const Source, allocator: std.mem.Allocator, result: *CompileResult) ![]const u8 {
    if (sources.len == 0) return error.NoSignatures;

    // Per-compilation arena for all intermediate data
    var compile_arena = std.heap.ArenaAllocator.init(allocator);
    defer compile_arena.deinit();
    const arena = compile_arena.allocator();

    // Parse all sources except the last one (user code)
    var info = ParsedSourcesInfo.init(arena);
    // No defer info.deinit() needed - arena handles cleanup
    try parseSourcesForMetadata(&info, sources[0 .. sources.len - 1], null, null);

    // Compile the last source (user code) with all types/funcs available
    const last_source = sources[sources.len - 1];
    var pipeline_result = runFrontend(last_source.content, arena, .{
        .mode = .full_ir,
        .source_file = last_source.path,
        .result = result,
        .external_funcs = info.funcs.items,
        .external_types = info.types.items,
        .external_interfaces = info.interfaces.items,
        .external_extensions = info.extensions.items,
        .external_enums = info.enums.items,
    }) catch {
        return error.IrError;
    };
    // No defer pipeline_result.deinit() needed - arena handles cleanup

    optimizer.eliminateDeadFunctions(&pipeline_result.full_ir.ir_module, arena) catch {
        return error.IrError;
    };

    // IR string must use parent allocator since it's returned
    const ir_string = pipeline_result.full_ir.ir_module.printToString(allocator) catch {
        return error.WriteError;
    };

    return ir_string;
}

/// Compile multiple source files into a single executable.
/// Sources are compiled in order and their IR is merged.
/// Later sources can reference functions/types from earlier sources.
pub fn compileMultiple(
    sources: []const Source,
    output_path: []const u8,
    options: CompileOptions,
    allocator: std.mem.Allocator,
    result: *CompileResult,
) !void {
    if (sources.len == 0) return;

    // Per-compilation arena for all intermediate data (metadata, func signatures, etc.)
    // This eliminates complex manual cleanup and prevents leaks on error paths.
    var compile_arena = std.heap.ArenaAllocator.init(allocator);
    defer compile_arena.deinit();
    const arena = compile_arena.allocator();

    // Phase 1: Parse all sources and collect metadata (uses arena)
    var info = ParsedSourcesInfo.init(arena);
    // No defer info.deinit() needed - arena handles all cleanup
    // Pass parent allocator so error messages can be duplicated to survive arena cleanup
    try parseSourcesForMetadata(&info, sources, result, allocator);

    // Phase 2: Filter to exported symbols for cross-file visibility
    const exported_funcs = try info.getExportedFuncs();
    // No defer free needed - arena handles it
    const exported_types = try info.getExportedTypes();
    // No defer free needed - arena handles it
    const exported_type_aliases = try info.getExportedTypeAliases();
    // No defer free needed - arena handles it

    // Phase 3: Compile and merge all sources
    // IR modules use arena - merged at the end, then codegen uses parent allocator for output
    var merged_module: ?ir.Module = null;
    // No defer needed - arena handles IR module cleanup

    for (sources) |source| {
        // Pass all enums to all sources. Internal enums (like SlotState in Map.maxon)
        // need to be visible when user code instantiates generic types that use them.
        const pipeline_result = try runFrontend(source.content, arena, .{
            .mode = .full_ir,
            .source_file = source.path,
            .result = result,
            .parent_allocator = allocator, // For error message survival past arena cleanup
            .external_funcs = exported_funcs,
            .external_types = exported_types,
            .external_interfaces = info.interfaces.items,
            .external_extensions = info.extensions.items,
            .external_enums = info.enums.items,
            .external_type_aliases = exported_type_aliases,
            .track_memory = options.track_memory,
            .track_registers = options.track_registers,
        });

        var frontend = pipeline_result.full_ir;

        if (merged_module) |*m| {
            try m.merge(&frontend.ir_module);
            // No frontend.deinit() needed - arena handles cleanup
        } else {
            merged_module = frontend.ir_module;
        }
    }

    const final_module = &merged_module.?;

    optimizer.eliminateDeadFunctions(final_module, arena) catch {
        return error.IrError;
    };

    if (options.emit_ir) {
        try writeIrFile(final_module.*, output_path, allocator);
    }

    debug.log("Generating x86-64 code from merged IR", .{});
    // Codegen output uses parent allocator since it's passed to PE writer
    const codegen_result = ir_codegen.generate(final_module.*, allocator, .{
        .track_memory = options.track_memory,
        .track_registers = options.track_registers,
    }) catch |err| {
        debug.log("IR codegen error: {}", .{err});
        return error.CodegenError;
    };
    defer allocator.free(codegen_result.code);
    defer allocator.free(codegen_result.external_patches);

    pe.writePE(output_path, codegen_result.code, codegen_result.external_patches) catch {
        return error.WriteError;
    };

    if (options.emit_asm) {
        try writeAsmFileFromPE(output_path, allocator);
    }
}

// ============================================================================
// Semantic Analysis (for LSP)
// ============================================================================

/// Analyze a source file for semantic information (LSP use).
/// Runs the frontend pipeline (lex, parse, type registration, type inference)
/// but doesn't generate IR or machine code.
pub fn analyzeForLSP(
    source: []const u8,
    source_file: ?[]const u8,
    allocator: std.mem.Allocator,
) !semantic_analysis.SemanticInfo {
    debug.log("[analyzeForLSP] Starting analysis for source length: {d}", .{source.len});
    // Load stdlib for type/function info
    const stdlib_path = findStdlibPath(allocator) catch {
        // Continue without stdlib - will have limited type info
        debug.log("[analyzeForLSP] No stdlib found, analyzing without it", .{});
        return runFrontendForSemanticOnly(source, source_file, &.{}, allocator);
    };
    debug.log("[analyzeForLSP] Found stdlib at: {s}", .{stdlib_path});
    defer allocator.free(stdlib_path);

    const stdlib_modules = loadAllStdlibModules(stdlib_path, allocator) catch {
        debug.log("[analyzeForLSP] Failed to load stdlib modules", .{});
        return runFrontendForSemanticOnly(source, source_file, &.{}, allocator);
    };
    debug.log("[analyzeForLSP] Loaded {d} stdlib modules", .{stdlib_modules.len});

    // Collect stdlib source content (ownership transfers to runFrontend)
    var stdlib_sources = try allocator.alloc([]const u8, stdlib_modules.len);
    errdefer {
        // If runFrontendForSemanticOnly fails, free the stdlib sources we collected
        for (stdlib_sources) |src| {
            allocator.free(src);
        }
        allocator.free(stdlib_sources);
    }
    for (stdlib_modules, 0..) |mod, i| {
        stdlib_sources[i] = mod.content;
    }
    // Free the paths but not the content
    for (stdlib_modules) |mod| {
        allocator.free(mod.path);
    }
    allocator.free(stdlib_modules);

    debug.log("[analyzeForLSP] Calling runFrontendForSemanticOnly", .{});
    const result = runFrontendForSemanticOnly(source, source_file, stdlib_sources, allocator);
    debug.log("[analyzeForLSP] Analysis complete", .{});
    return result;
}

fn runFrontendForSemanticOnly(
    source: []const u8,
    source_file: ?[]const u8,
    stdlib_sources: []const []const u8,
    allocator: std.mem.Allocator,
) !semantic_analysis.SemanticInfo {
    // Ensure source ends with newline
    const needs_newline = source.len == 0 or source[source.len - 1] != '\n';
    const user_source = if (needs_newline)
        try std.mem.concat(allocator, u8, &.{ source, "\n" })
    else
        try allocator.dupe(u8, source);
    errdefer allocator.free(user_source);

    // Call runFrontend in semantic_only mode - it handles all stdlib parsing internally
    var result = try runFrontend(user_source, allocator, .{
        .mode = .semantic_only,
        .source_file = source_file,
        .stdlib_sources = stdlib_sources, // Transfer ownership
    });

    // Set user_source (transfers ownership to SemanticInfo)
    result.semantic_only.user_source = user_source;
    return result.semantic_only;
}

/// Find stdlib directory relative to the executable.
/// Returns the path to stdlib/ or error if not found.
pub fn findStdlibPath(allocator: std.mem.Allocator) ![]const u8 {
    // Get path to this executable
    var exe_path_buf: [std.fs.max_path_bytes]u8 = undefined;
    const exe_path = std.fs.selfExePath(&exe_path_buf) catch return error.StdlibNotFound;

    // Get directory containing executable
    const exe_dir = std.fs.path.dirname(exe_path) orelse return error.StdlibNotFound;

    // Try paths in order of preference
    const up_levels = [_]usize{ 4, 3, 2, 1, 0 }; // Number of levels to go up from exe_dir

    for (up_levels) |levels| {
        // Build path by going up 'levels' directories, then down into stdlib
        var path_buf: [std.fs.max_path_bytes]u8 = undefined;
        var fbs = std.io.fixedBufferStream(&path_buf);
        const writer = fbs.writer();

        // Start with exe_dir
        writer.writeAll(exe_dir) catch continue;

        // Go up 'levels' directories
        var i: usize = 0;
        while (i < levels) : (i += 1) {
            writer.writeAll(std.fs.path.sep_str) catch continue;
            writer.writeAll("..") catch continue;
        }

        // Add stdlib
        writer.writeAll(std.fs.path.sep_str) catch continue;
        writer.writeAll("stdlib") catch continue;

        const path = fbs.getWritten();

        // Try to open the directory
        var dir = std.fs.cwd().openDir(path, .{}) catch continue;
        dir.close();
        return allocator.dupe(u8, path);
    }

    return error.StdlibNotFound;
}

/// Normalize a path in-place in a buffer by resolving .. and . components.
/// Returns a slice of the buffer containing the normalized path.
fn normalizePathBuf(path: []const u8, buf: []u8) []const u8 {
    // Handle Windows absolute paths (e.g., "C:\...")
    var start: usize = 0;
    var root_len: usize = 0;
    if (path.len >= 2 and path[1] == ':') {
        if (path.len >= 3 and (path[2] == '\\' or path[2] == '/')) {
            root_len = 3;
        } else {
            root_len = 2;
        }
        start = root_len;
    } else if (path.len >= 1 and (path[0] == '\\' or path[0] == '/')) {
        root_len = 1;
        start = 1;
    }

    // Copy root prefix
    if (root_len > buf.len) return path;
    @memcpy(buf[0..root_len], path[0..root_len]);

    // Determine separator
    const sep: u8 = if (std.mem.indexOf(u8, path, "\\") != null) '\\' else '/';

    // Process path components
    var out_pos: usize = root_len;
    var iter = std.mem.tokenizeAny(u8, path[start..], "\\/");
    var first = true;
    while (iter.next()) |component| {
        if (std.mem.eql(u8, component, "..")) {
            // Go up one directory - find last separator and truncate
            if (out_pos > root_len) {
                out_pos -= 1; // skip past trailing content
                while (out_pos > root_len and buf[out_pos - 1] != sep) {
                    out_pos -= 1;
                }
                if (out_pos > root_len) {
                    out_pos -= 1; // remove separator too
                }
                first = (out_pos == root_len);
            }
        } else if (!std.mem.eql(u8, component, ".")) {
            // Add separator if not first component
            if (!first) {
                if (out_pos >= buf.len) return path;
                buf[out_pos] = sep;
                out_pos += 1;
            }
            first = false;
            // Copy component
            if (out_pos + component.len > buf.len) return path;
            @memcpy(buf[out_pos..][0..component.len], component);
            out_pos += component.len;
        }
    }

    return buf[0..out_pos];
}

/// Print a detailed error message when stdlib is not found.
/// Shows the actual absolute paths that were searched.
pub fn printStdlibNotFoundError(allocator: std.mem.Allocator) void {
    std.debug.print("Error: stdlib not found\n\n", .{});
    std.debug.print("The compiler could not locate the standard library directory.\n\n", .{});
    std.debug.print("Searched locations:\n", .{});

    // Get path to this executable
    var exe_path_buf: [std.fs.max_path_bytes]u8 = undefined;
    const exe_path = std.fs.selfExePath(&exe_path_buf) catch {
        std.debug.print("  (could not determine executable path)\n", .{});
        return;
    };

    const exe_dir = std.fs.path.dirname(exe_path) orelse {
        std.debug.print("  (could not determine executable directory)\n", .{});
        return;
    };

    const paths_to_try = [_][]const []const u8{
        &.{ exe_dir, "..", "..", "..", "stdlib" },
        &.{ exe_dir, "..", "..", "stdlib" },
        &.{ exe_dir, "..", "stdlib" },
        &.{ exe_dir, "stdlib" },
    };

    for (paths_to_try) |path_parts| {
        const path = std.fs.path.join(allocator, path_parts) catch {
            std.debug.print("  (allocation failed)\n", .{});
            continue;
        };
        defer allocator.free(path);

        // Normalize the path by resolving .. components
        var norm_buf: [std.fs.max_path_bytes]u8 = undefined;
        const normalized = normalizePathBuf(path, &norm_buf);
        std.debug.print("  - {s}\n", .{normalized});
    }

    std.debug.print("\nMake sure the 'stdlib' directory exists in one of these locations.\n", .{});
}

/// Load a stdlib module by name (e.g., "collections/array").
/// Returns the source content or error if not found.
pub fn loadStdlibModule(
    stdlib_path: []const u8,
    module_name: []const u8,
    allocator: std.mem.Allocator,
) !Source {
    // Build full path: stdlib_path/module_name.maxon
    const file_path = try std.fs.path.join(allocator, &.{ stdlib_path, module_name });
    defer allocator.free(file_path);

    const full_path = try std.fmt.allocPrint(allocator, "{s}.maxon", .{file_path});

    const content = std.fs.cwd().readFileAlloc(allocator, full_path, 1024 * 1024) catch {
        allocator.free(full_path);
        return error.StdlibNotFound;
    };

    // Canonicalize the path for display purposes
    const resolved_path = std.fs.path.resolve(allocator, &.{full_path}) catch {
        // If resolve fails, use the original path
        return Source{
            .path = full_path,
            .content = content,
        };
    };

    allocator.free(full_path);

    return Source{
        .path = resolved_path,
        .content = content,
    };
}

/// Load all stdlib modules by recursively scanning the stdlib directory.
/// Returns a list of Source structs for all .maxon files found.
pub fn loadAllStdlibModules(
    stdlib_path: []const u8,
    allocator: std.mem.Allocator,
) ![]Source {
    var sources: std.ArrayListUnmanaged(Source) = .empty;
    errdefer {
        for (sources.items) |src| {
            allocator.free(src.path);
            allocator.free(src.content);
        }
        sources.deinit(allocator);
    }

    // Recursively walk stdlib directory
    try collectStdlibFiles(stdlib_path, stdlib_path, &sources, allocator);

    return sources.toOwnedSlice(allocator);
}

fn collectStdlibFiles(
    base_path: []const u8,
    current_path: []const u8,
    sources: *std.ArrayListUnmanaged(Source),
    allocator: std.mem.Allocator,
) !void {
    var dir = std.fs.cwd().openDir(current_path, .{ .iterate = true }) catch {
        return; // Skip directories that can't be opened
    };
    defer dir.close();

    var iter = dir.iterate();
    while (try iter.next()) |entry| {
        const full_entry_path = try std.fs.path.join(allocator, &.{ current_path, entry.name });
        defer allocator.free(full_entry_path);

        if (entry.kind == .directory) {
            // Recursively process subdirectories
            try collectStdlibFiles(base_path, full_entry_path, sources, allocator);
        } else if (entry.kind == .file and std.mem.endsWith(u8, entry.name, ".maxon")) {
            // Load this .maxon file
            const content = std.fs.cwd().readFileAlloc(allocator, full_entry_path, 1024 * 1024) catch {
                continue; // Skip files that can't be read
            };

            const path_copy = try allocator.dupe(u8, full_entry_path);

            try sources.append(allocator, .{
                .path = path_copy,
                .content = content,
            });
        }
    }
}

/// Free all sources returned by loadAllStdlibModules
pub fn freeStdlibModules(sources: []Source, allocator: std.mem.Allocator) void {
    for (sources) |src| {
        allocator.free(src.path);
        allocator.free(src.content);
    }
    allocator.free(sources);
}

// ============================================================================
// Project File Collection (for maxon build)
// ============================================================================

/// Error types for main() detection
pub const MainDetectionError = error{
    NoMainFunction,
    MultipleMainFunctions,
};

/// Collect all .maxon files from a project directory (not stdlib).
/// Returns a list of Source structs for all .maxon files found.
pub fn collectProjectFiles(
    project_path: []const u8,
    allocator: std.mem.Allocator,
) ![]Source {
    return collectProjectFilesExcluding(project_path, null, allocator);
}

/// Collect all .maxon files in a project directory, excluding a specific file
pub fn collectProjectFilesExcluding(
    project_path: []const u8,
    exclude_file: ?[]const u8,
    allocator: std.mem.Allocator,
) ![]Source {
    var sources: std.ArrayListUnmanaged(Source) = .empty;
    errdefer {
        for (sources.items) |src| {
            allocator.free(src.path);
            allocator.free(src.content);
        }
        sources.deinit(allocator);
    }

    try collectProjectFilesRecursiveExcluding(project_path, exclude_file, &sources, allocator);

    return sources.toOwnedSlice(allocator);
}

fn collectProjectFilesRecursiveExcluding(
    current_path: []const u8,
    exclude_file: ?[]const u8,
    sources: *std.ArrayListUnmanaged(Source),
    allocator: std.mem.Allocator,
) !void {
    var dir = std.fs.cwd().openDir(current_path, .{ .iterate = true }) catch {
        return; // Skip directories that can't be opened
    };
    defer dir.close();

    var iter = dir.iterate();
    while (try iter.next()) |entry| {
        const full_entry_path = try std.fs.path.join(allocator, &.{ current_path, entry.name });
        defer allocator.free(full_entry_path);

        if (entry.kind == .directory) {
            // Skip hidden directories and common non-source directories
            if (entry.name[0] != '.' and
                !std.mem.eql(u8, entry.name, "node_modules") and
                !std.mem.eql(u8, entry.name, "zig-out") and
                !std.mem.eql(u8, entry.name, "zig-cache"))
            {
                try collectProjectFilesRecursiveExcluding(full_entry_path, exclude_file, sources, allocator);
            }
        } else if (entry.kind == .file and std.mem.endsWith(u8, entry.name, ".maxon")) {
            // Skip excluded file
            if (exclude_file) |excluded| {
                if (std.mem.eql(u8, entry.name, excluded)) {
                    continue;
                }
            }

            const content = std.fs.cwd().readFileAlloc(allocator, full_entry_path, 1024 * 1024) catch {
                continue; // Skip files that can't be read
            };

            const path_copy = try allocator.dupe(u8, full_entry_path);

            try sources.append(allocator, .{
                .path = path_copy,
                .content = content,
            });
        }
    }
}

/// Find which source file contains the main() function.
/// Returns the path of the file containing main().
pub fn findMainFile(sources: []const Source) MainDetectionError![]const u8 {
    var main_file: ?[]const u8 = null;

    for (sources) |src| {
        if (containsMainFunction(src.content)) {
            if (main_file != null) {
                return error.MultipleMainFunctions;
            }
            main_file = src.path;
        }
    }

    return main_file orelse error.NoMainFunction;
}

/// Check if source content contains a main() function definition.
/// Uses simple text matching for efficiency.
fn containsMainFunction(content: []const u8) bool {
    // Look for "function main(" pattern
    var i: usize = 0;
    while (i < content.len) {
        // Find "function"
        if (i + 8 <= content.len and std.mem.eql(u8, content[i .. i + 8], "function")) {
            var j = i + 8;
            // Skip whitespace
            while (j < content.len and (content[j] == ' ' or content[j] == '\t')) {
                j += 1;
            }
            // Check for "main("
            if (j + 5 <= content.len and std.mem.eql(u8, content[j .. j + 5], "main(")) {
                return true;
            }
        }
        i += 1;
    }
    return false;
}

/// Free sources returned by collectProjectFiles
pub fn freeProjectSources(sources: []Source, allocator: std.mem.Allocator) void {
    for (sources) |src| {
        allocator.free(src.path);
        allocator.free(src.content);
    }
    allocator.free(sources);
}

fn writeIrFile(ir_module: ir.Module, output_path: []const u8, allocator: std.mem.Allocator) !void {
    const ir_path = blk: {
        if (std.mem.endsWith(u8, output_path, ".exe")) {
            const base = output_path[0 .. output_path.len - 4];
            break :blk std.fmt.allocPrint(allocator, "{s}.ir", .{base}) catch {
                return error.WriteError;
            };
        } else {
            break :blk std.fmt.allocPrint(allocator, "{s}.ir", .{output_path}) catch {
                return error.WriteError;
            };
        }
    };
    defer allocator.free(ir_path);

    const ir_text = ir_module.printToString(allocator) catch {
        return error.WriteError;
    };
    defer allocator.free(ir_text);

    const ir_file = std.fs.cwd().createFile(ir_path, .{}) catch {
        return error.WriteError;
    };
    defer ir_file.close();
    ir_file.writeAll(ir_text) catch {
        return error.WriteError;
    };
}

/// Write disassembled machine code to .asm file by reading from the generated PE
fn writeAsmFileFromPE(output_path: []const u8, allocator: std.mem.Allocator) !void {
    const asm_path = blk: {
        if (std.mem.endsWith(u8, output_path, ".exe")) {
            const base = output_path[0 .. output_path.len - 4];
            break :blk std.fmt.allocPrint(allocator, "{s}.asm", .{base}) catch {
                return error.WriteError;
            };
        } else {
            break :blk std.fmt.allocPrint(allocator, "{s}.asm", .{output_path}) catch {
                return error.WriteError;
            };
        }
    };
    defer allocator.free(asm_path);

    // Read the PE file and extract .text section
    const pe_file = std.fs.cwd().openFile(output_path, .{}) catch {
        return error.WriteError;
    };
    defer pe_file.close();

    const pe_data = pe_file.readToEndAlloc(allocator, 10 * 1024 * 1024) catch {
        return error.WriteError;
    };
    defer allocator.free(pe_data);

    // Parse PE to find .text section
    // DOS header: e_lfanew at offset 0x3C gives PE header offset
    if (pe_data.len < 0x40) return error.WriteError;
    const pe_offset = std.mem.readInt(u32, pe_data[0x3C..0x40], .little);
    if (pe_offset + 24 > pe_data.len) return error.WriteError;

    // PE signature check
    if (!std.mem.eql(u8, pe_data[pe_offset..][0..4], "PE\x00\x00")) return error.WriteError;

    // COFF header starts at pe_offset + 4
    const coff_offset = pe_offset + 4;
    const num_sections = std.mem.readInt(u16, pe_data[coff_offset + 2 ..][0..2], .little);
    const optional_header_size = std.mem.readInt(u16, pe_data[coff_offset + 16 ..][0..2], .little);

    // Section headers start after optional header
    const section_table_offset = coff_offset + 20 + optional_header_size;

    // Find .text section
    var text_rva: u32 = 0;
    var text_size: u32 = 0;
    var text_file_offset: u32 = 0;
    for (0..num_sections) |i| {
        const section_offset = section_table_offset + i * 40;
        if (section_offset + 40 > pe_data.len) return error.WriteError;

        const name = pe_data[section_offset..][0..8];
        if (std.mem.eql(u8, name, ".text\x00\x00\x00")) {
            text_size = std.mem.readInt(u32, pe_data[section_offset + 8 ..][0..4], .little);
            text_rva = std.mem.readInt(u32, pe_data[section_offset + 12 ..][0..4], .little);
            text_file_offset = std.mem.readInt(u32, pe_data[section_offset + 20 ..][0..4], .little);
            break;
        }
    }

    if (text_size == 0) return error.WriteError;

    const code = pe_data[text_file_offset..][0..text_size];

    // PE base address for .text section (0x140000000 image base + section RVA)
    const base_addr: u64 = 0x140000000 + @as(u64, text_rva);

    const asm_text = disasm.disassemble(code, base_addr, allocator) catch {
        return error.WriteError;
    };
    defer allocator.free(asm_text);

    const asm_file = std.fs.cwd().createFile(asm_path, .{}) catch {
        return error.WriteError;
    };
    defer asm_file.close();
    asm_file.writeAll(asm_text) catch {
        return error.WriteError;
    };
}
