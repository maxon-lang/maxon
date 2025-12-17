const std = @import("std");
const Lexer = @import("lexer.zig").Lexer;
const Parser = @import("parser.zig").Parser;
const ast_to_ir = @import("ast_to_ir.zig");
const ir_codegen = @import("ir_codegen.zig");
const pe = @import("pe.zig");

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
    const program = parser.parse() catch {
        return error.ParserError;
    };
    defer {
        for (program.functions) |func| {
            allocator.free(func.body);
        }
        allocator.free(program.functions);
    }

    // Convert AST to IR
    var ir_module = ast_to_ir.convert(program, allocator) catch {
        return error.IrError;
    };
    defer ir_module.deinit();

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
    const code = ir_codegen.generate(ir_module, allocator) catch {
        return error.CodegenError;
    };
    defer allocator.free(code);

    // Write PE executable
    pe.writePE(output_path, code) catch {
        return error.WriteError;
    };
}
