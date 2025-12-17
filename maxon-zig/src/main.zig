const std = @import("std");
const compiler = @import("compiler/compiler.zig");

pub fn main() !void {
    var gpa = std.heap.GeneralPurposeAllocator(.{}){};
    defer _ = gpa.deinit();
    const allocator = gpa.allocator();

    const args = try std.process.argsAlloc(allocator);
    defer std.process.argsFree(allocator, args);

    if (args.len < 2) {
        printUsage();
        std.process.exit(1);
    }

    const command = args[1];

    if (std.mem.eql(u8, command, "compile")) {
        runCompile(args, allocator);
    } else {
        std.debug.print("Unknown command: {s}\n", .{command});
        printUsage();
        std.process.exit(1);
    }
}

fn printUsage() void {
    std.debug.print("Usage: maxon-zig <command> [args]\n\n", .{});
    std.debug.print("Commands:\n", .{});
    std.debug.print("  compile <source.maxon>  Compile a source file\n", .{});
}

fn runCompile(args: [][:0]u8, allocator: std.mem.Allocator) void {
    if (args.len < 3) {
        std.debug.print("Usage: maxon-zig compile <source.maxon>\n", .{});
        std.process.exit(1);
    }

    const source_path = args[2];

    // Read source file
    const source = std.fs.cwd().readFileAlloc(allocator, source_path, 1024 * 1024) catch |err| {
        std.debug.print("Error reading file '{s}': {}\n", .{ source_path, err });
        std.process.exit(1);
    };
    defer allocator.free(source);

    // Determine output path
    const output_path = blk: {
        if (std.mem.endsWith(u8, source_path, ".maxon")) {
            const base = source_path[0 .. source_path.len - 6];
            break :blk std.fmt.allocPrint(allocator, "{s}.exe", .{base}) catch {
                std.debug.print("Out of memory\n", .{});
                std.process.exit(1);
            };
        } else {
            break :blk std.fmt.allocPrint(allocator, "{s}.exe", .{source_path}) catch {
                std.debug.print("Out of memory\n", .{});
                std.process.exit(1);
            };
        }
    };
    defer allocator.free(output_path);

    // Compile
    compiler.compile(source, output_path, allocator) catch |err| {
        std.debug.print("Compilation failed: {}\n", .{err});
        std.process.exit(1);
    };

    std.debug.print("Compiled: {s}\n", .{output_path});
}
