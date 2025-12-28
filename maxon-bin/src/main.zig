const std = @import("std");
const compiler = @import("compiler/0-compiler.zig");
const test_runner = @import("testing/test_runner.zig");
const testing = @import("testing/testing.zig");

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
    } else if (std.mem.eql(u8, command, "test")) {
        runTest(args, allocator);
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
    std.debug.print("  test                    Run spec fragment tests\n", .{});
    std.debug.print("\nCompile Options:\n", .{});
    std.debug.print("  --no-debug              Disable debug output\n", .{});
    std.debug.print("  --debug                 Enable debug output (default)\n", .{});
    std.debug.print("  --track-allocs          Enable runtime allocation tracking\n", .{});
    std.debug.print("\nTest Options:\n", .{});
    std.debug.print("  --filter <pattern>      Run only tests matching pattern\n", .{});
    std.debug.print("  --verbose               Show detailed output\n", .{});
}

fn runCompile(args: [][:0]u8, allocator: std.mem.Allocator) void {
    var source_path: ?[:0]u8 = null;
    var track_allocs = false;

    // Parse arguments
    for (args[2..]) |arg| {
        if (std.mem.eql(u8, arg, "--no-debug")) {
            compiler.debug.enabled = false;
        } else if (std.mem.eql(u8, arg, "--debug")) {
            compiler.debug.enabled = true;
        } else if (std.mem.eql(u8, arg, "--track-allocs")) {
            track_allocs = true;
        } else if (arg[0] != '-') {
            source_path = arg;
        }
    }

    if (source_path == null) {
        std.debug.print("Usage: maxon-zig compile <source.maxon> [--no-debug]\n", .{});
        std.process.exit(1);
    }

    const src_path = source_path.?;

    // Read source file
    const source = std.fs.cwd().readFileAlloc(allocator, src_path, 1024 * 1024) catch |err| {
        std.debug.print("Error reading file '{s}': {}\n", .{ src_path, err });
        std.process.exit(1);
    };
    defer allocator.free(source);

    // Determine output path
    const output_path = blk: {
        if (std.mem.endsWith(u8, src_path, ".maxon")) {
            const base = src_path[0 .. src_path.len - 6];
            break :blk std.fmt.allocPrint(allocator, "{s}.exe", .{base}) catch {
                std.debug.print("Out of memory\n", .{});
                std.process.exit(1);
            };
        } else if (std.mem.endsWith(u8, src_path, ".test")) {
            const base = src_path[0 .. src_path.len - 5];
            break :blk std.fmt.allocPrint(allocator, "{s}.exe", .{base}) catch {
                std.debug.print("Out of memory\n", .{});
                std.process.exit(1);
            };
        } else {
            break :blk std.fmt.allocPrint(allocator, "{s}.exe", .{src_path}) catch {
                std.debug.print("Out of memory\n", .{});
                std.process.exit(1);
            };
        }
    };
    defer allocator.free(output_path);

    // Compile with error info
    var result: compiler.CompileResult = .{ .error_info = null };

    // Load stdlib
    const stdlib_path = compiler.findStdlibPath(allocator) catch {
        std.debug.print("Error: stdlib not found\n", .{});
        std.process.exit(1);
    };
    defer allocator.free(stdlib_path);

    // Load all stdlib modules
    const stdlib_modules = compiler.loadAllStdlibModules(stdlib_path, allocator) catch {
        std.debug.print("Error: failed to load stdlib modules\n", .{});
        std.process.exit(1);
    };
    defer compiler.freeStdlibModules(stdlib_modules, allocator);

    // Build sources array: stdlib first, then user code
    var sources: std.ArrayListUnmanaged(compiler.Source) = .empty;
    defer sources.deinit(allocator);
    for (stdlib_modules) |mod| {
        sources.append(allocator, mod) catch {
            std.debug.print("Error: out of memory\n", .{});
            std.process.exit(1);
        };
    }
    sources.append(allocator, .{ .path = src_path, .content = source }) catch {
        std.debug.print("Error: out of memory\n", .{});
        std.process.exit(1);
    };

    // Compile with stdlib: stdlib first, then user code
    compiler.compileMultiple(
        sources.items,
        output_path,
        .{ .track_allocs = track_allocs },
        allocator,
        &result,
    ) catch |err| {
        if (result.error_info) |error_info| {
            error_info.printToStderr();
        } else {
            std.debug.print("Compilation failed: {}\n", .{err});
        }
        std.process.exit(1);
    };

    std.debug.print("Compiled: {s}\n", .{output_path});
}

fn runTest(args: [][:0]u8, allocator: std.mem.Allocator) void {
    var options = testing.TestOptions{};

    // Parse arguments
    var i: usize = 2;
    while (i < args.len) : (i += 1) {
        const arg = args[i];
        if (std.mem.eql(u8, arg, "--filter")) {
            i += 1;
            if (i < args.len) {
                options.filter = args[i];
            }
        } else if (std.mem.eql(u8, arg, "--verbose")) {
            options.verbose = true;
        }
    }

    // Run tests
    var timer = std.time.Timer.start() catch unreachable;
    const summary = test_runner.runAllTests(allocator, options) catch |err| {
        std.debug.print("Error running tests: {}\n", .{err});
        std.process.exit(1);
    };
    const elapsed_ns = timer.read();
    const elapsed_ms = @as(f64, @floatFromInt(elapsed_ns)) / 1_000_000.0;
    defer allocator.free(summary.results);

    // Print summary
    summary.printSummaryDebug(elapsed_ms);

    // Exit with error if any tests failed
    if (summary.failed > 0) {
        std.process.exit(1);
    }
}
