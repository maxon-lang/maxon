const std = @import("std");
const testing = @import("testing.zig");
const spec_parser = @import("spec_parser.zig");
const compiler = @import("../compiler/0-compiler.zig");
const debug = compiler.debug;

/// Check if fragments need regeneration based on file modification times
pub fn needsRegeneration(specs_dir: []const u8, fragments_dir: []const u8) bool {
    // Get the newest spec file mtime and count
    const spec_info = getMtimeAndCount(specs_dir, ".md") catch return true;

    // Get the oldest fragment file mtime (or check if fragments exist)
    const oldest_fragment_mtime = getOldestMtime(fragments_dir, ".test") catch return true;

    // Get fragment count to detect added/removed specs
    const fragment_count = getFileCount(fragments_dir, ".test") catch return true;

    // Regenerate if no fragments exist
    if (fragment_count == 0) return true;

    // Check if spec count matches stored count in .spec_count file
    // This detects newly added specs even if their mtime is old (e.g., copied files)
    if (specCountChanged(fragments_dir, spec_info.count)) return true;

    // Regenerate if any spec is newer than the oldest fragment
    if (spec_info.newest_mtime > oldest_fragment_mtime) return true;

    // Also regenerate if the compiler binary is newer than the oldest fragment
    const compiler_mtime = getCompilerMtime() catch return true;
    return compiler_mtime > oldest_fragment_mtime;
}

fn specCountChanged(fragments_dir: []const u8, current_count: usize) bool {
    // Read stored spec count from .spec_count file
    var path_buf: [std.fs.max_path_bytes]u8 = undefined;
    const count_file_path = std.fmt.bufPrint(&path_buf, "{s}{c}.spec_count", .{ fragments_dir, std.fs.path.sep }) catch return true;

    const content = std.fs.cwd().readFileAlloc(std.heap.page_allocator, count_file_path, 64) catch return true;
    defer std.heap.page_allocator.free(content);

    const stored_count = std.fmt.parseInt(usize, std.mem.trim(u8, content, " \t\r\n"), 10) catch return true;
    return stored_count != current_count;
}

fn getCompilerMtime() !i128 {
    const exe_path = std.fs.selfExePath(&self_exe_buf) catch return error.NoFilesFound;
    const file = std.fs.openFileAbsolute(exe_path, .{}) catch return error.NoFilesFound;
    defer file.close();
    const stat = file.stat() catch return error.NoFilesFound;
    return stat.mtime;
}

var self_exe_buf: [std.fs.max_path_bytes]u8 = undefined;

const MtimeInfo = struct {
    newest_mtime: i128,
    count: usize,
};

fn getMtimeAndCount(dir_path: []const u8, extension: []const u8) !MtimeInfo {
    var dir = try std.fs.cwd().openDir(dir_path, .{ .iterate = true });
    defer dir.close();

    var newest: i128 = 0;
    var count: usize = 0;
    var iter = dir.iterate();
    while (try iter.next()) |entry| {
        if (entry.kind != .file) continue;
        if (!std.mem.endsWith(u8, entry.name, extension)) continue;

        const stat = try dir.statFile(entry.name);
        if (stat.mtime > newest) {
            newest = stat.mtime;
        }
        count += 1;
    }

    if (count == 0) return error.NoFilesFound;
    return .{ .newest_mtime = newest, .count = count };
}

fn getFileCount(dir_path: []const u8, extension: []const u8) !usize {
    var dir = std.fs.cwd().openDir(dir_path, .{ .iterate = true }) catch return error.NoFilesFound;
    defer dir.close();

    var count: usize = 0;
    var iter = dir.iterate();
    while (try iter.next()) |entry| {
        if (entry.kind != .file) continue;
        if (!std.mem.endsWith(u8, entry.name, extension)) continue;
        count += 1;
    }

    return count;
}

fn getNewestMtime(dir_path: []const u8, extension: []const u8) !i128 {
    var dir = try std.fs.cwd().openDir(dir_path, .{ .iterate = true });
    defer dir.close();

    var newest: i128 = 0;
    var iter = dir.iterate();
    while (try iter.next()) |entry| {
        if (entry.kind != .file) continue;
        if (!std.mem.endsWith(u8, entry.name, extension)) continue;

        const stat = try dir.statFile(entry.name);
        if (stat.mtime > newest) {
            newest = stat.mtime;
        }
    }

    if (newest == 0) return error.NoFilesFound;
    return newest;
}

