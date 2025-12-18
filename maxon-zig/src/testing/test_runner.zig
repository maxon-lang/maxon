const std = @import("std");
const testing = @import("testing.zig");
const fragment_gen = @import("fragment_generator.zig");

const SPECS_DIR = "specs";
const FRAGMENTS_DIR = "specs/fragments";
const TEMP_DIR = "specs/temp";

/// Parsed fragment file
pub const Fragment = struct {
    name: []const u8,
    source: []const u8,
    expected: testing.TestExpectation,

    pub fn deinit(self: *Fragment, allocator: std.mem.Allocator) void {
        allocator.free(self.name);
        allocator.free(self.source);
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

    // Ensure temp directory exists
    std.fs.cwd().makePath(TEMP_DIR) catch {};

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

    var results: std.ArrayListUnmanaged(testing.TestResult) = .empty;
    var total: usize = 0;
    var passed: usize = 0;
    var failed: usize = 0;
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

        // Parse fragment
        var fragment = parseFragment(allocator, content) catch |err| {
            std.debug.print("Error parsing fragment '{s}': {}\n", .{ entry.name, err });
            continue;
        };
        defer fragment.deinit(allocator);

        // Check filter
        if (options.filter) |filter| {
            if (std.mem.indexOf(u8, fragment.name, filter) == null) {
                skipped += 1;
                continue;
            }
        }

        total += 1;

        // Run the test
        const result = runTest(allocator, fragment, options);
        try results.append(allocator, result);

        switch (result.status) {
            .passed => {
                passed += 1;
                if (options.verbose) {
                    std.debug.print("PASS: {s}\n", .{fragment.name});
                } else {
                    std.debug.print(".", .{});
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

    // Clean up temp directory
    std.fs.cwd().deleteTree(TEMP_DIR) catch {};

    return testing.TestSummary{
        .total = total,
        .passed = passed,
        .failed = failed,
        .skipped = skipped,
        .results = try results.toOwnedSlice(allocator),
    };
}

/// Parse a fragment file
pub fn parseFragment(allocator: std.mem.Allocator, content: []const u8) !Fragment {
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
    const source = try allocator.dupe(u8, std.mem.trim(u8, content[first_newline + 1 .. first_sep], "\r\n"));
    errdefer allocator.free(source);

    // Find metadata section (between first and second ---)
    const metadata_start = first_sep + 5;
    const second_sep = std.mem.indexOfPos(u8, content, metadata_start, "\n---") orelse return error.InvalidFragment;
    const metadata = content[metadata_start..second_sep];

    // Parse expected output
    const expected = try parseExpected(allocator, metadata);

    return Fragment{
        .name = name,
        .source = source,
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

    var lines = std.mem.splitScalar(u8, metadata, '\n');
    while (lines.next()) |line| {
        const trimmed = std.mem.trim(u8, line, " \t\r");

        if (std.mem.startsWith(u8, trimmed, "ExitCode:")) {
            const value = std.mem.trim(u8, trimmed[9..], " \t");
            exit_code = std.fmt.parseInt(u8, value, 10) catch return error.InvalidExitCode;
        } else if (std.mem.startsWith(u8, trimmed, "Stdout:")) {
            const value = std.mem.trim(u8, trimmed[7..], " \t");
            stdout = try allocator.dupe(u8, value);
        }
    }

    return .{ .success = .{
        .exit_code = exit_code,
        .stdout = stdout,
    } };
}

/// Run a single test
fn runTest(
    allocator: std.mem.Allocator,
    fragment: Fragment,
    options: testing.TestOptions,
) testing.TestResult {
    _ = options;

    // Create temp source file
    const temp_source = std.fs.path.join(allocator, &.{ TEMP_DIR, "test.maxon" }) catch {
        return .{ .name = fragment.name, .status = .failed, .message = "Failed to create temp path" };
    };
    defer allocator.free(temp_source);

    const temp_exe = std.fs.path.join(allocator, &.{ TEMP_DIR, "test.exe" }) catch {
        return .{ .name = fragment.name, .status = .failed, .message = "Failed to create exe path" };
    };
    defer allocator.free(temp_exe);

    // Write source to temp file
    std.fs.cwd().writeFile(.{
        .sub_path = temp_source,
        .data = fragment.source,
    }) catch {
        return .{ .name = fragment.name, .status = .failed, .message = "Failed to write temp source" };
    };

    // Determine the compiler executable path
    const exe_path = getCompilerPath(allocator) catch {
        return .{ .name = fragment.name, .status = .failed, .message = "Failed to get compiler path" };
    };
    defer allocator.free(exe_path);

    // Run the compiler
    const compile_result = runCompiler(allocator, exe_path, temp_source) catch {
        return .{ .name = fragment.name, .status = .failed, .message = "Failed to run compiler" };
    };
    defer {
        allocator.free(compile_result.stdout);
        allocator.free(compile_result.stderr);
    }

    switch (fragment.expected) {
        .compiler_error => |expected_err| {
            // For error tests, compilation should fail
            if (compile_result.term.Exited == 0) {
                return .{
                    .name = fragment.name,
                    .status = .failed,
                    .message = "Expected compilation to fail, but it succeeded",
                };
            }

            // Check if stderr contains expected error
            if (std.mem.indexOf(u8, compile_result.stderr, expected_err) == null) {
                const msg = std.fmt.allocPrint(allocator, "Expected error '{s}' not found in stderr: '{s}'", .{ expected_err, compile_result.stderr }) catch "Error message mismatch";
                return .{ .name = fragment.name, .status = .failed, .message = msg };
            }

            return .{ .name = fragment.name, .status = .passed, .message = null };
        },
        .success => |expected| {
            // For success tests, compilation should succeed
            if (compile_result.term.Exited != 0) {
                const msg = std.fmt.allocPrint(allocator, "Compilation failed: {s}", .{compile_result.stderr}) catch "Compilation failed";
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

fn getCompilerPath(allocator: std.mem.Allocator) ![]const u8 {
    // Try to find the compiler executable
    // First, try the build output directory
    const paths_to_try = [_][]const u8{
        "zig-out/bin/maxon-zig.exe",
        "zig-out/bin/maxon-zig",
        "./maxon-zig.exe",
        "./maxon-zig",
    };

    for (paths_to_try) |path| {
        if (std.fs.cwd().access(path, .{})) |_| {
            return try allocator.dupe(u8, path);
        } else |_| {}
    }

    return error.CompilerNotFound;
}

fn runCompiler(allocator: std.mem.Allocator, exe_path: []const u8, source_path: []const u8) !ProcessResult {
    const result = try std.process.Child.run(.{
        .allocator = allocator,
        .argv = &[_][]const u8{ exe_path, "compile", source_path, "--no-debug" },
    });

    return ProcessResult{
        .stdout = result.stdout,
        .stderr = result.stderr,
        .term = result.term,
    };
}

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
