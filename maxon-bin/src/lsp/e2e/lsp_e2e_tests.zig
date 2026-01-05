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

// ============================================================================
// Hover Tests
// ============================================================================

test "hover shows value for immutable variables" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    const source =
        \\function main() returns int
        \\    let PI = 3.14159
        \\    return 0
        \\end 'main'
    ;

    try ctx.client.openDocument("file:///test.maxon", source);

    // Request hover on 'PI' at line 1, character 5
    var result = try ctx.client.hover("file:///test.maxon", 1, 5);
    defer result.deinit();

    // The hover should contain the value for immutable variables
    if (result.content) |content| {
        try testing.expect(std.mem.indexOf(u8, content, "3.14159") != null);
    }

    try ctx.deinit();
}

test "hover shows local function signature" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    const source =
        \\function add(a int, b int) returns int
        \\    return a + b
        \\end 'add'
        \\
        \\function main() returns int
        \\    return add(1, 2)
        \\end 'main'
    ;

    try ctx.client.openDocument("file:///test.maxon", source);

    // Request hover on 'add' at the call site (line 5)
    // Line 5: "    return add(1, 2)" - 'add' starts at position 11
    var result = try ctx.client.hover("file:///test.maxon", 5, 12);
    defer result.deinit();

    // The hover should show the function signature
    if (result.content) |content| {
        try testing.expect(std.mem.indexOf(u8, content, "function add") != null);
        try testing.expect(std.mem.indexOf(u8, content, "a int") != null);
    }

    try ctx.deinit();
}

test "hover shows correct type for function parameter" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    const source =
        \\function greet(name string) returns int
        \\    var x = name
        \\    return 0
        \\end 'greet'
    ;

    try ctx.client.openDocument("file:///test.maxon", source);

    // Request hover on 'name' at line 1
    // Line 1: "    var x = name" - 'name' starts at position 12
    var result = try ctx.client.hover("file:///test.maxon", 1, 13);
    defer result.deinit();

    // The hover should show the parameter type as string
    if (result.content) |content| {
        try testing.expect(std.mem.indexOf(u8, content, "string") != null);
        try testing.expect(std.mem.indexOf(u8, content, "float") == null);
    }

    try ctx.deinit();
}

test "hover shows intrinsic signature" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    const source =
        \\function test()
        \\    var cs = "test".cstr()
        \\    var arr = [1 as byte, 2 as byte]
        \\    __write_file_binary(cs, arr)
        \\end 'test'
    ;

    try ctx.client.openDocument("file:///test.maxon", source);

    // Request hover on '__write_file_binary' at line 3
    var result = try ctx.client.hover("file:///test.maxon", 3, 10);
    defer result.deinit();

    // The hover should show the intrinsic signature
    if (result.content) |content| {
        try testing.expect(std.mem.indexOf(u8, content, "intrinsic") != null);
        try testing.expect(std.mem.indexOf(u8, content, "__write_file_binary") != null);
    }

    try ctx.deinit();
}

// ============================================================================
// Go to Definition Tests
// ============================================================================

test "definition returns location" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    const source =
        \\function helper() returns int
        \\    return 42
        \\end 'helper'
        \\
        \\function main() returns int
        \\    return helper()
        \\end 'main'
    ;

    try ctx.client.openDocument("file:///test.maxon", source);

    // Request definition on 'helper' call at line 5, character 11 (inside "helper")
    var result = try ctx.client.definition("file:///test.maxon", 5, 11);
    defer result.deinit();

    // Should point to helper() definition at line 0
    try testing.expect(result.uri != null);
    try testing.expectEqual(@as(u32, 0), result.line.?);

    try ctx.deinit();
}

// ============================================================================
// Formatting Tests
// ============================================================================

