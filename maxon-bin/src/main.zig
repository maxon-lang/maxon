const std = @import("std");
const compiler = @import("compiler/0-compiler.zig");
const test_runner = @import("testing/test_runner.zig");
const testing = @import("testing/testing.zig");
const lsp = @import("lsp/lsp.zig");

const version = "0.1.0";

pub fn main() !void {
    var gpa = std.heap.GeneralPurposeAllocator(.{ .stack_trace_frames = 8 }){};
    defer {
        const check = gpa.deinit();
        if (check == .leak) {
            std.debug.print("Memory leak detected!\n", .{});
        }
    }
    const allocator = gpa.allocator();

    const args = try std.process.argsAlloc(allocator);
    defer std.process.argsFree(allocator, args);

    // Handle global options
    if (args.len >= 2) {
        const first_arg = args[1];
        if (std.mem.eql(u8, first_arg, "--help")) {
            printUsage();
            return;
        } else if (std.mem.eql(u8, first_arg, "--version")) {
            std.debug.print("maxon {s}\n", .{version});
            return;
        }
    }

    if (args.len < 2) {
        printUsage();
        std.process.exit(1);
    }

    const command = args[1];

    if (std.mem.eql(u8, command, "compile")) {
        runCompile(args, allocator);
    } else if (std.mem.eql(u8, command, "build")) {
        runBuild(args, allocator);
    } else if (std.mem.eql(u8, command, "test")) {
        runTest(args, allocator);
    } else if (std.mem.eql(u8, command, "lsp-server")) {
        runLspServer(allocator);
    } else {
        std.debug.print("Unknown command: {s}\n", .{command});
        printUsage();
        std.process.exit(1);
    }
}

fn printUsage() void {
    std.debug.print("Usage: maxon [options] <command> [args]\n\n", .{});
    std.debug.print("Options:\n", .{});
    std.debug.print("  --help                  Show this help message\n", .{});
    std.debug.print("  --version               Show version information\n", .{});
    std.debug.print("\nCommands:\n", .{});
    std.debug.print("  compile <source.maxon>  Compile a single source file\n", .{});
    std.debug.print("  build                   Build project from current directory\n", .{});
    std.debug.print("  test                    Run spec fragment tests\n", .{});
    std.debug.print("  lsp-server              Start LSP server for IDE integration\n", .{});
    std.debug.print("\nCompile Options:\n", .{});
    std.debug.print("  -v                      Enable verbose/debug output\n", .{});
    std.debug.print("  --track-memory          Enable runtime memory tracking (allocs, moves, refcounts)\n", .{});
    std.debug.print("  --emit-ir               Emit IR output (.ir file)\n", .{});
    std.debug.print("  --emit-asm              Emit assembly output (.asm file)\n", .{});
    std.debug.print("\nBuild Options:\n", .{});
    std.debug.print("  -v                      Enable verbose/debug output\n", .{});
    std.debug.print("  --track-memory          Enable runtime memory tracking (allocs, moves, refcounts)\n", .{});
    std.debug.print("  --emit-ir               Emit IR output (.ir file)\n", .{});
    std.debug.print("  --emit-asm              Emit assembly output (.asm file)\n", .{});
    std.debug.print("\nTest Options:\n", .{});
    std.debug.print("  --filter <pattern>      Run only tests matching pattern\n", .{});
    std.debug.print("  --verbose               Show detailed output\n", .{});
}

