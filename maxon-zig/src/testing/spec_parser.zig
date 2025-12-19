const std = @import("std");
const testing = @import("testing.zig");

pub const ParseError = error{
    InvalidFrontmatter,
    MissingCodeBlock,
    MissingExpectedOutput,
    InvalidExitCode,
    OutOfMemory,
    UnexpectedEndOfInput,
};

/// Parse a single spec file from its content
pub fn parseSpec(
    allocator: std.mem.Allocator,
    spec_name: []const u8,
    content: []const u8,
) ParseError!testing.SpecFile {
    // Parse frontmatter
    const frontmatter = try parseFrontmatter(content);

    // Find ## Tests section and extract tests
    var test_cases: std.ArrayListUnmanaged(testing.TestCase) = .empty;
    errdefer {
        for (test_cases.items) |*tc| {
            tc.deinit(allocator);
        }
        test_cases.deinit(allocator);
    }

    // Find the Tests section
    if (findSection(content, "## Tests")) |tests_section| {
        try extractTestsFromSection(allocator, spec_name, tests_section, &test_cases);
    }

    // Also extract executable doc examples from Documentation section
    if (findSection(content, "## Documentation")) |doc_section| {
        // Limit doc section to before ## Tests if it exists
        const doc_end = if (std.mem.indexOf(u8, doc_section, "## Tests")) |idx| idx else doc_section.len;
        const limited_doc = doc_section[0..doc_end];
        try extractDocExamples(allocator, spec_name, limited_doc, &test_cases);
    }

    return testing.SpecFile{
        .name = spec_name,
        .feature = frontmatter.feature,
        .status = frontmatter.status,
        .category = frontmatter.category,
        .tests = try test_cases.toOwnedSlice(allocator),
    };
}

const Frontmatter = struct {
    feature: []const u8,
    status: testing.SpecFile.Status,
    category: []const u8,
};

fn parseFrontmatter(content: []const u8) ParseError!Frontmatter {
    // Find opening ---
    const start = std.mem.indexOf(u8, content, "---") orelse return ParseError.InvalidFrontmatter;
    const after_start = start + 3;

    // Find closing ---
    const end = std.mem.indexOfPos(u8, content, after_start, "---") orelse return ParseError.InvalidFrontmatter;

    const frontmatter_content = content[after_start..end];

    var feature: []const u8 = "";
    var status: testing.SpecFile.Status = .stable;
    var category: []const u8 = "uncategorized";

    // Parse line by line
    var lines = std.mem.splitScalar(u8, frontmatter_content, '\n');
    while (lines.next()) |line| {
        const trimmed = std.mem.trim(u8, line, " \t\r");
        if (trimmed.len == 0) continue;

        if (std.mem.indexOf(u8, trimmed, ":")) |colon_idx| {
            const key = std.mem.trim(u8, trimmed[0..colon_idx], " \t");
            const value = std.mem.trim(u8, trimmed[colon_idx + 1 ..], " \t");

            if (std.mem.eql(u8, key, "feature")) {
                feature = value;
            } else if (std.mem.eql(u8, key, "status")) {
                status = testing.SpecFile.Status.fromString(value) orelse .stable;
            } else if (std.mem.eql(u8, key, "category")) {
                category = value;
            }
        }
    }

    return Frontmatter{
        .feature = feature,
        .status = status,
        .category = category,
    };
}

fn findSection(content: []const u8, section_header: []const u8) ?[]const u8 {
    const idx = std.mem.indexOf(u8, content, section_header) orelse return null;
    return content[idx + section_header.len ..];
}

