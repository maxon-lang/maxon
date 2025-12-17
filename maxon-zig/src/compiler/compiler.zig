const std = @import("std");
const Lexer = @import("lexer.zig").Lexer;
const Parser = @import("parser.zig").Parser;
const ast = @import("ast.zig");
const ast_to_ir = @import("ast_to_ir.zig");
const optimizer = @import("optimizer.zig");
const ir_codegen = @import("ir_codegen.zig");
const pe = @import("pe.zig");
pub const debug = @import("debug.zig");

pub const CompileError = error{
    LexerError,
    ParserError,
    IrError,
    CodegenError,
    WriteError,
};

pub fn compile(source: []const u8, output_path: []const u8, allocator: std.mem.Allocator) !void {
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
        for (program.functions) |func| {
            // Free call arguments in statements
            for (func.body) |stmt| {
                freeStatementArgs(stmt, allocator);
            }
            allocator.free(func.body);
        }
        allocator.free(program.functions);
    }

    // Convert AST to IR
    var ir_module = ast_to_ir.convert(program, allocator) catch {
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
    const code = ir_codegen.generate(ir_module, allocator) catch |err| {
        debug.log("IR codegen error: {}", .{err});
        return error.CodegenError;
    };
    defer allocator.free(code);

    // Write PE executable
    pe.writePE(output_path, code) catch {
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
    }
}

/// Free call argument slices in an AST expression
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
        else => {},
    }
}