fn runCompile(args: [][:0]u8, allocator: std.mem.Allocator) void {
    var source_path: ?[:0]u8 = null;
    var track_memory = false;
    var emit_ir = false;
    var emit_asm = false;

    // Parse arguments
    for (args[2..]) |arg| {
        if (std.mem.eql(u8, arg, "-v")) {
            compiler.debug.enabled = true;
        } else if (std.mem.eql(u8, arg, "--track-memory") or std.mem.eql(u8, arg, "--track-allocs")) {
            track_memory = true;
        } else if (std.mem.eql(u8, arg, "--emit-ir")) {
            emit_ir = true;
        } else if (std.mem.eql(u8, arg, "--emit-asm")) {
            emit_asm = true;
        } else if (arg.len > 0 and arg[0] == '-') {
            std.debug.print("Unknown option: {s}\n", .{arg});
            std.process.exit(1);
        } else {
            source_path = arg;
        }
    }

    if (source_path == null) {
        std.debug.print("Usage: maxon compile <source.maxon> [-v]\n", .{});
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
        compiler.printStdlibNotFoundError(allocator);
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
        .{ .track_memory = track_memory, .emit_ir = emit_ir, .emit_asm = emit_asm },
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
    if (emit_ir) {
        const ir_path = blk: {
            if (std.mem.endsWith(u8, output_path, ".exe")) {
                const base = output_path[0 .. output_path.len - 4];
                break :blk std.fmt.allocPrint(allocator, "{s}.ir", .{base}) catch {
                    std.debug.print("Out of memory\n", .{});
                    std.process.exit(1);
                };
            } else {
                break :blk std.fmt.allocPrint(allocator, "{s}.ir", .{output_path}) catch {
                    std.debug.print("Out of memory\n", .{});
                    std.process.exit(1);
                };
            }
        };
        defer allocator.free(ir_path);
        std.debug.print("IR:        {s}\n", .{ir_path});
    }
    if (emit_asm) {
        const asm_path = blk: {
            if (std.mem.endsWith(u8, output_path, ".exe")) {
                const base = output_path[0 .. output_path.len - 4];
                break :blk std.fmt.allocPrint(allocator, "{s}.asm", .{base}) catch {
                    std.debug.print("Out of memory\n", .{});
                    std.process.exit(1);
                };
            } else {
                break :blk std.fmt.allocPrint(allocator, "{s}.asm", .{output_path}) catch {
                    std.debug.print("Out of memory\n", .{});
                    std.process.exit(1);
                };
            }
        };
        defer allocator.free(asm_path);
        std.debug.print("Assembly:  {s}\n", .{asm_path});
    }
}

/// Build configuration parsed from build.maxon JSON output
const BuildConfig = struct {
    name: []const u8,
    output: []const u8,
    sources: []const []const u8,
    optimize: bool,
    debug_info: bool,
};

fn runBuild(args: [][:0]u8, allocator: std.mem.Allocator) void {
    var track_memory = false;
    var emit_ir = false;
    var emit_asm = false;

    // Parse arguments
    for (args[2..]) |arg| {
        if (std.mem.eql(u8, arg, "-v")) {
            compiler.debug.enabled = true;
        } else if (std.mem.eql(u8, arg, "--track-memory") or std.mem.eql(u8, arg, "--track-allocs")) {
            track_memory = true;
        } else if (std.mem.eql(u8, arg, "--emit-ir")) {
            emit_ir = true;
        } else if (std.mem.eql(u8, arg, "--emit-asm")) {
            emit_asm = true;
        } else if (arg.len > 0 and arg[0] == '-') {
            std.debug.print("Unknown option: {s}\n", .{arg});
            std.process.exit(1);
        }
    }

    // Get current working directory
    const cwd_path = std.fs.cwd().realpathAlloc(allocator, ".") catch |err| {
        std.debug.print("Error getting current directory: {}\n", .{err});
        std.process.exit(1);
    };
    defer allocator.free(cwd_path);

    // Check for build.maxon file
    const build_maxon_path = std.fs.path.join(allocator, &.{ cwd_path, "build.maxon" }) catch {
        std.debug.print("Out of memory\n", .{});
        std.process.exit(1);
    };
    defer allocator.free(build_maxon_path);

    // Load stdlib first (needed for both build.maxon execution and project compilation)
    const stdlib_path = compiler.findStdlibPath(allocator) catch {
        compiler.printStdlibNotFoundError(allocator);
        std.process.exit(1);
    };
    defer allocator.free(stdlib_path);

    const stdlib_modules = compiler.loadAllStdlibModules(stdlib_path, allocator) catch {
        std.debug.print("Error: failed to load stdlib modules\n", .{});
        std.process.exit(1);
    };
    defer compiler.freeStdlibModules(stdlib_modules, allocator);

    // Try to read build.maxon
    const build_source = std.fs.cwd().readFileAlloc(allocator, "build.maxon", 1024 * 1024) catch |err| {
        if (err == error.FileNotFound) {
            // No build.maxon - fall back to legacy behavior
            runBuildLegacy(cwd_path, stdlib_modules, track_memory, emit_ir, emit_asm, allocator);
            return;
        }
        std.debug.print("Error reading build.maxon: {}\n", .{err});
        std.process.exit(1);
    };
    defer allocator.free(build_source);

    std.debug.print("Found build.maxon, executing...\n", .{});

    // Compile and run build.maxon to get JSON configuration
    const build_config = executeBuildMaxon(build_source, stdlib_modules, allocator) catch |err| {
        std.debug.print("Error executing build.maxon: {}\n", .{err});
        std.process.exit(1);
    };

    // Determine output path
    const output_path = if (build_config.output.len > 0)
        allocator.dupe(u8, build_config.output) catch {
            std.debug.print("Out of memory\n", .{});
            std.process.exit(1);
        }
    else
        std.fmt.allocPrint(allocator, "bin/{s}.exe", .{build_config.name}) catch {
            std.debug.print("Out of memory\n", .{});
            std.process.exit(1);
        };
    defer allocator.free(output_path);

    // Collect project files (excluding build.maxon)
    const user_sources = compiler.collectProjectFilesExcluding(cwd_path, "build.maxon", allocator) catch |err| {
        std.debug.print("Error scanning directory: {}\n", .{err});
        std.process.exit(1);
    };
    defer compiler.freeProjectSources(user_sources, allocator);

    if (user_sources.len == 0) {
        std.debug.print("Error: No .maxon files found in project\n", .{});
        std.process.exit(1);
    }

    // Build combined sources array: stdlib first, then user files
    var all_sources: std.ArrayListUnmanaged(compiler.Source) = .empty;
    defer all_sources.deinit(allocator);

    for (stdlib_modules) |mod| {
        all_sources.append(allocator, mod) catch {
            std.debug.print("Error: out of memory\n", .{});
            std.process.exit(1);
        };
    }
    for (user_sources) |src| {
        all_sources.append(allocator, src) catch {
            std.debug.print("Error: out of memory\n", .{});
            std.process.exit(1);
        };
    }

    // Print what we're building
    std.debug.print("Building '{s}' with {d} source file(s)...\n", .{ build_config.name, user_sources.len });

    // Create output directory if needed
    if (std.fs.path.dirname(output_path)) |dir| {
        std.fs.cwd().makePath(dir) catch {};
    }

    // Compile all together
    var result: compiler.CompileResult = .{ .error_info = null };
    compiler.compileMultiple(
        all_sources.items,
        output_path,
        .{ .track_memory = track_memory, .emit_ir = emit_ir, .emit_asm = emit_asm },
        allocator,
        &result,
    ) catch |err| {
        if (result.error_info) |error_info| {
            error_info.printToStderr();
        } else {
            std.debug.print("Build failed: {}\n", .{err});
        }
        std.process.exit(1);
    };

    std.debug.print("Built: {s}\n", .{output_path});
    printEmitPaths(output_path, emit_ir, emit_asm, allocator);
}

/// Execute build.maxon and parse its JSON output
fn executeBuildMaxon(build_source: []const u8, stdlib_modules: []const compiler.Source, allocator: std.mem.Allocator) !BuildConfig {
    // Create a temporary executable for build.maxon
    const temp_exe = "build_temp.exe";

    // Build sources: stdlib + build.maxon
    var sources: std.ArrayListUnmanaged(compiler.Source) = .empty;
    defer sources.deinit(allocator);

    for (stdlib_modules) |mod| {
        try sources.append(allocator, mod);
    }
    try sources.append(allocator, .{ .path = "build.maxon", .content = build_source });

    // Compile build.maxon
    var result: compiler.CompileResult = .{ .error_info = null };
    compiler.compileMultiple(
        sources.items,
        temp_exe,
        .{ .track_memory = false, .emit_ir = false, .emit_asm = false },
        allocator,
        &result,
    ) catch |err| {
        if (result.error_info) |error_info| {
            error_info.printToStderr();
        }
        return err;
    };
    defer std.fs.cwd().deleteFile(temp_exe) catch {};

    // Execute build_temp.exe and capture stdout
    // Get full path for the temp exe
    const cwd = std.fs.cwd().realpathAlloc(allocator, ".") catch {
        std.debug.print("Error: could not get cwd\n", .{});
        return error.ExecutionFailed;
    };
    defer allocator.free(cwd);

    const abs_temp_exe = std.fs.path.join(allocator, &.{ cwd, temp_exe }) catch {
        return error.ExecutionFailed;
    };
    defer allocator.free(abs_temp_exe);

    std.debug.print("Running: {s}\n", .{abs_temp_exe});

    // Verify file exists
    const stat = std.fs.cwd().statFile(temp_exe) catch |err| {
        std.debug.print("Cannot stat temp exe: {}\n", .{err});
        return error.ExecutionFailed;
    };
    std.debug.print("Temp exe size: {d} bytes\n", .{stat.size});

    // Brief pause to ensure file system has finished writing (Windows antivirus sometimes locks new executables)
    std.Thread.sleep(200 * std.time.ns_per_ms);

    var child = std.process.Child.init(&.{abs_temp_exe}, allocator);
    child.stdout_behavior = .Pipe;
    child.stderr_behavior = .Inherit;

    child.spawn() catch |err| {
        std.debug.print("Error spawning build.maxon: {}\n", .{err});
        return error.ExecutionFailed;
    };

    // Read all stdout data
    var stdout_data: std.ArrayListUnmanaged(u8) = .empty;
    defer stdout_data.deinit(allocator);

    var buf: [4096]u8 = undefined;
    while (true) {
        const n = child.stdout.?.read(&buf) catch break;
        if (n == 0) break;
        stdout_data.appendSlice(allocator, buf[0..n]) catch break;
    }

    const term = child.wait() catch |err| {
        std.debug.print("Error waiting for build.maxon: {}\n", .{err});
        return error.ExecutionFailed;
    };

    if (term.Exited != 0) {
        std.debug.print("build.maxon exited with code {d}\n", .{term.Exited});
        return error.ExecutionFailed;
    }

    const stdout = stdout_data.items;

    // Parse JSON output
    return parseBuildConfig(stdout, allocator);
}

/// Parse JSON build configuration from build.maxon output
fn parseBuildConfig(json: []const u8, allocator: std.mem.Allocator) !BuildConfig {
    const parsed = std.json.parseFromSlice(std.json.Value, allocator, json, .{}) catch |err| {
        std.debug.print("Error parsing build.maxon JSON output: {}\n", .{err});
        std.debug.print("Output was:\n{s}\n", .{json});
        return error.ParseError;
    };
    defer parsed.deinit();

    const root = parsed.value;
    if (root != .object) {
        std.debug.print("Error: build.maxon output is not a JSON object\n", .{});
        return error.ParseError;
    }

    const obj = root.object;

    const name = if (obj.get("name")) |v| switch (v) {
        .string => |s| try allocator.dupe(u8, s),
        else => {
            std.debug.print("Error: 'name' must be a string\n", .{});
            return error.ParseError;
        },
    } else {
        std.debug.print("Error: 'name' is required in build.maxon output\n", .{});
        return error.ParseError;
    };

    const output = if (obj.get("output")) |v| switch (v) {
        .string => |s| try allocator.dupe(u8, s),
        else => "",
    } else "";

    const optimize = if (obj.get("optimize")) |v| switch (v) {
        .bool => |b| b,
        else => false,
    } else false;

    const debug_info = if (obj.get("debug_info")) |v| switch (v) {
        .bool => |b| b,
        else => false,
    } else false;

    // Parse sources array (may be empty for auto-discovery)
    var sources_list: std.ArrayListUnmanaged([]const u8) = .empty;
    if (obj.get("sources")) |v| {
        if (v == .array) {
            for (v.array.items) |item| {
                if (item == .string) {
                    try sources_list.append(allocator, try allocator.dupe(u8, item.string));
                }
            }
        }
    }

    return BuildConfig{
        .name = name,
        .output = output,
        .sources = try sources_list.toOwnedSlice(allocator),
        .optimize = optimize,
        .debug_info = debug_info,
    };
}

/// Legacy build behavior when no build.maxon is present
fn runBuildLegacy(cwd_path: []const u8, stdlib_modules: []const compiler.Source, track_memory: bool, emit_ir: bool, emit_asm: bool, allocator: std.mem.Allocator) void {
    // Scan for .maxon files
    const user_sources = compiler.collectProjectFiles(cwd_path, allocator) catch |err| {
        std.debug.print("Error scanning directory: {}\n", .{err});
        std.process.exit(1);
    };
    defer compiler.freeProjectSources(user_sources, allocator);

    if (user_sources.len == 0) {
        std.debug.print("Error: No .maxon files found in current directory\n", .{});
        std.process.exit(1);
    }

    // Find file with main()
    const main_file = compiler.findMainFile(user_sources) catch |err| {
        switch (err) {
            error.NoMainFunction => std.debug.print("Error: No main() function found in any .maxon file\n", .{}),
            error.MultipleMainFunctions => std.debug.print("Error: Multiple main() functions found\n", .{}),
        }
        std.process.exit(1);
    };

    // Determine output name from directory name
    const dir_name = std.fs.path.basename(cwd_path);
    const output_path = std.fmt.allocPrint(allocator, "{s}.exe", .{dir_name}) catch {
        std.debug.print("Out of memory\n", .{});
        std.process.exit(1);
    };
    defer allocator.free(output_path);

    // Build combined sources array: stdlib first, then user files
    var all_sources: std.ArrayListUnmanaged(compiler.Source) = .empty;
    defer all_sources.deinit(allocator);

    for (stdlib_modules) |mod| {
        all_sources.append(allocator, mod) catch {
            std.debug.print("Error: out of memory\n", .{});
            std.process.exit(1);
        };
    }
    for (user_sources) |src| {
        all_sources.append(allocator, src) catch {
            std.debug.print("Error: out of memory\n", .{});
            std.process.exit(1);
        };
    }

    // Print what we're building
    std.debug.print("Building project with {d} source file(s)...\n", .{user_sources.len});
    std.debug.print("  Main: {s}\n", .{main_file});

    // Compile all together
    var result: compiler.CompileResult = .{ .error_info = null };
    compiler.compileMultiple(
        all_sources.items,
        output_path,
        .{ .track_memory = track_memory, .emit_ir = emit_ir, .emit_asm = emit_asm },
        allocator,
        &result,
    ) catch |err| {
        if (result.error_info) |error_info| {
            error_info.printToStderr();
        } else {
            std.debug.print("Build failed: {}\n", .{err});
        }
        std.process.exit(1);
    };

    std.debug.print("Built: {s}\n", .{output_path});
    printEmitPaths(output_path, emit_ir, emit_asm, allocator);
}

fn printEmitPaths(output_path: []const u8, emit_ir: bool, emit_asm: bool, allocator: std.mem.Allocator) void {
    if (emit_ir) {
        const ir_path = blk: {
            if (std.mem.endsWith(u8, output_path, ".exe")) {
                const base = output_path[0 .. output_path.len - 4];
                break :blk std.fmt.allocPrint(allocator, "{s}.ir", .{base}) catch {
                    std.debug.print("Out of memory\n", .{});
                    std.process.exit(1);
                };
            } else {
                break :blk std.fmt.allocPrint(allocator, "{s}.ir", .{output_path}) catch {
                    std.debug.print("Out of memory\n", .{});
                    std.process.exit(1);
                };
            }
        };
        defer allocator.free(ir_path);
        std.debug.print("IR:        {s}\n", .{ir_path});
    }
    if (emit_asm) {
        const asm_path = blk: {
            if (std.mem.endsWith(u8, output_path, ".exe")) {
                const base = output_path[0 .. output_path.len - 4];
                break :blk std.fmt.allocPrint(allocator, "{s}.asm", .{base}) catch {
                    std.debug.print("Out of memory\n", .{});
                    std.process.exit(1);
                };
            } else {
                break :blk std.fmt.allocPrint(allocator, "{s}.asm", .{output_path}) catch {
                    std.debug.print("Out of memory\n", .{});
                    std.process.exit(1);
                };
            }
        };
        defer allocator.free(asm_path);
        std.debug.print("Assembly:  {s}\n", .{asm_path});
    }
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
        } else if (arg.len > 0 and arg[0] == '-') {
            std.debug.print("Unknown option: {s}\n", .{arg});
            std.process.exit(1);
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
    defer {
        for (summary.results) |result| {
            if (result.message) |msg| {
                allocator.free(msg);
            }
        }
        allocator.free(summary.results);
    }

    // Print summary
    summary.printSummaryDebug(elapsed_ms);

    // Exit with error if any tests failed
    if (summary.failed > 0) {
        std.process.exit(1);
    }
}

fn runLspServer(allocator: std.mem.Allocator) void {
    lsp.run(allocator) catch |err| {
        std.debug.print("LSP server error: {}\n", .{err});
        std.process.exit(1);
    };
    // Exit immediately after LSP server completes - the GPA deinit in main
    // can hang on Windows when stdout/stderr pipes are closed by the client
    std.process.exit(0);
}

// Include LSP in-process tests in unit tests
test {
    _ = @import("lsp/lsp_tests.zig");
}
