const std = @import("std");
const testing = @import("testing.zig");
const fragment_gen = @import("fragment_generator.zig");
const compiler = @import("../compiler/0-compiler.zig");

const SPECS_DIR = "specs";
const FRAGMENTS_DIR = "specs" ++ std.fs.path.sep_str ++ "fragments";

const WorkerContext = struct {
    fragments: []Fragment,
    results: []testing.TestResult,
    next_index: std.atomic.Value(usize),
    completed_count: std.atomic.Value(usize),
    allocator: std.mem.Allocator,
    options: testing.TestOptions,
    print_mutex: std.Thread.Mutex,
    stdlib_modules: []const compiler.Source,
};

/// Cached stdlib modules (loaded once, reused across all tests)
var cached_stdlib: ?[]const compiler.Source = null;
var stdlib_allocator: ?std.mem.Allocator = null;

fn getStdlibModules(allocator: std.mem.Allocator) ![]const compiler.Source {
    if (cached_stdlib) |modules| {
        return modules;
    }

    const stdlib_path = try compiler.findStdlibPath(allocator);
    defer allocator.free(stdlib_path);

    cached_stdlib = try compiler.loadAllStdlibModules(stdlib_path, allocator);
    stdlib_allocator = allocator;
    return cached_stdlib.?;
}

fn freeStdlibCache() void {
    if (cached_stdlib) |modules| {
        if (stdlib_allocator) |alloc| {
            for (modules) |mod| {
                alloc.free(mod.path);
                alloc.free(mod.content);
            }
            alloc.free(modules);
        }
    }
    cached_stdlib = null;
    stdlib_allocator = null;
}

/// Extract the error line from compiler stderr (line starting with "error ")
fn extractErrorLine(stderr: []const u8) ?[]const u8 {
    var lines = std.mem.splitScalar(u8, stderr, '\n');
    while (lines.next()) |line| {
        if (std.mem.startsWith(u8, line, "error ")) {
            return line;
        }
    }
    return null;
}

/// Parsed fragment file
pub const Fragment = struct {
    name: []const u8,
    source: []const u8,
    file_path: []const u8,
    expected: testing.TestExpectation,

    pub fn deinit(self: *Fragment, allocator: std.mem.Allocator) void {
        allocator.free(self.name);
        allocator.free(self.source);
        allocator.free(self.file_path);
        switch (self.expected) {
            .success => |s| {
                if (s.stdout) |stdout| allocator.free(stdout);
            },
            .compiler_error => |err| allocator.free(err),
        }
    }
};