test "formatting indents nested code correctly" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    // Code with no indentation - formatter should add proper indentation
    const source =
        \\function main() returns int
        \\var x = 1
        \\if x > 0 'check'
        \\var y = 2
        \\end 'check'
        \\return x
        \\end 'main'
    ;
    try ctx.client.openDocument("file:///test.maxon", source);

    var result = try ctx.client.formatting("file:///test.maxon", 4, false);
    defer result.deinit();

    // Verify we got formatted text
    try testing.expect(result.new_text != null);
    const new_text = result.new_text.?;

    // Check that var x is indented (should have a tab before it)
    try testing.expect(std.mem.indexOf(u8, new_text, "\tvar x") != null);

    // Check that var y inside if block has two tabs
    try testing.expect(std.mem.indexOf(u8, new_text, "\t\tvar y") != null);

    // Check that end 'check' has one tab
    try testing.expect(std.mem.indexOf(u8, new_text, "\tend 'check'") != null);

    // Check that end 'main' has no leading tab
    try testing.expect(std.mem.indexOf(u8, new_text, "\nend 'main'") != null);

    try ctx.deinit();
}

test "formatting indents type fields correctly" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    // Type with no indentation on fields
    const source =
        \\type Point
        \\var x int
        \\var y int
        \\end 'Point'
    ;
    try ctx.client.openDocument("file:///test.maxon", source);

    var result = try ctx.client.formatting("file:///test.maxon", 4, false);
    defer result.deinit();

    try testing.expect(result.new_text != null);
    const new_text = result.new_text.?;

    // Struct fields should be indented one level
    try testing.expect(std.mem.indexOf(u8, new_text, "\tvar x int") != null);
    try testing.expect(std.mem.indexOf(u8, new_text, "\tvar y int") != null);

    // end should be at level 0
    try testing.expect(std.mem.indexOf(u8, new_text, "\nend 'Point'") != null);

    try ctx.deinit();
}

test "formatting handles else blocks correctly" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    // Code with else block - indentation should be maintained
    const source =
        \\function test() returns int
        \\if true 'check'
        \\return 1
        \\else 'check'
        \\return 0
        \\end 'check'
        \\return 0
        \\end 'test'
    ;
    try ctx.client.openDocument("file:///test.maxon", source);

    var result = try ctx.client.formatting("file:///test.maxon", 4, false);
    defer result.deinit();

    try testing.expect(result.new_text != null);
    const new_text = result.new_text.?;

    // The if should be indented 1 level
    try testing.expect(std.mem.indexOf(u8, new_text, "\tif true") != null);

    // The first return should be indented 2 levels
    try testing.expect(std.mem.indexOf(u8, new_text, "\t\treturn 1") != null);

    // The else should be indented 1 level (same as if)
    try testing.expect(std.mem.indexOf(u8, new_text, "\telse 'check'") != null);

    // The end 'check' should be indented 1 level
    try testing.expect(std.mem.indexOf(u8, new_text, "\tend 'check'") != null);

    try ctx.deinit();
}

test "formatting uses spaces when insertSpaces is true" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    // Code with no indentation
    const source =
        \\function main() returns int
        \\var x = 1
        \\return x
        \\end 'main'
    ;
    try ctx.client.openDocument("file:///test.maxon", source);

    var result = try ctx.client.formatting("file:///test.maxon", 4, true);
    defer result.deinit();

    try testing.expect(result.new_text != null);
    const new_text = result.new_text.?;

    // With insertSpaces=true and tabSize=4, indentation should be 4 spaces
    try testing.expect(std.mem.indexOf(u8, new_text, "    var x") != null);
    try testing.expect(std.mem.indexOf(u8, new_text, "    return x") != null);

    // Should NOT have tabs
    try testing.expect(std.mem.indexOf(u8, new_text, "\tvar") == null);

    try ctx.deinit();
}

