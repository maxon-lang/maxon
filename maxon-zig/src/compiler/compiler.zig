const std = @import("std");
const Lexer = @import("lexer.zig").Lexer;
const Parser = @import("parser.zig").Parser;
const Codegen = @import("codegen.zig").Codegen;
const pe = @import("pe.zig");

pub const CompileError = error{
    LexerError,
    ParserError,
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

    // Generate code
    var codegen = Codegen.init(allocator);
    defer codegen.deinit();
    const code = codegen.generate(program) catch {
        return error.CodegenError;
    };
    defer allocator.free(code);

    // Write PE executable
    pe.writePE(output_path, code) catch {
        return error.WriteError;
    };
}