/// Run all tests
pub fn runAllTests(
    allocator: std.mem.Allocator,
    options: testing.TestOptions,
) !testing.TestSummary {
    // Regenerate fragments if specs changed
    _ = fragment_gen.regenerateIfNeeded(allocator, SPECS_DIR, FRAGMENTS_DIR) catch |err| {
        std.debug.print("Warning: Could not check/regenerate fragments: {}\n", .{err});
    };

    // Collect all fragment files
    var fragments_dir = std.fs.cwd().openDir(FRAGMENTS_DIR, .{ .iterate = true }) catch |err| {
        std.debug.print("Error opening fragments directory: {}\n", .{err});
        return testing.TestSummary{
            .total = 0,
            .passed = 0,
            .failed = 0,
            .skipped = 0,
            .results = &.{},
        };
    };
    defer fragments_dir.close();

    // First pass: collect all fragments
    var fragments_list: std.ArrayListUnmanaged(Fragment) = .empty;
    defer {
        for (fragments_list.items) |*f| {
            f.deinit(allocator);
        }
        fragments_list.deinit(allocator);
    }

    var skipped: usize = 0;
    var iter = fragments_dir.iterate();
    while (try iter.next()) |entry| {
        if (entry.kind != .file) continue;
        if (!std.mem.endsWith(u8, entry.name, ".test")) continue;

        // Read fragment file
        const content = fragments_dir.readFileAlloc(allocator, entry.name, 1024 * 1024) catch |err| {
            std.debug.print("Error reading fragment '{s}': {}\n", .{ entry.name, err });
            continue;
        };
        defer allocator.free(content);

        // Build full path to fragment file
        const fragment_path = std.fs.path.join(allocator, &.{ FRAGMENTS_DIR, entry.name }) catch |err| {
            std.debug.print("Error building path for '{s}': {}\n", .{ entry.name, err });
            continue;
        };
        defer allocator.free(fragment_path);

        // Parse fragment
        var fragment = parseFragment(allocator, content, fragment_path) catch |err| {
            std.debug.print("Error parsing fragment '{s}': {}\n", .{ entry.name, err });
            continue;
        };

        // Check filter
        if (options.filter) |filter| {
            if (std.mem.indexOf(u8, fragment.name, filter) == null) {
                skipped += 1;
                fragment.deinit(allocator);
                continue;
            }
        }

        try fragments_list.append(allocator, fragment);
    }

    const total = fragments_list.items.len;
    if (total == 0) {
        return testing.TestSummary{
            .total = 0,
            .passed = 0,
            .failed = 0,
            .skipped = skipped,
            .results = &.{},
        };
    }

    // Load stdlib once for all tests
    const stdlib_modules = getStdlibModules(allocator) catch {
        std.debug.print("Error: Failed to load stdlib\n", .{});
        return testing.TestSummary{
            .total = 0,
            .passed = 0,
            .failed = 0,
            .skipped = skipped,
            .results = &.{},
        };
    };
    defer freeStdlibCache();

    // Allocate results array
    const results = try allocator.alloc(testing.TestResult, total);

    // Determine number of workers (default to half CPU count for in-process compilation)
    const cpu_count = std.Thread.getCpuCount() catch 4;
    const default_workers = @max(1, cpu_count / 2);
    const num_workers = if (options.jobs == 0)
        @min(default_workers, total)
    else
        @min(options.jobs, total);

    // Single-threaded fast path
    if (num_workers == 1) {
        std.debug.print("Running {d} tests with 1 worker\n", .{total});
        return runTestsSequential(allocator, fragments_list.items, results, options, skipped, stdlib_modules);
    }

    std.debug.print("Running {d} tests with {d} workers\n", .{ total, num_workers });

    // Set up worker context
    var ctx = WorkerContext{
        .fragments = fragments_list.items,
        .results = results,
        .next_index = std.atomic.Value(usize).init(0),
        .completed_count = std.atomic.Value(usize).init(0),
        .allocator = allocator,
        .options = options,
        .print_mutex = .{},
        .stdlib_modules = stdlib_modules,
    };

    // Spawn worker threads
    const workers = try allocator.alloc(std.Thread, num_workers);
    defer allocator.free(workers);

    for (workers) |*worker| {
        worker.* = try std.Thread.spawn(.{}, workerThread, .{&ctx});
    }

    // Wait for all workers to complete
    for (workers) |worker| {
        worker.join();
    }

    // Count results
    var passed: usize = 0;
    var failed: usize = 0;
    for (results) |result| {
        switch (result.status) {
            .passed => passed += 1,
            .failed => failed += 1,
            .skipped => skipped += 1,
        }
    }

    // Clean up generated .exe and .ir files in fragments directory
    var cleanup_iter = fragments_dir.iterate();
    while (cleanup_iter.next() catch null) |entry| {
        if (entry.kind != .file) continue;
        if (std.mem.endsWith(u8, entry.name, ".exe") or std.mem.endsWith(u8, entry.name, ".ir")) {
            fragments_dir.deleteFile(entry.name) catch {};
        }
    }

    return testing.TestSummary{
        .total = total,
        .passed = passed,
        .failed = failed,
        .skipped = skipped,
        .results = results,
    };
}