fn getOldestMtime(dir_path: []const u8, extension: []const u8) !i128 {
    var dir = std.fs.cwd().openDir(dir_path, .{ .iterate = true }) catch return error.NoFilesFound;
    defer dir.close();

    var oldest: i128 = std.math.maxInt(i128);
    var found_any = false;

    var iter = dir.iterate();
    while (try iter.next()) |entry| {
        if (entry.kind != .file) continue;
        if (!std.mem.endsWith(u8, entry.name, extension)) continue;

        const stat = try dir.statFile(entry.name);
        if (stat.mtime < oldest) {
            oldest = stat.mtime;
        }
        found_any = true;
    }

    if (!found_any) return error.NoFilesFound;
    return oldest;
}

const FragmentWorkerContext = struct {
    test_cases: []testing.TestCase,
    fragments_dir: []const u8,
    next_index: std.atomic.Value(usize),
    error_count: std.atomic.Value(usize),
    allocator: std.mem.Allocator,
};

fn fragmentWorkerThread(ctx: *FragmentWorkerContext) void {
    while (true) {
        const index = ctx.next_index.fetchAdd(1, .seq_cst);
        if (index >= ctx.test_cases.len) {
            break;
        }

        const test_case = ctx.test_cases[index];
        writeFragment(ctx.allocator, ctx.fragments_dir, test_case) catch {
            _ = ctx.error_count.fetchAdd(1, .seq_cst);
        };
    }
}

/// Generate fragment files from specs
pub fn generateFragments(
    allocator: std.mem.Allocator,
    specs_dir: []const u8,
    fragments_dir: []const u8,
) !testing.GenerationResult {
    // Suppress compiler debug output during fragment generation
    const was_enabled = debug.enabled;
    debug.enabled = false;
    defer debug.enabled = was_enabled;

    // Parse all specs
    const specs = try spec_parser.parseAllSpecs(allocator, specs_dir);
    defer {
        for (specs) |*spec| {
            var s = spec.*;
            s.deinit(allocator);
        }
        allocator.free(specs);
    }

    // Ensure fragments directory exists (create if needed, delete old files)
    try ensureFragmentsDir(fragments_dir);

    // Collect all test cases from non-draft specs
    var test_cases_list: std.ArrayListUnmanaged(testing.TestCase) = .empty;
    defer test_cases_list.deinit(allocator);

    for (specs) |spec| {
        if (spec.status == .draft) continue;
        for (spec.tests) |test_case| {
            try test_cases_list.append(allocator, test_case);
        }
    }

    const total = test_cases_list.items.len;
    if (total == 0) {
        return testing.GenerationResult{
            .fragments_written = 0,
            .specs_processed = specs.len,
        };
    }

    // Determine number of workers
    const cpu_count = std.Thread.getCpuCount() catch 4;
    const num_workers = @min(@max(1, cpu_count / 2), total);

    if (num_workers == 1) {
        // Single-threaded path
        for (test_cases_list.items) |test_case| {
            try writeFragment(allocator, fragments_dir, test_case);
        }
    } else {
        // Multi-threaded path
        var ctx = FragmentWorkerContext{
            .test_cases = test_cases_list.items,
            .fragments_dir = fragments_dir,
            .next_index = std.atomic.Value(usize).init(0),
            .error_count = std.atomic.Value(usize).init(0),
            .allocator = allocator,
        };

        const workers = try allocator.alloc(std.Thread, num_workers);
        defer allocator.free(workers);

        for (workers) |*worker| {
            worker.* = try std.Thread.spawn(.{}, fragmentWorkerThread, .{&ctx});
        }

        for (workers) |worker| {
            worker.join();
        }

        if (ctx.error_count.load(.seq_cst) > 0) {
            return error.FragmentGenerationFailed;
        }
    }

    // Write spec count file for change detection
    writeSpecCount(fragments_dir, specs.len);

    return testing.GenerationResult{
        .fragments_written = total,
        .specs_processed = specs.len,
    };
}

fn writeSpecCount(fragments_dir: []const u8, count: usize) void {
    var path_buf: [std.fs.max_path_bytes]u8 = undefined;
    const count_file_path = std.fmt.bufPrint(&path_buf, "{s}{c}.spec_count", .{ fragments_dir, std.fs.path.sep }) catch return;

    var content_buf: [32]u8 = undefined;
    const content = std.fmt.bufPrint(&content_buf, "{d}", .{count}) catch return;

    const file = std.fs.cwd().createFile(count_file_path, .{}) catch return;
    defer file.close();
    file.writeAll(content) catch return;
}