fn extractTestsFromSection(
    allocator: std.mem.Allocator,
    spec_name: []const u8,
    section: []const u8,
    test_cases: *std.ArrayListUnmanaged(testing.TestCase),
) ParseError!void {
    var pos: usize = 0;

    while (pos < section.len) {
        // Look for test marker: <!-- test: name --> (note: colon may have space after)
        const marker_start = std.mem.indexOfPos(u8, section, pos, "<!-- test:") orelse break;
        const marker_end = std.mem.indexOfPos(u8, section, marker_start, "-->") orelse break;

        // Extract test name
        const marker_content = section[marker_start + 10 .. marker_end];
        const test_name_raw = std.mem.trim(u8, marker_content, " \t\r\n");

        // Build full test name: spec_name.test_name.1
        const full_name = try std.fmt.allocPrint(allocator, "{s}.{s}.1", .{ spec_name, test_name_raw });
        errdefer allocator.free(full_name);

        pos = marker_end + 3;

        // Check for optional TrackAllocs marker before the code block
        var track_allocs = false;
        if (std.mem.indexOfPos(u8, section, pos, "<!-- TrackAllocs:")) |track_start| {
            // Only if it's before the code block
            if (std.mem.indexOfPos(u8, section, pos, "```maxon")) |code_start| {
                if (track_start < code_start) {
                    if (std.mem.indexOfPos(u8, section, track_start, "-->")) |track_end| {
                        const track_content = section[track_start + 17 .. track_end];
                        track_allocs = std.mem.indexOf(u8, track_content, "true") != null;
                    }
                }
            }
        }

        // Find the ```maxon block
        const code_block = try findCodeBlock(section, pos, "maxon") orelse {
            allocator.free(full_name);
            continue;
        };
        pos = code_block.end;

        const source = try allocator.dupe(u8, code_block.content);
        errdefer allocator.free(source);

        // Find expected output: either ```exitcode or ```maxoncstderr
        const expected = try parseExpectedOutput(allocator, section, pos) orelse {
            allocator.free(full_name);
            allocator.free(source);
            continue;
        };
        pos = expected.end_pos;

        // Apply track_allocs to success expectations
        var final_expectation = expected.expectation;
        if (track_allocs) {
            switch (final_expectation) {
                .success => |*s| s.track_allocs = true,
                .compiler_error => {},
            }
        }

        try test_cases.append(allocator, testing.TestCase{
            .name = full_name,
            .source = source,
            .expected = final_expectation,
        });
    }
}

fn extractDocExamples(
    allocator: std.mem.Allocator,
    spec_name: []const u8,
    section: []const u8,
    test_cases: *std.ArrayListUnmanaged(testing.TestCase),
) ParseError!void {
    var pos: usize = 0;
    var example_num: usize = 1;

    while (pos < section.len) {
        // Find ```maxon blocks that contain "function main()"
        const code_block = findCodeBlock(section, pos, "maxon") catch break orelse break;
        pos = code_block.end;

        // Check if this is an executable example (has function main)
        if (std.mem.indexOf(u8, code_block.content, "function main()") == null) {
            continue;
        }

        // Try to find expected output immediately after
        const expected = parseExpectedOutput(allocator, section, pos) catch continue orelse continue;
        pos = expected.end_pos;

        // Build doc example name
        const full_name = try std.fmt.allocPrint(allocator, "{s}.doc-example-{d}.1", .{ spec_name, example_num });
        errdefer allocator.free(full_name);

        const source = try allocator.dupe(u8, code_block.content);
        errdefer allocator.free(source);

        try test_cases.append(allocator, testing.TestCase{
            .name = full_name,
            .source = source,
            .expected = expected.expectation,
        });

        example_num += 1;
    }
}

const CodeBlock = struct {
    content: []const u8,
    end: usize,
};

fn findCodeBlock(content: []const u8, start_pos: usize, language: []const u8) ParseError!?CodeBlock {
    // Look for ```language
    const marker = "```";
    const block_start_marker = std.mem.indexOfPos(u8, content, start_pos, marker) orelse return null;

    // Check language
    const after_marker = block_start_marker + 3;
    if (after_marker >= content.len) return null;

    // Find end of first line to get language
    const line_end = std.mem.indexOfPos(u8, content, after_marker, "\n") orelse return null;
    const lang_part = std.mem.trim(u8, content[after_marker..line_end], " \t\r");

    if (!std.mem.eql(u8, lang_part, language)) {
        // Wrong language, skip this block and try next
        const block_end = std.mem.indexOfPos(u8, content, line_end, marker) orelse return null;
        return findCodeBlock(content, block_end + 3, language);
    }

    // Find closing ``` (must be at start of line)
    const content_start = line_end + 1;
    const block_end = std.mem.indexOfPos(u8, content, content_start, "\n```") orelse {
        // Try end of content
        if (std.mem.endsWith(u8, content[content_start..], "```")) {
            const be = content.len - 3;
            return CodeBlock{
                .content = std.mem.trim(u8, content[content_start..be], "\r\n"),
                .end = content.len,
            };
        }
        return null;
    };

    // Content ends at the newline before ```
    const actual_content = std.mem.trim(u8, content[content_start..block_end], "\r\n");

    // End position is after the closing ``` and its newline
    const closing_marker_end = block_end + 4; // "\n```".len
    const end_pos = if (std.mem.indexOfPos(u8, content, closing_marker_end, "\n")) |nl| nl + 1 else closing_marker_end;

    return CodeBlock{
        .content = actual_content,
        .end = end_pos,
    };
}