/// Sequential test execution (single thread)
fn runTestsSequential(
    allocator: std.mem.Allocator,
    fragments: []Fragment,
    results: []testing.TestResult,
    options: testing.TestOptions,
    initial_skipped: usize,
    stdlib_modules: []const compiler.Source,
) testing.TestSummary {
    var passed: usize = 0;
    var failed: usize = 0;
    var skipped = initial_skipped;

    for (fragments, 0..) |fragment, i| {
        const result = runTest(allocator, fragment, options, stdlib_modules);
        results[i] = result;

        switch (result.status) {
            .passed => {
                passed += 1;
                if (options.verbose) {
                    std.debug.print("PASS: {s}\n", .{fragment.name});
                }
            },
            .failed => {
                failed += 1;
                std.debug.print("\nFAIL: {s}\n", .{fragment.name});
                if (result.message) |msg| {
                    std.debug.print("  {s}\n", .{msg});
                }
            },
            .skipped => {
                skipped += 1;
            },
        }
    }

    return testing.TestSummary{
        .total = fragments.len,
        .passed = passed,
        .failed = failed,
        .skipped = skipped,
        .results = results,
    };
}

/// Worker thread function
fn workerThread(ctx: *WorkerContext) void {
    while (true) {
        // Atomically get next test index
        const index = ctx.next_index.fetchAdd(1, .seq_cst);
        if (index >= ctx.fragments.len) {
            break;
        }

        const fragment = ctx.fragments[index];
        const result = runTest(ctx.allocator, fragment, ctx.options, ctx.stdlib_modules);
        ctx.results[index] = result;

        // Print result with mutex to avoid interleaved output
        {
            ctx.print_mutex.lock();
            defer ctx.print_mutex.unlock();

            switch (result.status) {
                .passed => {
                    if (ctx.options.verbose) {
                        std.debug.print("PASS: {s}\n", .{fragment.name});
                    }
                },
                .failed => {
                    std.debug.print("\nFAIL: {s}\n", .{fragment.name});
                    if (result.message) |msg| {
                        std.debug.print("  {s}\n", .{msg});
                    }
                },
                .skipped => {},
            }
        }

        _ = ctx.completed_count.fetchAdd(1, .seq_cst);
    }
}

/// Parse a fragment file
pub fn parseFragment(allocator: std.mem.Allocator, content: []const u8, file_path: []const u8) !Fragment {
    // Format:
    // // Test: name
    // <source>
    // ---
    // ExitCode: N / MaxoncStderr: ```...```
    // ---

    // Find test name (first line)
    const first_newline = std.mem.indexOf(u8, content, "\n") orelse return error.InvalidFragment;
    const first_line = std.mem.trim(u8, content[0..first_newline], " \t\r");

    if (!std.mem.startsWith(u8, first_line, "// Test: ")) {
        return error.InvalidFragment;
    }
    const name = try allocator.dupe(u8, first_line[9..]);
    errdefer allocator.free(name);

    // Find first ---
    const first_sep = std.mem.indexOf(u8, content, "\n---\n") orelse return error.InvalidFragment;
    // Include the "// Test:" comment line in source to preserve line numbers for error messages
    const source = try allocator.dupe(u8, std.mem.trim(u8, content[0..first_sep], "\r\n"));
    errdefer allocator.free(source);

    // Store file path
    const path = try allocator.dupe(u8, file_path);
    errdefer allocator.free(path);

    // Find metadata section (between first and second ---)
    const metadata_start = first_sep + 5;
    const second_sep = std.mem.indexOfPos(u8, content, metadata_start, "\n---") orelse return error.InvalidFragment;
    const metadata = content[metadata_start..second_sep];

    // Parse expected output
    const expected = try parseExpected(allocator, metadata);

    return Fragment{
        .name = name,
        .source = source,
        .file_path = path,
        .expected = expected,
    };
}

