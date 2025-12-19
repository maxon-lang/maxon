const std = @import("std");
const Lexer = @import("lexer.zig").Lexer;
const Parser = @import("parser.zig").Parser;
const ast = @import("ast.zig");
const ast_to_ir = @import("ast_to_ir.zig");
const mutation_analysis = @import("mutation_analysis.zig");
const optimizer = @import("optimizer.zig");
const ir_codegen = @import("ir_codegen.zig");
const pe = @import("pe.zig");
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

/// Compile source and return the IR as a string (for fragment generation)
pub fn compileToIr(source: []const u8, allocator: std.mem.Allocator) ![]const u8 {
    // Lex
    var lexer = Lexer.init(source);
    const tokens = lexer.tokenize(allocator) catch {
        return error.LexerError;
    };
    defer allocator.free(tokens);

    // Parse
    var parser = Parser.init(tokens, allocator);
    defer parser.deinit();
    const program = parser.parse() catch {
        return error.ParserError;
    };
    defer {
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

    // Run mutation analysis for ownership tracking
    var mutation_analyzer = mutation_analysis.MutationAnalyzer.init(allocator);
    defer mutation_analyzer.deinit();
    mutation_analyzer.analyze(program) catch {
        return error.IrError;
    };

    // Convert AST to IR with ownership checking
    var ir_module = ast_to_ir.convert(program, allocator, &mutation_analyzer) catch {
        return error.IrError;
    };
    defer ir_module.deinit();

    // Optimize IR
    optimizer.optimize(&ir_module, allocator) catch {
        return error.IrError;
    };

    // Return IR as string
    return ir_module.printToString(allocator) catch {
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
    // Lex
    var lexer = Lexer.init(source);
    const tokens = lexer.tokenize(allocator) catch {
        return error.LexerError;
    };
    defer allocator.free(tokens);

    // Parse
    var parser = if (source_file) |sf|
        Parser.initWithFile(tokens, allocator, sf)
    else
        Parser.init(tokens, allocator);
    defer parser.deinit();
    const program = parser.parse() catch {
        result.error_info = parser.last_error;
        return error.ParserError;
    };
    defer {
        // Free type declarations
        for (program.types) |type_decl| {
            allocator.free(type_decl.fields);
        }
        allocator.free(program.types);

        // Free enum declarations
        for (program.enums) |enum_decl| {
            allocator.free(enum_decl.members);
        }
        allocator.free(program.enums);

        // Free functions
        for (program.functions) |func| {
            for (func.body) |stmt| {
                freeStatementArgs(stmt, allocator);
            }
            allocator.free(func.body);
            allocator.free(func.params);
        }
        allocator.free(program.functions);
    }

    // Run mutation analysis for ownership tracking
    var mutation_analyzer = mutation_analysis.MutationAnalyzer.init(allocator);
    defer mutation_analyzer.deinit();
    mutation_analyzer.analyze(program) catch {
        return error.IrError;
    };

    // Convert AST to IR with ownership checking
    var ir_error: ?compile_error.CompileError = null;
    var ir_module = ast_to_ir.convertWithFile(program, allocator, &mutation_analyzer, source_file, &ir_error) catch |e| {
        debug.astToIr("AST to IR error: {}\n", .{e});
        result.error_info = ir_error;
        return error.IrError;
    };
    defer ir_module.deinit();

    // Optimize IR
    optimizer.optimize(&ir_module, allocator) catch {
        return error.IrError;
    };

    // Emit IR to file
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

    // Generate code from IR
    debug.log("Generating x86-64 code from IR", .{});
    const codegen_result = ir_codegen.generate(ir_module, allocator) catch |err| {
        debug.log("IR codegen error: {}", .{err});
        return error.CodegenError;
    };
    defer allocator.free(codegen_result.code);
    defer allocator.free(codegen_result.external_patches);

    // Write PE executable
    pe.writePE(output_path, codegen_result.code, codegen_result.external_patches) catch {
        return error.WriteError;
    };
}

/// Free call argument slices in an AST statement
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
    }
}

/// Free dynamically allocated slices in an AST expression
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