const ExpectedOutput = struct {
    expectation: testing.TestExpectation,
    end_pos: usize,
};

fn parseExpectedOutput(allocator: std.mem.Allocator, content: []const u8, start_pos: usize) ParseError!?ExpectedOutput {
    // Look for ```exitcode or ```maxoncstderr
    const next_block_marker = std.mem.indexOfPos(u8, content, start_pos, "```") orelse return null;

    // Don't look too far ahead (max 100 chars of whitespace)
    if (next_block_marker - start_pos > 100) return null;

    const after_marker = next_block_marker + 3;
    const line_end = std.mem.indexOfPos(u8, content, after_marker, "\n") orelse return null;
    const block_type = std.mem.trim(u8, content[after_marker..line_end], " \t\r");

    if (std.mem.eql(u8, block_type, "exitcode")) {
        // Success test
        const content_start = line_end + 1;
        const block_end = std.mem.indexOfPos(u8, content, content_start, "```") orelse return null;

        const exit_code_str = std.mem.trim(u8, content[content_start..block_end], " \t\r\n");
        const exit_code = std.fmt.parseInt(u8, exit_code_str, 10) catch return ParseError.InvalidExitCode;

        var end_pos = block_end + 3;

        // Check for optional stdout block
        var stdout: ?[]const u8 = null;
        if (findCodeBlock(content, end_pos, "stdout") catch null) |stdout_block| {
            stdout = try allocator.dupe(u8, stdout_block.content);
            end_pos = stdout_block.end;
        }

        return ExpectedOutput{
            .expectation = .{ .success = .{
                .exit_code = exit_code,
                .stdout = stdout,
            } },
            .end_pos = end_pos,
        };
    } else if (std.mem.eql(u8, block_type, "maxoncstderr")) {
        // Error test
        const content_start = line_end + 1;
        const block_end = std.mem.indexOfPos(u8, content, content_start, "```") orelse return null;

        const error_msg = try allocator.dupe(u8, std.mem.trim(u8, content[content_start..block_end], " \t\r\n"));

        return ExpectedOutput{
            .expectation = .{ .compiler_error = error_msg },
            .end_pos = block_end + 3,
        };
    }

    return null;
}

/// Parse all spec files in a directory
pub fn parseAllSpecs(
    allocator: std.mem.Allocator,
    specs_dir: []const u8,
) ![]testing.SpecFile {
    var specs: std.ArrayListUnmanaged(testing.SpecFile) = .empty;
    errdefer {
        for (specs.items) |*spec| {
            spec.deinit(allocator);
        }
        specs.deinit(allocator);
    }

    var dir = std.fs.cwd().openDir(specs_dir, .{ .iterate = true }) catch |err| {
        std.debug.print("Error opening specs directory '{s}': {}\n", .{ specs_dir, err });
        return error.OpenDirFailed;
    };
    defer dir.close();

    var iter = dir.iterate();
    while (try iter.next()) |entry| {
        if (entry.kind != .file) continue;
        if (!std.mem.endsWith(u8, entry.name, ".md")) continue;

        // Get spec name (filename without .md)
        const spec_name = entry.name[0 .. entry.name.len - 3];

        // Read file content
        const content = dir.readFileAlloc(allocator, entry.name, 1024 * 1024) catch |err| {
            std.debug.print("Error reading spec file '{s}': {}\n", .{ entry.name, err });
            continue;
        };
        defer allocator.free(content);

        // Parse spec
        const spec = parseSpec(allocator, spec_name, content) catch |err| {
            std.debug.print("Error parsing spec file '{s}': {}\n", .{ entry.name, err });
            continue;
        };

        try specs.append(allocator, spec);
    }

    return try specs.toOwnedSlice(allocator);
}
