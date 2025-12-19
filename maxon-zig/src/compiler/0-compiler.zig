const std = @import("std");
const Lexer = @import("1-lexer.zig").Lexer;
const Parser = @import("2-parser.zig").Parser;
const ast = @import("ast.zig");
const ast_to_ir = @import("4-ast_to_ir.zig");
const ir = @import("ir.zig");
const mutation_analysis = @import("3-mutation_analysis.zig");
const optimizer = @import("5-optimizer.zig");
const ir_codegen = @import("ir_codegen.zig");
const pe = @import("7-pe.zig");
pub const debug = @import("debug.zig");
pub const compile_error = @import("error.zig");

pub const CompileError = error{
    LexerError,
    ParserError,
    IrError,
    CodegenError,
    WriteError,
};

/// Result of compilation with optional error info
pub const CompileResult = struct {
    error_info: ?compile_error.CompileError,
};

/// Options for the compilation pipeline
const PipelineOptions = struct {
    source_file: ?[]const u8 = null,
    result: ?*CompileResult = null,
};

/// Intermediate result from frontend compilation (lexing, parsing, analysis, IR generation)
const FrontendResult = struct {
    ir_module: ir.Module,
    allocator: std.mem.Allocator,

    fn deinit(self: *FrontendResult) void {
        self.ir_module.deinit();
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
        }
        return error.ParserError;
    };
    defer freeProgram(program, allocator);

    // 3 - Mutation analysis
    var mutation_analyzer = mutation_analysis.MutationAnalyzer.init(allocator);
    defer mutation_analyzer.deinit();
    mutation_analyzer.analyze(program) catch {
        return error.IrError;
    };

    // 4 - AST to IR
    var ir_error: ?compile_error.CompileError = null;
    var ir_module = ast_to_ir.convertWithFile(program, allocator, &mutation_analyzer, options.source_file, &ir_error) catch |e| {
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

    return .{ .ir_module = ir_module, .allocator = allocator };
}

/// Compile source and return the IR as a string (for fragment generation)
pub fn compileToIr(source: []const u8, allocator: std.mem.Allocator) ![]const u8 {
    var frontend = try runFrontend(source, allocator, .{});
    defer frontend.deinit();

    return frontend.ir_module.printToString(allocator) catch {
        return error.WriteError;
    };
}

pub fn compile(source: []const u8, output_path: []const u8, allocator: std.mem.Allocator) !void {
    var result: CompileResult = .{ .error_info = null };
    return compileWithInfo(source, output_path, null, allocator, &result);
}

pub fn compileWithFile(source: []const u8, output_path: []const u8, source_file: []const u8, allocator: std.mem.Allocator, result: *CompileResult) !void {
    return compileWithInfo(source, output_path, source_file, allocator, result);
}

fn compileWithInfo(source: []const u8, output_path: []const u8, source_file: ?[]const u8, allocator: std.mem.Allocator, result: *CompileResult) !void {
    // Run frontend pipeline
    var frontend = try runFrontend(source, allocator, .{
        .source_file = source_file,
        .result = result,
    });
    defer frontend.deinit();

    // Emit IR to file
    try writeIrFile(frontend.ir_module, output_path, allocator);

    // 6 -Generate x86-64 code
    debug.log("Generating x86-64 code from IR", .{});
    const codegen_result = ir_codegen.generate(frontend.ir_module, allocator) catch |err| {
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

// ============================================================================
// AST Memory Management
// ============================================================================

fn freeProgram(program: ast.Program, allocator: std.mem.Allocator) void {
    for (program.types) |type_decl| {
        allocator.free(type_decl.fields);
    }
    allocator.free(program.types);

    for (program.enums) |enum_decl| {
        allocator.free(enum_decl.members);
    }
    allocator.free(program.enums);

    for (program.functions) |func| {
        for (func.body) |stmt| {
            freeStatementArgs(stmt, allocator);
        }
        allocator.free(func.body);
        allocator.free(func.params);
    }
    allocator.free(program.functions);
}

fn freeStatementArgs(stmt: ast.Statement, allocator: std.mem.Allocator) void {
    switch (stmt) {
        .let_decl, .var_decl => |decl| {
            freeExpressionArgs(decl.value, allocator);
        },
        .@"return" => |ret| {
            if (ret.value) |expr| {
                freeExpressionArgs(expr, allocator);
            }
        },
        .index_assign => |idx_assign| {
            freeExpressionArgs(idx_assign.base.*, allocator);
            freeExpressionArgs(idx_assign.index.*, allocator);
            freeExpressionArgs(idx_assign.value, allocator);
        },
        .assign => |assign| {
            freeExpressionArgs(assign.value, allocator);
        },
        .call => |call| {
            for (call.args) |arg| {
                freeExpressionArgs(arg, allocator);
            }
            allocator.free(call.args);
        },
        .field_assign => |assign| {
            freeExpressionArgs(assign.base.*, allocator);
            freeExpressionArgs(assign.value, allocator);
        },
        .if_stmt => |if_s| {
            freeExpressionArgs(if_s.condition, allocator);
            for (if_s.body) |body_stmt| {
                freeStatementArgs(body_stmt, allocator);
            }
            allocator.free(if_s.body);
        },
    }
}

fn freeExpressionArgs(expr: ast.Expression, allocator: std.mem.Allocator) void {
    switch (expr) {
        .call => |call| {
            for (call.args) |arg| {
                freeExpressionArgs(arg, allocator);
            }
            allocator.free(call.args);
        },
        .binary => |bin| {
            freeExpressionArgs(bin.left.*, allocator);
            freeExpressionArgs(bin.right.*, allocator);
        },
        .compare => |cmp| {
            freeExpressionArgs(cmp.left.*, allocator);
            freeExpressionArgs(cmp.right.*, allocator);
        },
        .struct_init => |sinit| {
            for (sinit.fields) |field| {
                freeExpressionArgs(field.value.*, allocator);
            }
            allocator.free(sinit.fields);
        },
        .field_access => |fa| {
            freeExpressionArgs(fa.base.*, allocator);
        },
        .array_literal => |arr| {
            for (arr.elements) |elem| {
                freeExpressionArgs(elem, allocator);
            }
            allocator.free(arr.elements);
        },
        .index => |idx| {
            freeExpressionArgs(idx.base.*, allocator);
            freeExpressionArgs(idx.index.*, allocator);
        },
        .sized_array => |sized| {
            freeExpressionArgs(sized.size.*, allocator);
        },
        // integer, float_lit, identifier: no nested allocations to free
        .integer, .float_lit, .identifier => {},
    }
}