test "formatting preserves implicit type declarations" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    // Code with type inference - no explicit type annotations
    const source =
        \\function main() returns int
        \\var x = 1
        \\let y = 2.5
        \\return x
        \\end 'main'
    ;
    try ctx.client.openDocument("file:///test.maxon", source);

    var result = try ctx.client.formatting("file:///test.maxon", 4, false);
    defer result.deinit();

    try testing.expect(result.new_text != null);
    const new_text = result.new_text.?;

    // The formatter should NOT add explicit type annotations
    // "var x = 1" should stay as "var x = 1", not become "var x int = 1"
    try testing.expect(std.mem.indexOf(u8, new_text, "var x = 1") != null);
    try testing.expect(std.mem.indexOf(u8, new_text, "var x int") == null);

    // "let y = 2.5" should stay as "let y = 2.5", not become "let y float = 2.5"
    try testing.expect(std.mem.indexOf(u8, new_text, "let y = 2.5") != null);
    try testing.expect(std.mem.indexOf(u8, new_text, "let y float") == null);

    try ctx.deinit();
}

test "formatting preserves comments" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    const source =
        \\// This is a comment
        \\function main() returns int
        \\// Another comment
        \\var x = 1
        \\return x
        \\end 'main'
    ;
    try ctx.client.openDocument("file:///test.maxon", source);

    var result = try ctx.client.formatting("file:///test.maxon", 4, false);
    defer result.deinit();

    try testing.expect(result.new_text != null);
    const new_text = result.new_text.?;

    // Comments should be preserved
    try testing.expect(std.mem.indexOf(u8, new_text, "// This is a comment") != null);
    try testing.expect(std.mem.indexOf(u8, new_text, "// Another comment") != null);

    try ctx.deinit();
}

test "formatting preserves blank lines" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    const source =
        \\function foo() returns int
        \\return 1
        \\end 'foo'
        \\
        \\function main() returns int
        \\return 0
        \\end 'main'
    ;
    try ctx.client.openDocument("file:///test.maxon", source);

    var result = try ctx.client.formatting("file:///test.maxon", 4, false);
    defer result.deinit();

    try testing.expect(result.new_text != null);
    const new_text = result.new_text.?;

    // Blank line between functions should be preserved
    try testing.expect(std.mem.indexOf(u8, new_text, "end 'foo'\n\nfunction main") != null);

    try ctx.deinit();
}

test "formatting formats multiple interfaces at top level" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    // Multiple interfaces with incorrect nesting
    const source =
        \\interface Hashable
        \\function hash() returns int
        \\end 'Hashable'
        \\
        \\interface Equatable
        \\function equals(other Self) returns bool
        \\end 'Equatable'
    ;
    try ctx.client.openDocument("file:///test.maxon", source);

    var result = try ctx.client.formatting("file:///test.maxon", 4, false);
    defer result.deinit();

    try testing.expect(result.new_text != null);
    const new_text = result.new_text.?;

    // Verify interfaces are NOT indented (no tab before interface keyword)
    try testing.expect(std.mem.indexOf(u8, new_text, "\tinterface") == null);

    // Method signatures should be indented one level
    try testing.expect(std.mem.indexOf(u8, new_text, "\tfunction hash()") != null);
    try testing.expect(std.mem.indexOf(u8, new_text, "\tfunction equals(") != null);

    try ctx.deinit();
}

// ============================================================================
// Document Symbols Tests
// ============================================================================

test "documentSymbol returns symbols" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    const source =
        \\type Point
        \\    var x int
        \\    var y int
        \\end 'Point'
        \\
        \\function main() returns int
        \\    return 0
        \\end 'main'
    ;

    try ctx.client.openDocument("file:///test.maxon", source);

    var result = try ctx.client.documentSymbols("file:///test.maxon");
    defer result.deinit();

    // Should have exactly Point type and main function
    try testing.expectEqual(@as(usize, 2), result.symbols.len);

    // Verify we found both Point and main with correct kinds
    // SymbolKind: struct = 23, function = 12
    var found_point = false;
    var found_main = false;
    for (result.symbols) |sym| {
        if (std.mem.eql(u8, sym.name, "Point")) {
            found_point = true;
            try testing.expectEqual(@as(i64, 23), sym.kind); // struct
        }
        if (std.mem.eql(u8, sym.name, "main")) {
            found_main = true;
            try testing.expectEqual(@as(i64, 12), sym.kind); // function
        }
    }
    try testing.expect(found_point);
    try testing.expect(found_main);

    try ctx.deinit();
}