fn parseExpected(allocator: std.mem.Allocator, metadata: []const u8) !testing.TestExpectation {
    // Check for MaxoncStderr (error test)
    if (std.mem.indexOf(u8, metadata, "MaxoncStderr:")) |_| {
        // Extract error message between ``` markers
        const start = std.mem.indexOf(u8, metadata, "```") orelse return error.InvalidFragment;
        const after_start = start + 3;
        // Skip newline after opening ```
        const content_start = if (after_start < metadata.len and metadata[after_start] == '\n') after_start + 1 else after_start;
        const end = std.mem.indexOfPos(u8, metadata, content_start, "```") orelse return error.InvalidFragment;

        const error_msg = try allocator.dupe(u8, std.mem.trim(u8, metadata[content_start..end], "\r\n"));
        return .{ .compiler_error = error_msg };
    }

    // Success test
    var exit_code: u8 = 0;
    var stdout: ?[]const u8 = null;
    var track_allocs: bool = false;

    var lines = std.mem.splitScalar(u8, metadata, '\n');
    while (lines.next()) |line| {
        const trimmed = std.mem.trim(u8, line, " \t\r");

        if (std.mem.startsWith(u8, trimmed, "ExitCode:")) {
            const value = std.mem.trim(u8, trimmed[9..], " \t");
            exit_code = std.fmt.parseInt(u8, value, 10) catch return error.InvalidExitCode;
        } else if (std.mem.startsWith(u8, trimmed, "Stdout:")) {
            const value = std.mem.trim(u8, trimmed[7..], " \t");
            // Check for multiline block format: Stdout: ```
            if (std.mem.eql(u8, value, "```")) {
                // Collect lines until closing ```
                var content_builder: std.ArrayListUnmanaged(u8) = .empty;
                errdefer content_builder.deinit(allocator);

                while (lines.next()) |content_line| {
                    const content_trimmed = std.mem.trimRight(u8, content_line, "\r");
                    if (std.mem.eql(u8, content_trimmed, "```")) {
                        break;
                    }
                    if (content_builder.items.len > 0) {
                        try content_builder.append(allocator, '\n');
                    }
                    try content_builder.appendSlice(allocator, content_trimmed);
                }
                stdout = try content_builder.toOwnedSlice(allocator);
            } else {
                stdout = try allocator.dupe(u8, value);
            }
        } else if (std.mem.startsWith(u8, trimmed, "TrackAllocs:")) {
            const value = std.mem.trim(u8, trimmed[12..], " \t");
            track_allocs = std.mem.eql(u8, value, "true");
        }
    }

    return .{ .success = .{
        .exit_code = exit_code,
        .stdout = stdout,
        .track_allocs = track_allocs,
    } };
}

