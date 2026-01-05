const std = @import("std");
const LspClient = @import("lsp_client.zig").LspClient;
const testing = std.testing;

/// Test context that handles setup/teardown with automatic leak checking
const TestContext = struct {
    client: LspClient,
    cleaned_up: bool = false,

    fn init() !TestContext {
        var client = try LspClient.init(testing.allocator);
        _ = try client.initialize();
        try client.initialized();
        return .{ .client = client };
    }

    /// Clean shutdown with leak check - call this at the end of successful tests
    fn deinit(self: *TestContext) !void {
        if (self.cleaned_up) return;
        self.cleaned_up = true;

        try self.client.shutdown();
        try self.client.exit();
        try self.client.deinitAndCheckLeaks();
    }

    /// Force cleanup without leak check - use only when test already failed
    fn forceCleanup(self: *TestContext) void {
        if (self.cleaned_up) return;
        self.cleaned_up = true;
        self.client.deinit();
    }
};

// ============================================================================
// Core Protocol Tests
// ============================================================================

test "initialize returns capabilities" {
    var client = try LspClient.init(testing.allocator);

    const result = try client.initialize();

    try testing.expect(result.has_text_document_sync);
    try testing.expect(result.has_completion_provider);

    try client.initialized();
    try client.shutdown();
    try client.exit();
    try client.deinitAndCheckLeaks();
}

test "shutdown and exit terminate cleanly" {
    var ctx = try TestContext.init();
    try ctx.deinit();
}

test "server handles full workflow" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    // Do some work that allocates memory
    try ctx.client.openDocument("file:///test.maxon",
        \\function main() returns int
        \\    var x = 42
        \\    return x
        \\end 'main'
    );
    try ctx.client.changeDocument("file:///test.maxon",
        \\function main() returns int
        \\    var x = 100
        \\    return x
        \\end 'main'
    , 2);

    // Request completion to exercise more code paths
    var result = try ctx.client.completionWithTrigger("file:///test.maxon", 1, 4, null);
    result.deinit();

    try ctx.client.closeDocument("file:///test.maxon");
    try ctx.deinit();
}

test "full lifecycle with document" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    // Open, modify, and close a document
    try ctx.client.openDocument("file:///test.maxon", "let x = 1");
    try ctx.client.changeDocument("file:///test.maxon", "let x = 2", 2);
    try ctx.client.closeDocument("file:///test.maxon");

    try ctx.deinit();
}

// ============================================================================
// Document Sync Tests
// ============================================================================

test "didOpen registers document" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    // Opening a document should not error
    try ctx.client.openDocument("file:///test.maxon",
        \\function main() returns int
        \\    return 0
        \\end 'main'
    );

    try ctx.deinit();
}

test "didChange updates document content" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    try ctx.client.openDocument("file:///test.maxon", "let x = 1");

    // Updating document should not error
    try ctx.client.changeDocument("file:///test.maxon", "let x = 2\nlet y = 3", 2);

    try ctx.deinit();
}

test "didClose removes document" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    try ctx.client.openDocument("file:///test.maxon", "let x = 1");
    try ctx.client.closeDocument("file:///test.maxon");

    try ctx.deinit();
}

// ============================================================================
// Completion Tests
// ============================================================================

test "completion request succeeds" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    // Use valid complete code - the LSP may not handle incomplete syntax well
    const source =
        \\enum Direction
        \\    case north
        \\    case south
        \\end 'Direction'
        \\
        \\function main() returns int
        \\    var d = Direction.north
        \\    return 0
        \\end 'main'
    ;

    try ctx.client.openDocument("file:///test.maxon", source);

    // Request completion - just verify the request completes without error
    // Position after "Direction." on line 7
    var result = try ctx.client.completion("file:///test.maxon", 7, 22);
    defer result.deinit();

    // The request should succeed (even if no items returned for valid code)
    // This tests the JSON-RPC round-trip works

    try ctx.deinit();
}

test "completion after dot with valid prefix" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    // First open valid code so semantic cache is populated
    const valid_source =
        \\enum Direction
        \\    case north
        \\    case south
        \\end 'Direction'
        \\
        \\function main() returns int
        \\    var d = Direction.north
        \\    return 0
        \\end 'main'
    ;

    try ctx.client.openDocument("file:///test.maxon", valid_source);

    // Now update to incomplete code to trigger completion
    const incomplete_source =
        \\enum Direction
        \\    case north
        \\    case south
        \\end 'Direction'
        \\
        \\function main() returns int
        \\    var d = Direction.
        \\    return 0
        \\end 'main'
    ;

    try ctx.client.changeDocument("file:///test.maxon", incomplete_source, 2);

    // Request completion at the dot position (line 7, after "Direction.")
    var result = try ctx.client.completion("file:///test.maxon", 7, 22);
    defer result.deinit();

    // Check if we got enum cases (may or may not work depending on LSP implementation)
    // For now, just verify no error occurred
    _ = result.items.len;

    try ctx.deinit();
}

test "completion on empty position returns results" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    const source =
        \\function main() returns int
        \\
        \\    return 0
        \\end 'main'
    ;

    try ctx.client.openDocument("file:///test.maxon", source);

    // Request completion at empty line (line 1, column 4)
    var result = try ctx.client.completion("file:///test.maxon", 1, 4);
    defer result.deinit();

    // Should get keyword completions or be empty, but not error
    // Just verify we got a valid response
    _ = result.items.len;

    try ctx.deinit();
}

test "completion for array methods" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    // Open a document with an array literal - use valid complete code first
    // LSP lines are 0-indexed, so line 2 is "    arr."
    const source =
        \\function main() returns int
        \\    var arr = [1, 2, 3]
        \\    arr.push(4)
        \\    return 0
        \\end 'main'
    ;

    try ctx.client.openDocument("file:///test.maxon", source);

    // Request completion after "arr." (line 2, column 8 - right after the dot)
    // Line 2 = "    arr.push(4)" - after "arr." is column 8
    // Use trigger character "." to simulate typing a dot
    var result = try ctx.client.completionWithTrigger("file:///test.maxon", 2, 8, ".");
    defer result.deinit();

    // Expected Array methods/fields
    const expected_completions = [_][]const u8{
        "append",
        "capacity",
        "clear",
        "count",
        "ensureCapacity",
        "first",
        "get",
        "init",
        "insert",
        "isEmpty",
        "iterIndex",
        "last",
        "managed",
        "map",
        "next",
        "pop",
        "push",
        "remove",
        "reserve",
        "set",
    };

    // Check we got the expected number of completions
    try testing.expectEqual(expected_completions.len, result.items.len);

    // Verify each expected completion is present
    for (expected_completions) |expected| {
        var found = false;
        for (result.items) |item| {
            if (std.mem.eql(u8, item.label, expected)) {
                found = true;
                break;
            }
        }
        if (!found) {
            std.debug.print("Missing expected completion: {s}\n", .{expected});
        }
        try testing.expect(found);
    }

    try ctx.deinit();
}