// ============================================================================
// Folding Range Tests
// ============================================================================

test "foldingRange returns ranges" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    const source =
        \\function main() returns int
        \\    if true 'check'
        \\        return 1
        \\    end 'check'
        \\    return 0
        \\end 'main'
    ;
    try ctx.client.openDocument("file:///test.maxon", source);

    var result = try ctx.client.foldingRange("file:///test.maxon");
    defer result.deinit();

    // Should have folding ranges for function and if block
    try testing.expect(result.ranges.len >= 1);

    try ctx.deinit();
}

// ============================================================================
// Linked Editing Range Tests
// ============================================================================

test "linkedEditingRange returns ranges for block labels" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    const source =
        \\function main() returns int
        \\for i in range(0, 10) 'loop'
        \\var x = i
        \\end 'loop'
        \\return 0
        \\end 'main'
    ;
    try ctx.client.openDocument("file:///test.maxon", source);

    // Request linked editing on the 'loop' label at line 1, character 23
    var result = try ctx.client.linkedEditingRange("file:///test.maxon", 1, 23);
    defer result.deinit();

    // Should return linked ranges for both 'loop' occurrences (start and end)
    try testing.expectEqual(@as(usize, 2), result.ranges.len);

    try ctx.deinit();
}

test "linkedEditingRange returns ranges for function names" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    const source =
        \\function myFunction() returns int
        \\return 42
        \\end 'myFunction'
    ;
    try ctx.client.openDocument("file:///test.maxon", source);

    // Request linked editing on the function name "myFunction" at line 0, character 12
    var result = try ctx.client.linkedEditingRange("file:///test.maxon", 0, 12);
    defer result.deinit();

    // Should return 2 ranges: function name and end label
    try testing.expectEqual(@as(usize, 2), result.ranges.len);

    // Check ranges point to the right locations
    // Range 0: function name at line 0, characters 9-19 (myFunction)
    // Range 1: end label at line 2, characters 5-15 (myFunction inside quotes)
    var has_declaration = false;
    var has_end_label = false;
    for (result.ranges) |range| {
        if (range.start_line == 0 and range.start_char == 9 and range.end_line == 0 and range.end_char == 19) {
            has_declaration = true;
        }
        if (range.start_line == 2 and range.start_char == 5 and range.end_line == 2 and range.end_char == 15) {
            has_end_label = true;
        }
    }
    try testing.expect(has_declaration);
    try testing.expect(has_end_label);

    try ctx.deinit();
}

test "linkedEditingRange returns ranges for interface method names" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    // Type with interface method implementation
    const source =
        \\interface Countable
        \\function count() returns int
        \\end 'Countable'
        \\
        \\type MyStruct is Countable
        \\function Countable.count() returns int
        \\return 0
        \\end 'count'
        \\end 'MyStruct'
    ;
    try ctx.client.openDocument("file:///test.maxon", source);

    // Request linked editing on the method name "count" at line 5, character 20
    var result = try ctx.client.linkedEditingRange("file:///test.maxon", 5, 20);
    defer result.deinit();

    // Should return 2 ranges: method name and end label
    try testing.expectEqual(@as(usize, 2), result.ranges.len);

    // Check ranges point to the right locations
    // Range 0: method name at line 5, characters 19-24 (count)
    // Range 1: end label at line 7, characters 5-10 (count inside quotes)
    var has_method_name = false;
    var has_end_label = false;
    for (result.ranges) |range| {
        if (range.start_line == 5 and range.start_char == 19 and range.end_line == 5 and range.end_char == 24) {
            has_method_name = true;
        }
        if (range.start_line == 7 and range.start_char == 5 and range.end_line == 7 and range.end_char == 10) {
            has_end_label = true;
        }
    }
    try testing.expect(has_method_name);
    try testing.expect(has_end_label);

    try ctx.deinit();
}