fn ensureFragmentsDir(fragments_dir: []const u8) !void {
    // Try to delete existing fragments directory
    std.fs.cwd().deleteTree(fragments_dir) catch |err| {
        if (err != error.FileNotFound) {
            // Directory might not exist, that's ok
        }
    };

    // Create fresh directory
    try std.fs.cwd().makePath(fragments_dir);
}

fn writeFragment(
    _: std.mem.Allocator,
    fragments_dir: []const u8,
    test_case: testing.TestCase,
) !void {
    // Use arena allocator to avoid GPA contention in multi-threaded context
    var arena = std.heap.ArenaAllocator.init(std.heap.page_allocator);
    defer arena.deinit();
    const allocator = arena.allocator();

    // Build filename: test_name.test
    const filename = try std.fmt.allocPrint(allocator, "{s}.test", .{test_case.name});

    // Build full path (relative for file operations)
    const full_path = try std.fs.path.join(allocator, &.{ fragments_dir, filename });

    // Build content as a string
    var content: std.ArrayListUnmanaged(u8) = .empty;

    // Write test header
    const header = try std.fmt.allocPrint(allocator, "// Test: {s}\n", .{test_case.name});
    try content.appendSlice(allocator, header);

    // Write source code
    try content.appendSlice(allocator, test_case.source);
    try content.appendSlice(allocator, "\n---\n");

    // Write expected output
    switch (test_case.expected) {
        .success => |s| {
            const exit_line = try std.fmt.allocPrint(allocator, "ExitCode: {d}\n", .{s.exit_code});
            try content.appendSlice(allocator, exit_line);
            if (s.track_allocs) {
                try content.appendSlice(allocator, "TrackAllocs: true\n");
            }
            if (s.stdout) |stdout| {
                // Use multiline format with ``` block if stdout contains newlines
                if (std.mem.indexOf(u8, stdout, "\n") != null) {
                    try content.appendSlice(allocator, "Stdout: ```\n");
                    try content.appendSlice(allocator, stdout);
                    try content.appendSlice(allocator, "\n```\n");
                } else {
                    const stdout_line = try std.fmt.allocPrint(allocator, "Stdout: {s}\n", .{stdout});
                    try content.appendSlice(allocator, stdout_line);
                }
            }
        },
        .compiler_error => |err| {
            try content.appendSlice(allocator, "MaxoncStderr: ```\n");
            try content.appendSlice(allocator, err);
            try content.appendSlice(allocator, "\n```\n");
        },
    }

    try content.appendSlice(allocator, "---\n");

    // Write partial file first so it exists for error messages
    {
        const file = try std.fs.cwd().createFile(full_path, .{});
        defer file.close();
        try file.writeAll(content.items);
    }

    // Generate and include IR (for informational purposes)
    switch (test_case.expected) {
        .success => {
            // Generate IR for success tests - this must succeed
            var compile_result: compiler.CompileResult = .{ .error_info = null };
            const ir = compiler.compileToIrWithResult(test_case.source, allocator, &compile_result, full_path) catch |err| {
                // IR generation failed - print detailed error info
                std.debug.print("IR generation failed for test '{s}': {}\n", .{ test_case.name, err });
                if (compile_result.error_info) |error_info| {
                    error_info.printToStderr();
                }
                return err;
            };
            try content.appendSlice(allocator, ir);
        },
        .compiler_error => {
            // No IR for error tests
            try content.appendSlice(allocator, "// IR: N/A (compiler error test)\n");
        },
    }

    try content.appendSlice(allocator, "---\n");

    // Write final file with IR
    const file = try std.fs.cwd().createFile(full_path, .{});
    defer file.close();
    try file.writeAll(content.items);
}

/// Regenerate fragments if needed, returns true if regenerated
pub fn regenerateIfNeeded(
    allocator: std.mem.Allocator,
    specs_dir: []const u8,
    fragments_dir: []const u8,
) !bool {
    if (!needsRegeneration(specs_dir, fragments_dir)) {
        return false;
    }

    std.debug.print("Specs or compiler changed, regenerating fragments...\n", .{});
    var timer = std.time.Timer.start() catch unreachable;
    const result = try generateFragments(allocator, specs_dir, fragments_dir);
    const elapsed_ns = timer.read();
    const elapsed_ms = @as(f64, @floatFromInt(elapsed_ns)) / 1_000_000.0;
    std.debug.print("Generated {d} fragments from {d} specs in {d:.0}ms\n", .{ result.fragments_written, result.specs_processed, elapsed_ms });

    return true;
}