/// Run a single test using in-process compilation
fn runTest(
    allocator: std.mem.Allocator,
    fragment: Fragment,
    options: testing.TestOptions,
    stdlib_modules: []const compiler.Source,
) testing.TestResult {
    _ = options;

    // Compute output exe path (replace .test with .exe)
    const temp_exe = blk: {
        if (std.mem.endsWith(u8, fragment.file_path, ".test")) {
            const base = fragment.file_path[0 .. fragment.file_path.len - 5];
            break :blk std.fmt.allocPrint(allocator, "{s}.exe", .{base}) catch {
                return .{ .name = fragment.name, .status = .failed, .message = "Failed to create exe path" };
            };
        } else {
            break :blk std.fmt.allocPrint(allocator, "{s}.exe", .{fragment.file_path}) catch {
                return .{ .name = fragment.name, .status = .failed, .message = "Failed to create exe path" };
            };
        }
    };
    defer allocator.free(temp_exe);

    // Get track_allocs option from expected result
    const track_allocs = switch (fragment.expected) {
        .success => |s| s.track_allocs,
        .compiler_error => false,
    };

    // Compile in-process using the compiler module directly
    // Disable debug logging to avoid polluting test output
    compiler.debug.enabled = false;

    // Use an arena allocator backed by the page allocator to avoid GPA contention
    var arena = std.heap.ArenaAllocator.init(std.heap.page_allocator);
    defer arena.deinit();
    const compile_allocator = arena.allocator();

    // Build sources array: stdlib first, then user code
    const sources = compile_allocator.alloc(compiler.Source, stdlib_modules.len + 1) catch {
        return .{ .name = fragment.name, .status = .failed, .message = "Failed to allocate sources" };
    };

    for (stdlib_modules, 0..) |mod, i| {
        sources[i] = mod;
    }
    sources[stdlib_modules.len] = .{ .path = fragment.file_path, .content = fragment.source };

    var compile_result: compiler.CompileResult = .{ .error_info = null };
    const compile_options = compiler.CompileOptions{ .track_allocs = track_allocs };

    const compile_success = if (compiler.compileMultiple(
        sources,
        temp_exe,
        compile_options,
        compile_allocator,
        &compile_result,
    )) |_| true else |_| false;

    switch (fragment.expected) {
        .compiler_error => |expected_err| {
            // For error tests, compilation should fail
            if (compile_success) {
                return .{
                    .name = fragment.name,
                    .status = .failed,
                    .message = "Expected compilation to fail, but it succeeded",
                };
            }

            // Format the actual error message
            var actual_error_allocated = false;
            const actual_error = if (compile_result.error_info) |err| blk: {
                actual_error_allocated = true;
                break :blk std.fmt.allocPrint(allocator, "error {s}: {s}:{d}:{d}: {s}", .{
                    err.code.format(),
                    err.location.file orelse "",
                    err.location.line,
                    err.location.column,
                    err.message,
                }) catch {
                    actual_error_allocated = false;
                    break :blk "Failed to format error";
                };
            } else "Unknown compilation error";
            defer if (actual_error_allocated) allocator.free(actual_error);

            // Check for exact match of error line
            if (!std.mem.eql(u8, std.mem.trim(u8, actual_error, " \t\r\n"), std.mem.trim(u8, expected_err, " \t\r\n"))) {
                const msg = std.fmt.allocPrint(allocator, "Expected error:\n  {s}\nActual error:\n  {s}", .{ expected_err, actual_error }) catch "Error message mismatch";
                return .{ .name = fragment.name, .status = .failed, .message = msg };
            }

            return .{ .name = fragment.name, .status = .passed, .message = null };
        },
        .success => |expected| {
            // For success tests, compilation should succeed
            if (!compile_success) {
                const msg = if (compile_result.error_info) |err| blk: {
                    break :blk std.fmt.allocPrint(allocator, "Compilation failed: {s}", .{err.message}) catch "Compilation failed";
                } else "Compilation failed";
                return .{ .name = fragment.name, .status = .failed, .message = msg };
            }

            // Run the executable
            const run_result = runExecutable(allocator, temp_exe) catch {
                return .{ .name = fragment.name, .status = .failed, .message = "Failed to run executable" };
            };
            defer {
                allocator.free(run_result.stdout);
                allocator.free(run_result.stderr);
            }

            // Check exit code
            const actual_exit_code: u8 = @intCast(run_result.term.Exited);
            if (actual_exit_code != expected.exit_code) {
                const msg = std.fmt.allocPrint(allocator, "Expected exit code {d}, got {d}", .{ expected.exit_code, actual_exit_code }) catch "Exit code mismatch";
                return .{ .name = fragment.name, .status = .failed, .message = msg };
            }

            // Check stdout if specified
            if (expected.stdout) |exp_stdout| {
                const actual_stdout = std.mem.trim(u8, run_result.stdout, " \t\r\n");
                if (!std.mem.eql(u8, actual_stdout, exp_stdout)) {
                    const msg = std.fmt.allocPrint(allocator, "Expected stdout '{s}', got '{s}'", .{ exp_stdout, actual_stdout }) catch "Stdout mismatch";
                    return .{ .name = fragment.name, .status = .failed, .message = msg };
                }
            }

            return .{ .name = fragment.name, .status = .passed, .message = null };
        },
    }
}

const ProcessResult = struct {
    stdout: []const u8,
    stderr: []const u8,
    term: std.process.Child.Term,
};

fn runExecutable(allocator: std.mem.Allocator, exe_path: []const u8) !ProcessResult {
    const result = try std.process.Child.run(.{
        .allocator = allocator,
        .argv = &[_][]const u8{exe_path},
    });

    return ProcessResult{
        .stdout = result.stdout,
        .stderr = result.stderr,
        .term = result.term,
    };
}
