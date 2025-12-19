const std = @import("std");
const testing = @import("testing.zig");
const spec_parser = @import("spec_parser.zig");
const compiler = @import("../compiler/0-compiler.zig");

/// Check if fragments need regeneration based on file modification times
pub fn needsRegeneration(specs_dir: []const u8, fragments_dir: []const u8) bool {
    // Get the newest spec file mtime
    const newest_spec_mtime = getNewestMtime(specs_dir, ".md") catch return true;

    // Get the oldest fragment file mtime (or check if fragments exist)
    const oldest_fragment_mtime = getOldestMtime(fragments_dir, ".test") catch return true;

    // Regenerate if any spec is newer than the oldest fragment
    return newest_spec_mtime > oldest_fragment_mtime;
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

/// Generate fragment files from specs
pub fn generateFragments(
    allocator: std.mem.Allocator,
    specs_dir: []const u8,
    fragments_dir: []const u8,
) !testing.GenerationResult {
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

    var fragments_written: usize = 0;

    // Write each test case as a fragment (skip draft specs)
    for (specs) |spec| {
        if (spec.status == .draft) continue;
        for (spec.tests) |test_case| {
            try writeFragment(allocator, fragments_dir, test_case);
            fragments_written += 1;
        }
    }

    return testing.GenerationResult{
        .fragments_written = fragments_written,
        .specs_processed = specs.len,
    };
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
    allocator: std.mem.Allocator,
    fragments_dir: []const u8,
    test_case: testing.TestCase,
) !void {
    // Build filename: test_name.test
    const filename = try std.fmt.allocPrint(allocator, "{s}.test", .{test_case.name});
    defer allocator.free(filename);

    // Build full path
    const full_path = try std.fs.path.join(allocator, &.{ fragments_dir, filename });
    defer allocator.free(full_path);

    // Build content as a string
    var content: std.ArrayListUnmanaged(u8) = .empty;
    defer content.deinit(allocator);

    // Write test header
    const header = try std.fmt.allocPrint(allocator, "// Test: {s}\n", .{test_case.name});
    defer allocator.free(header);
    try content.appendSlice(allocator, header);

    // Write source code
    try content.appendSlice(allocator, test_case.source);
    try content.appendSlice(allocator, "\n---\n");

    // Write expected output
    switch (test_case.expected) {
        .success => |s| {
            const exit_line = try std.fmt.allocPrint(allocator, "ExitCode: {d}\n", .{s.exit_code});
            defer allocator.free(exit_line);
            try content.appendSlice(allocator, exit_line);
            if (s.stdout) |stdout| {
                const stdout_line = try std.fmt.allocPrint(allocator, "Stdout: {s}\n", .{stdout});
                defer allocator.free(stdout_line);
                try content.appendSlice(allocator, stdout_line);
            }
        },
        .compiler_error => |err| {
            try content.appendSlice(allocator, "MaxoncStderr: ```\n");
            try content.appendSlice(allocator, err);
            try content.appendSlice(allocator, "\n```\n");
        },
    }

    try content.appendSlice(allocator, "---\n");

    // Generate and include IR (for informational purposes)
    switch (test_case.expected) {
        .success => {
            // Generate IR for success tests
            const ir = compiler.compileToIr(test_case.source, allocator) catch |err| {
                // If IR generation fails, include error message
                const err_msg = try std.fmt.allocPrint(allocator, "// IR generation failed: {}\n", .{err});
                defer allocator.free(err_msg);
                try content.appendSlice(allocator, err_msg);
                try content.appendSlice(allocator, "---\n");

                // Write to file
                const file = try std.fs.cwd().createFile(full_path, .{});
                defer file.close();
                try file.writeAll(content.items);
                return;
            };
            defer allocator.free(ir);
            try content.appendSlice(allocator, ir);
        },
        .compiler_error => {
            // No IR for error tests
            try content.appendSlice(allocator, "// IR: N/A (compiler error test)\n");
        },
    }

    try content.appendSlice(allocator, "---\n");

    // Write to file
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

    std.debug.print("Specs changed, regenerating fragments...\n", .{});
    const result = try generateFragments(allocator, specs_dir, fragments_dir);
    std.debug.print("Generated {d} fragments from {d} specs\n", .{ result.fragments_written, result.specs_processed });

    return true;
}
