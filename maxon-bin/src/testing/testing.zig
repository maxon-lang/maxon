const std = @import("std");

/// Represents a parsed spec file
pub const SpecFile = struct {
    name: []const u8, // Spec name (from filename, e.g., "abs")
    feature: []const u8, // From frontmatter
    status: Status,
    category: []const u8, // From frontmatter
    tests: []TestCase, // Extracted test cases

    pub const Status = enum {
        stable,
        experimental,
        draft,

        pub fn fromString(s: []const u8) ?Status {
            if (std.mem.eql(u8, s, "stable")) return .stable;
            if (std.mem.eql(u8, s, "experimental")) return .experimental;
            if (std.mem.eql(u8, s, "draft")) return .draft;
            return null;
        }
    };

    pub fn deinit(self: *SpecFile, allocator: std.mem.Allocator) void {
        for (self.tests) |*test_case| {
            test_case.deinit(allocator);
        }
        allocator.free(self.tests);
    }
};

/// Represents a single test case extracted from a spec
pub const TestCase = struct {
    name: []const u8, // Full test name (e.g., "abs.float.1")
    source: []const u8, // Maxon source code
    expected: TestExpectation, // Expected result

    pub fn deinit(self: *TestCase, allocator: std.mem.Allocator) void {
        allocator.free(self.name);
        allocator.free(self.source);
        switch (self.expected) {
            .success => |s| {
                if (s.stdout) |stdout| allocator.free(stdout);
                if (s.expected_ir) |ir| allocator.free(ir);
                if (s.run_args) |args| allocator.free(args);
            },
            .compiler_error => |err| allocator.free(err),
        }
    }
};

/// Expected outcome of a test
pub const TestExpectation = union(enum) {
    success: SuccessExpectation,
    compiler_error: []const u8, // Expected stderr from compiler
};

pub const SuccessExpectation = struct {
    exit_code: u8,
    stdout: ?[]const u8 = null, // Optional expected stdout
    expected_ir: ?[]const u8 = null, // Optional expected IR (for optimization tests)
    track_allocs: bool = false, // Enable allocation tracking
    run_args: ?[]const u8 = null, // Command-line arguments to pass to the test executable
};

/// Result of running a test
pub const TestResult = struct {
    name: []const u8,
    status: ResultStatus,
    message: ?[]const u8, // Error details if failed

    pub const ResultStatus = enum {
        passed,
        failed,
        skipped,
    };
};

/// Options for running tests
pub const TestOptions = struct {
    filter: ?[]const u8 = null, // Run only tests matching pattern
    verbose: bool = false,
};

/// Summary of test run
pub const TestSummary = struct {
    total: usize,
    passed: usize,
    failed: usize,
    skipped: usize,
    results: []TestResult,

    pub fn printSummaryDebug(self: TestSummary, elapsed_ms: f64) void {
        std.debug.print("\n", .{});
        std.debug.print("Tests: {} passed", .{self.passed});
        if (self.failed > 0) {
            std.debug.print(", {} failed", .{self.failed});
        }
        if (self.skipped > 0) {
            std.debug.print(", {} skipped", .{self.skipped});
        }
        std.debug.print(" (total: {}) in {d:.0}ms\n", .{ self.total, elapsed_ms });
    }
};

/// Result of fragment generation
pub const GenerationResult = struct {
    fragments_written: usize,
    specs_processed: usize,
};
