const std = @import("std");
const Lexer = @import("lexer.zig").Lexer;
const Parser = @import("parser.zig").Parser;
const Codegen = @import("codegen.zig").Codegen;
const pe = @import("pe.zig");

pub fn main() !void {
    var gpa = std.heap.GeneralPurposeAllocator(.{}){};
    defer _ = gpa.deinit();
    const allocator = gpa.allocator();

    const args = try std.process.argsAlloc(allocator);
    defer std.process.argsFree(allocator, args);

    if (args.len < 2) {
        std.debug.print("Usage: maxon-zig <source.maxon>\n", .{});
        std.process.exit(1);
    }

    const source_path = args[1];

    // Read source file
    const source = std.fs.cwd().readFileAlloc(allocator, source_path, 1024 * 1024) catch |err| {
        std.debug.print("Error reading file '{s}': {}\n", .{ source_path, err });
        std.process.exit(1);
    };
    defer allocator.free(source);

    // Lex
    var lexer = Lexer.init(source);
    const tokens = lexer.tokenize(allocator) catch |err| {
        std.debug.print("Lexer error: {}\n", .{err});
        std.process.exit(1);
    };
    defer allocator.free(tokens);

    // Parse
    var parser = Parser.init(tokens, allocator);
    const program = parser.parse() catch |err| {
        std.debug.print("Parser error: {}\n", .{err});
        std.process.exit(1);
    };
    defer {
        for (program.functions) |func| {
            allocator.free(func.body);
        }
        allocator.free(program.functions);
    }

    // Generate code
    var codegen = Codegen.init(allocator);
    const code = codegen.generate(program) catch |err| {
        std.debug.print("Codegen error: {}\n", .{err});
        std.process.exit(1);
    };
    defer allocator.free(code);

    // Determine output path
    const output_path = blk: {
        if (std.mem.endsWith(u8, source_path, ".maxon")) {
            const base = source_path[0 .. source_path.len - 6];
            break :blk try std.fmt.allocPrint(allocator, "{s}.exe", .{base});
        } else {
            break :blk try std.fmt.allocPrint(allocator, "{s}.exe", .{source_path});
        }
    };
    defer allocator.free(output_path);

    // Write PE executable
    pe.writePE(output_path, code) catch |err| {
        std.debug.print("Error writing executable: {}\n", .{err});
        std.process.exit(1);
    };

    std.debug.print("Compiled: {s}\n", .{output_path});
}
