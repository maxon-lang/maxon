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
    track_allocs: bool = false,
    emit_ir: bool = false,
    emit_asm: bool = false,
};

/// Options for the compilation pipeline
const PipelineOptions = struct {
    source_file: ?[]const u8 = null,
    result: ?*CompileResult = null,
    track_allocs: bool = false,
    emit_ir: bool = false,
    external_funcs: ?[]const ast_to_ir.ExternalFuncSignature = null,
    external_types: ?[]const ast_to_ir.ExternalTypeInfo = null,
    external_interfaces: ?[]const ast_to_ir.ExternalInterfaceInfo = null,
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
                    self.allocator.free(sig.param_types);
                }
                if (sig.return_type_name) |rtn| {
                    self.allocator.free(rtn);
                }
            }
            self.allocator.free(sigs);
        }
    }
};

/// Run the frontend pipeline: lex -> parse -> analyze -> IR -> optimize
fn runFrontend(source: []const u8, allocator: std.mem.Allocator, options: PipelineOptions) !FrontendResult {
    // 1 - Lex
    var lexer = Lexer.init(source);
    const tokens = lexer.tokenize(allocator) catch {
        return error.LexerError;
    };
    defer allocator.free(tokens);

    // 2 - Parse
    var parser = if (options.source_file) |sf|
        Parser.initWithFile(tokens, allocator, sf)
    else
        Parser.init(tokens, allocator);
    defer parser.deinit();

    const program = parser.parse() catch {
        if (options.result) |result| {
            result.error_info = parser.last_error;
            // Transfer ownership of allocated message to result
            if (parser.last_error) |*e| {
                e.message_allocated = false;
            }
        }
        return error.ParserError;
    };

    // Extract function signatures from AST before freeing
    // (needed for cross-module compilation with full type info)
    const func_signatures = ast_to_ir.extractFunctionSignaturesFromAst(program, allocator) catch null;
    errdefer if (func_signatures) |sigs| {
        for (sigs) |sig| {
            allocator.free(sig.name);
            if (sig.param_types.len > 0) {
                allocator.free(sig.param_types);
            }
            if (sig.return_type_name) |rtn| {
                allocator.free(rtn);
            }
        }
        allocator.free(sigs);
    };

    defer ast.freeProgram(program, allocator);

    // 3 - Mutation analysis
    var mutation_analyzer = semantic_analysis.MutationAnalyzer.init(allocator);
    defer mutation_analyzer.deinit();
    mutation_analyzer.analyze(program) catch {
        return error.IrError;
    };

    // 4 - AST to IR
    var ir_error: ?compile_error.CompileError = null;
    var ir_module = ast_to_ir.convertWithExternals(program, allocator, &mutation_analyzer, options.source_file, options.external_funcs, options.external_types, options.external_interfaces, &ir_error) catch |e| {
        debug.astToIr("AST to IR error: {}\n", .{e});
        if (options.result) |result| {
            result.error_info = ir_error;
        }
        return error.IrError;
    };

    // 5 - Optimize
    optimizer.optimize(&ir_module, allocator) catch {
        ir_module.deinit();
        return error.IrError;
    };

    return .{ .ir_module = ir_module, .func_signatures = func_signatures, .allocator = allocator };
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

/// Compile multiple sources and return combined IR as string
fn compileMultipleToIr(sources: []const Source, allocator: std.mem.Allocator, result: *CompileResult) ![]const u8 {
    if (sources.len == 0) return error.NoSignatures;

    // Use an arena allocator for Phase 1 ASTs
    var phase1_arena = std.heap.ArenaAllocator.init(allocator);
    defer phase1_arena.deinit();
    const phase1_allocator = phase1_arena.allocator();

    // Phase 1: Parse all sources and collect type info AND function signatures
    var all_types: std.ArrayListUnmanaged(ast_to_ir.ExternalTypeInfo) = .empty;
    defer {
        for (all_types.items) |t| {
            allocator.free(t.fields);
        }
        all_types.deinit(allocator);
    }

    var all_funcs: std.ArrayListUnmanaged(ast_to_ir.ExternalFuncSignature) = .empty;
    defer {
        for (all_funcs.items) |sig| {
            allocator.free(sig.name);
            if (sig.param_types.len > 0) {
                allocator.free(sig.param_types);
            }
            if (sig.return_type_name) |rtn| {
                allocator.free(rtn);
            }
        }
        all_funcs.deinit(allocator);
    }

    var all_interfaces: std.ArrayListUnmanaged(ast_to_ir.ExternalInterfaceInfo) = .empty;
    defer all_interfaces.deinit(allocator);

    // Phase 1: Parse all sources EXCEPT the last one (user code) to collect type info
    // The last source will be compiled directly in Phase 2, so we don't need its types as externals
    const stdlib_sources = sources[0 .. sources.len - 1];
    for (stdlib_sources) |source| {
        var lexer = Lexer.init(source.content);
        const tokens = lexer.tokenize(phase1_allocator) catch continue;

        var parser = Parser.initWithFile(tokens, phase1_allocator, source.path);
        const program = parser.parse() catch continue;

        const type_info = ast_to_ir.extractTypeInfo(program, allocator) catch continue;
        var types_appended: usize = 0;
        errdefer {
            // Free fields from items that weren't appended to all_types
            for (type_info[types_appended..]) |t| {
                allocator.free(t.fields);
            }
            allocator.free(type_info);
        }
        for (type_info) |t| {
            var t_with_path = t;
            t_with_path.source_path = source.path;
            try all_types.append(allocator, t_with_path);
            types_appended += 1;
        }
        allocator.free(type_info);

        const func_sigs = ast_to_ir.extractFunctionSignaturesFromAst(program, allocator) catch continue;
        var sigs_appended: usize = 0;
        errdefer {
            // Free names and param_types from items that weren't appended to all_funcs
            for (func_sigs[sigs_appended..]) |sig| {
                allocator.free(sig.name);
                if (sig.param_types.len > 0) {
                    allocator.free(sig.param_types);
                }
            }
            allocator.free(func_sigs);
        }
        for (func_sigs) |sig| {
            // Make a deep copy of param_types so it outlives the AST frontend
            const param_types_copy = try allocator.dupe(ast_to_ir.ParamType, sig.param_types);

            var sig_with_path = sig;
            sig_with_path.source_path = source.path;
            sig_with_path.param_types = param_types_copy;
            try all_funcs.append(allocator, sig_with_path);
            sigs_appended += 1;
        }
        // Free the original param_types slices (we made copies)
        for (func_sigs) |sig| {
            if (sig.param_types.len > 0) {
                allocator.free(sig.param_types);
            }
        }
        allocator.free(func_sigs);

        // Extract interface info
        const iface_info = ast_to_ir.extractInterfaceInfo(program, allocator) catch continue;
        for (iface_info) |iface| {
            try all_interfaces.append(allocator, iface);
        }
        allocator.free(iface_info);
    }

    // Phase 2: Compile the last source (user code) with all types/funcs available
    const last_source = sources[sources.len - 1];
    var frontend = runFrontend(last_source.content, allocator, .{
        .source_file = last_source.path,
        .result = result,
        .external_funcs = all_funcs.items,
        .external_types = all_types.items,
        .external_interfaces = all_interfaces.items,
    }) catch {
        return error.IrError;
    };
    defer frontend.deinit();

    // Run dead function elimination on the user's module
    optimizer.eliminateDeadFunctions(&frontend.ir_module, allocator) catch {
        return error.IrError;
    };

    return frontend.ir_module.printToString(allocator) catch {
        return error.WriteError;
    };
}

pub fn compile(source: []const u8, output_path: []const u8, allocator: std.mem.Allocator) !void {
    var result: CompileResult = .{ .error_info = null };
    return compileWithInfo(source, output_path, null, .{}, allocator, &result);
}

pub fn compileWithFile(source: []const u8, output_path: []const u8, source_file: []const u8, allocator: std.mem.Allocator, result: *CompileResult) !void {
    return compileWithInfo(source, output_path, source_file, .{}, allocator, result);
}

pub fn compileWithOptions(source: []const u8, output_path: []const u8, source_file: []const u8, options: CompileOptions, allocator: std.mem.Allocator, result: *CompileResult) !void {
    return compileWithInfo(source, output_path, source_file, .{ .track_allocs = options.track_allocs, .emit_ir = options.emit_ir }, allocator, result);
}

fn compileWithInfo(source: []const u8, output_path: []const u8, source_file: ?[]const u8, pipeline_opts: PipelineOptions, allocator: std.mem.Allocator, result: *CompileResult) !void {
    // Run frontend pipeline
    var frontend = try runFrontend(source, allocator, .{
        .source_file = source_file,
        .result = result,
    });
    defer frontend.deinit();

    // Emit IR to file if requested
    if (pipeline_opts.emit_ir) {
        try writeIrFile(frontend.ir_module, output_path, allocator);
    }

    // 6 - Generate x86-64 code
    debug.log("Generating x86-64 code from IR", .{});
    const codegen_result = ir_codegen.generate(frontend.ir_module, allocator, .{
        .track_allocs = pipeline_opts.track_allocs,
    }) catch |err| {
        debug.log("IR codegen error: {}", .{err});
        return error.CodegenError;
    };
    defer allocator.free(codegen_result.code);
    defer allocator.free(codegen_result.external_patches);

    // 7 - Write PE executable
    pe.writePE(output_path, codegen_result.code, codegen_result.external_patches) catch {
        return error.WriteError;
    };
}

// ============================================================================
// Multi-file Compilation
// ============================================================================

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

    // Use an arena allocator for Phase 1 ASTs to prevent Phase 2 from overwriting them
    // (Phase 2 parsing might reuse the same memory addresses otherwise)
    var phase1_arena = std.heap.ArenaAllocator.init(allocator);
    defer phase1_arena.deinit();
    const phase1_allocator = phase1_arena.allocator();

    // Phase 1: Parse all sources and collect type info AND function signatures
    // We must keep ASTs alive because ExternalTypeInfo.type_decl points into them
    var all_types: std.ArrayListUnmanaged(ast_to_ir.ExternalTypeInfo) = .empty;
    defer {
        for (all_types.items) |t| {
            allocator.free(t.fields);
        }
        all_types.deinit(allocator);
    }

    var all_funcs: std.ArrayListUnmanaged(ast_to_ir.ExternalFuncSignature) = .empty;
    defer {
        for (all_funcs.items) |sig| {
            allocator.free(sig.name);
            if (sig.param_types.len > 0) {
                allocator.free(sig.param_types);
            }
            if (sig.return_type_name) |rtn| {
                allocator.free(rtn);
            }
        }
        all_funcs.deinit(allocator);
    }

    var all_interfaces: std.ArrayListUnmanaged(ast_to_ir.ExternalInterfaceInfo) = .empty;
    defer all_interfaces.deinit(allocator);

    for (sources) |source| {
        // Lex and parse using phase1_allocator (but don't compile yet)
        var lexer = Lexer.init(source.content);
        const tokens = lexer.tokenize(phase1_allocator) catch continue;
        // Don't free tokens - arena will clean up

        var parser = Parser.initWithFile(tokens, phase1_allocator, source.path);
        // Don't deinit parser - arena will clean up

        const program = parser.parse() catch continue;
        // Program is kept alive by arena until the end

        // Extract type info from parsed AST
        const type_info = ast_to_ir.extractTypeInfo(program, allocator) catch continue;
        for (type_info) |t| {
            var t_with_path = t;
            t_with_path.source_path = source.path;
            try all_types.append(allocator, t_with_path);
        }
        allocator.free(type_info); // Free the slice but keep the items

        // Extract function signatures from parsed AST
        const func_sigs = ast_to_ir.extractFunctionSignaturesFromAst(program, allocator) catch continue;
        errdefer {
            for (func_sigs) |sig| {
                allocator.free(sig.name);
                if (sig.param_types.len > 0) allocator.free(sig.param_types);
                if (sig.return_type_name) |rtn| allocator.free(rtn);
            }
            allocator.free(func_sigs);
        }
        for (func_sigs) |sig| {
            // Set source_path so we can distinguish stdlib from user code
            var sig_with_path = sig;
            sig_with_path.source_path = source.path;
            try all_funcs.append(allocator, sig_with_path);
        }
        // Free param_types and names from func_sigs (all_funcs has shallow copies sharing the same pointers)
        // Note: We're NOT freeing these since they're now owned by all_funcs (shallow copy shares pointers)
        allocator.free(func_sigs); // Free only the slice

        // Extract interface info from parsed AST
        const iface_info = ast_to_ir.extractInterfaceInfo(program, allocator) catch continue;
        for (iface_info) |iface| {
            try all_interfaces.append(allocator, iface);
        }
        allocator.free(iface_info); // Free the slice but keep the items
    }

    // Phase 2: Compile and merge all sources with collected type info and function signatures
    // Filter external symbols to only include exported ones (unexported symbols are file-private)
    var exported_funcs: std.ArrayListUnmanaged(ast_to_ir.ExternalFuncSignature) = .empty;
    defer exported_funcs.deinit(allocator);
    for (all_funcs.items) |func| {
        if (func.is_exported) {
            try exported_funcs.append(allocator, func);
        }
    }

    var exported_types: std.ArrayListUnmanaged(ast_to_ir.ExternalTypeInfo) = .empty;
    defer exported_types.deinit(allocator);
    for (all_types.items) |t| {
        if (t.is_exported) {
            try exported_types.append(allocator, t);
        }
    }

    var merged: ?FrontendResult = null;
    defer if (merged) |*m| m.deinit();

    for (sources) |source| {
        var frontend = try runFrontend(source.content, allocator, .{
            .source_file = source.path,
            .result = result,
            .external_funcs = exported_funcs.items,
            .external_types = exported_types.items,
            .external_interfaces = all_interfaces.items,
        });

        if (merged) |*m| {
            // Merge into the combined module
            try m.ir_module.merge(&frontend.ir_module);
            // Clean up the frontend we're merging from (not keeping it)
            frontend.deinit();
        } else {
            merged = frontend;
        }
    }

    const final_module = &merged.?.ir_module;

    // Run dead function elimination on the merged module
    // (this must happen after merging, not during per-module optimization)
    optimizer.eliminateDeadFunctions(final_module, allocator) catch {
        return error.IrError;
    };

    // Emit IR to file if requested
    if (options.emit_ir) {
        try writeIrFile(final_module.*, output_path, allocator);
    }

    // Generate x86-64 code
    debug.log("Generating x86-64 code from merged IR", .{});
    const codegen_result = ir_codegen.generate(final_module.*, allocator, .{
        .track_allocs = options.track_allocs,
    }) catch |err| {
        debug.log("IR codegen error: {}", .{err});
        return error.CodegenError;
    };
    defer allocator.free(codegen_result.code);
    defer allocator.free(codegen_result.external_patches);

    // Write PE executable
    pe.writePE(output_path, codegen_result.code, codegen_result.external_patches) catch {
        return error.WriteError;
    };

    // Emit assembly to file if requested (disassemble from final PE with patches applied)
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
    // Load stdlib for type/function info
    const stdlib_path = findStdlibPath(allocator) catch {
        // Continue without stdlib - will have limited type info
        return analyzeSourceForLSP(source, source_file, &.{}, &.{}, &.{}, &.{}, allocator);
    };
    defer allocator.free(stdlib_path);

    const stdlib_modules = loadAllStdlibModules(stdlib_path, allocator) catch {
        return analyzeSourceForLSP(source, source_file, &.{}, &.{}, &.{}, &.{}, allocator);
    };
    // Don't defer free - we'll transfer ownership to SemanticInfo

    // Collect stdlib source content strings (these need to stay alive for string references)
    var stdlib_sources = try allocator.alloc([]const u8, stdlib_modules.len);
    errdefer {
        // Free both the content strings and the slice itself on error
        for (stdlib_sources) |content| {
            allocator.free(content);
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

    // Parse stdlib and extract type info (same as compileMultiple phase 1)
    var phase1_arena = std.heap.ArenaAllocator.init(allocator);
    defer phase1_arena.deinit();
    const phase1_allocator = phase1_arena.allocator();

    var all_types: std.ArrayListUnmanaged(ast_to_ir.ExternalTypeInfo) = .empty;
    defer {
        for (all_types.items) |t| allocator.free(t.fields);
        all_types.deinit(allocator);
    }

    var all_funcs: std.ArrayListUnmanaged(ast_to_ir.ExternalFuncSignature) = .empty;
    defer {
        for (all_funcs.items) |sig| {
            // Free both name, param_types, and return_type_name - extractFunctionSignaturesFromAst allocates all
            allocator.free(sig.name);
            if (sig.param_types.len > 0) allocator.free(sig.param_types);
            if (sig.return_type_name) |rtn| allocator.free(rtn);
        }
        all_funcs.deinit(allocator);
    }

    var all_interfaces: std.ArrayListUnmanaged(ast_to_ir.ExternalInterfaceInfo) = .empty;
    defer all_interfaces.deinit(allocator);

    for (stdlib_sources) |content| {
        var lexer = Lexer.init(content);
        const tokens = lexer.tokenize(phase1_allocator) catch continue;
        var parser = Parser.init(tokens, phase1_allocator);
        const program = parser.parse() catch continue;

        // Extract type info
        const type_info = ast_to_ir.extractTypeInfo(program, allocator) catch continue;
        for (type_info) |t| {
            all_types.append(allocator, t) catch continue;
        }
        allocator.free(type_info);

        // Extract function signatures
        const func_sigs = ast_to_ir.extractFunctionSignaturesFromAst(program, allocator) catch continue;
        for (func_sigs) |sig| {
            const param_types_copy = allocator.dupe(ast_to_ir.ParamType, sig.param_types) catch {
                // Failed to dupe param_types - free the name we own
                allocator.free(sig.name);
                continue;
            };
            var sig_copy = sig;
            sig_copy.param_types = param_types_copy;
            all_funcs.append(allocator, sig_copy) catch {
                allocator.free(param_types_copy);
                allocator.free(sig.name);
                continue;
            };
        }
        for (func_sigs) |sig| {
            if (sig.param_types.len > 0) allocator.free(sig.param_types);
        }
        allocator.free(func_sigs);

        // Extract interface info
        const iface_info = ast_to_ir.extractInterfaceInfo(program, allocator) catch continue;
        for (iface_info) |iface| {
            all_interfaces.append(allocator, iface) catch continue;
        }
        allocator.free(iface_info);
    }

    return analyzeSourceForLSP(
        source,
        source_file,
        all_types.items,
        all_funcs.items,
        all_interfaces.items,
        stdlib_sources,
        allocator,
    );
}

fn analyzeSourceForLSP(
    source: []const u8,
    source_file: ?[]const u8,
    external_types: []const ast_to_ir.ExternalTypeInfo,
    external_funcs: []const ast_to_ir.ExternalFuncSignature,
    external_interfaces: []const ast_to_ir.ExternalInterfaceInfo,
    stdlib_sources: []const []const u8,
    allocator: std.mem.Allocator,
) !semantic_analysis.SemanticInfo {
    _ = source_file; // Not needed in new approach

    // Ensure source ends with newline (parser requires it)
    const needs_newline = source.len == 0 or source[source.len - 1] != '\n';
    const user_source = if (needs_newline)
        try std.mem.concat(allocator, u8, &.{ source, "\n" })
    else
        try allocator.dupe(u8, source);
    errdefer allocator.free(user_source);

    // Lex and parse using the duplicated source
    var lexer = Lexer.init(user_source);
    const tokens = lexer.tokenize(allocator) catch return error.LexerError;
    defer allocator.free(tokens);

    var parser = Parser.init(tokens, allocator);
    defer parser.deinit();

    const program = parser.parse() catch return error.ParserError;
    errdefer ast.freeProgram(program, allocator);

    // Use the new SemanticAnalyzer
    var analyzer = semantic_analysis.SemanticAnalyzer.init(allocator);
    // Always deinit - buildSemanticInfo() transfers ownership of some maps, deinit cleans up the rest
    defer analyzer.deinit();

    // Duplicate external function names (they'll be freed after this call)
    var func_name_copies: std.ArrayListUnmanaged([]const u8) = .empty;
    errdefer {
        for (func_name_copies.items) |name| allocator.free(name);
        func_name_copies.deinit(allocator);
    }

    // Build list of external funcs with duplicated names
    var ext_funcs_copy = try allocator.alloc(semantic_analysis.ExternalFuncSignature, external_funcs.len);
    defer allocator.free(ext_funcs_copy);
    for (external_funcs, 0..) |ext_func, i| {
        const name_copy = allocator.dupe(u8, ext_func.name) catch continue;
        const param_types_copy = allocator.dupe(semantic_analysis.ParamType, ext_func.param_types) catch {
            allocator.free(name_copy);
            continue;
        };
        ext_funcs_copy[i] = .{
            .name = name_copy,
            .return_type = ext_func.return_type,
            .return_type_name = ext_func.return_type_name,
            .return_value_type = ext_func.return_value_type,
            .param_types = param_types_copy,
            .doc_comment = ext_func.doc_comment,
        };
        func_name_copies.append(allocator, name_copy) catch {};
    }

    var result = try analyzer.analyze(program, external_types, ext_funcs_copy, external_interfaces);
    result.stdlib_sources = stdlib_sources;
    result.user_source = user_source;
    result.allocated_func_names = func_name_copies;
    result.program = program;

    // Note: param_types are NOT freed here - they're stored in func_map
    // and will be freed by SemanticInfo.deinit()

    return result;
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
    const paths_to_try = [_][]const []const u8{
        // Development layout: exe is at maxon-bin/zig-out/bin/, stdlib at maxon/stdlib
        &.{ exe_dir, "..", "..", "..", "stdlib" },
        // Alternative dev layout: exe is at zig-out/bin/, stdlib at ../stdlib
        &.{ exe_dir, "..", "..", "stdlib" },
        // Alternative dev layout: exe is at bin/, stdlib at ../stdlib
        &.{ exe_dir, "..", "stdlib" },
        // Installed layout: stdlib next to executable
        &.{ exe_dir, "stdlib" },
    };

    for (paths_to_try) |path_parts| {
        const path = std.fs.path.join(allocator, path_parts) catch continue;
        // Check if directory exists by trying to open it
        var dir = std.fs.cwd().openDir(path, .{}) catch {
            allocator.free(path);
            continue;
        };
        dir.close();
        return path;
    }

    return error.StdlibNotFound;
}

/// Normalize a path by resolving .. and . components without requiring the path to exist.
fn normalizePath(allocator: std.mem.Allocator, path: []const u8) ![]const u8 {
    // Split path into components and resolve .. by removing previous component
    var components: std.ArrayListUnmanaged([]const u8) = .empty;
    defer components.deinit(allocator);

    // Handle Windows absolute paths (e.g., "C:\...")
    var start: usize = 0;
    var root_prefix: []const u8 = "";
    if (path.len >= 2 and path[1] == ':') {
        // Windows drive letter
        if (path.len >= 3 and (path[2] == '\\' or path[2] == '/')) {
            root_prefix = path[0..3];
            start = 3;
        } else {
            root_prefix = path[0..2];
            start = 2;
        }
    } else if (path.len >= 1 and (path[0] == '\\' or path[0] == '/')) {
        root_prefix = path[0..1];
        start = 1;
    }

    // Split remaining path by separators
    var iter = std.mem.tokenizeAny(u8, path[start..], "\\/");
    while (iter.next()) |component| {
        if (std.mem.eql(u8, component, "..")) {
            // Go up one directory
            if (components.items.len > 0) {
                _ = components.pop();
            }
        } else if (!std.mem.eql(u8, component, ".")) {
            // Add normal component (skip ".")
            try components.append(allocator, component);
        }
    }

    // Rebuild the path
    const sep = if (std.mem.indexOf(u8, path, "\\") != null) "\\" else "/";
    var result: std.ArrayListUnmanaged(u8) = .empty;
    errdefer result.deinit(allocator);

    try result.appendSlice(allocator, root_prefix);
    for (components.items, 0..) |component, i| {
        if (i > 0) {
            try result.appendSlice(allocator, sep);
        }
        try result.appendSlice(allocator, component);
    }

    return result.toOwnedSlice(allocator);
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

        // Normalize the path by resolving .. components manually
        const normalized = normalizePath(allocator, path) catch path;
        defer if (normalized.ptr != path.ptr) allocator.free(normalized);
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

    return Source{
        .path = full_path,
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
    var sources: std.ArrayListUnmanaged(Source) = .empty;
    errdefer {
        for (sources.items) |src| {
            allocator.free(src.path);
            allocator.free(src.content);
        }
        sources.deinit(allocator);
    }

    try collectProjectFilesRecursive(project_path, &sources, allocator);

    return sources.toOwnedSlice(allocator);
}

fn collectProjectFilesRecursive(
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
            // Skip hidden directories and common non-source directories
            if (entry.name[0] != '.' and
                !std.mem.eql(u8, entry.name, "node_modules") and
                !std.mem.eql(u8, entry.name, "zig-out") and
                !std.mem.eql(u8, entry.name, "zig-cache"))
            {
                try collectProjectFilesRecursive(full_entry_path, sources, allocator);
            }
        } else if (entry.kind == .file and std.mem.endsWith(u8, entry.name, ".maxon")) {
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
