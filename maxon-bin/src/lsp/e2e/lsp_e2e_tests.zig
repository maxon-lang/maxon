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
        \\    var arr = [1, 2, 3]
        \\    arr.push(4)
        \\    return 0
        \\end 'main'
    , 2);

    // Request completion after "arr." (line 2, column 8)
    // This should return array methods
    var result = try ctx.client.completionWithTrigger("file:///test.maxon", 2, 8, ".");
    defer result.deinit();

    // Should get array method completions - verify specific methods exist
    try testing.expect(result.items.len > 0);
    var found_push = false;
    for (result.items) |item| {
        if (std.mem.eql(u8, item.label, "push")) {
            found_push = true;
            break;
        }
    }
    try testing.expect(found_push);

    try ctx.client.closeDocument("file:///test.maxon");
    try ctx.deinit();
}

test "full lifecycle with document" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    // Open, modify, and close a document - verify no errors
    try ctx.client.openDocument("file:///test.maxon",
        \\function main() returns int
        \\    var x = 1
        \\    return x
        \\end 'main'
    );

    // Request hover on 'x' variable at line 1, character 8
    // Line 1: "    var x = 1" - 'x' is at position 8
    var hover_result = try ctx.client.hover("file:///test.maxon", 1, 8);
    defer hover_result.deinit();
    try testing.expect(hover_result.content != null);
    // Verify hover shows the variable type (int)
    try testing.expect(std.mem.indexOf(u8, hover_result.content.?, "int") != null);

    try ctx.client.changeDocument("file:///test.maxon",
        \\function main() returns int
        \\    var x = 2
        \\    return x
        \\end 'main'
    , 2);
    try ctx.client.closeDocument("file:///test.maxon");

    try ctx.deinit();
}

// ============================================================================
// Document Sync Tests
// ============================================================================

test "didOpen registers document" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    try ctx.client.openDocument("file:///test.maxon",
        \\function main() returns int
        \\    return 0
        \\end 'main'
    );

    // Verify document is accessible by requesting symbols
    var result = try ctx.client.documentSymbols("file:///test.maxon");
    defer result.deinit();
    try testing.expectEqual(@as(usize, 1), result.symbols.len);
    try testing.expect(std.mem.eql(u8, result.symbols[0].name, "main"));
    try testing.expectEqual(@as(i64, 12), result.symbols[0].kind); // function = 12

    try ctx.deinit();
}

test "didChange updates document content" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    try ctx.client.openDocument("file:///test.maxon",
        \\function foo() returns int
        \\    return 1
        \\end 'foo'
    );

    // Verify initial content
    var result1 = try ctx.client.documentSymbols("file:///test.maxon");
    defer result1.deinit();
    try testing.expect(result1.symbols.len == 1);
    try testing.expect(std.mem.eql(u8, result1.symbols[0].name, "foo"));

    // Update to add another function
    try ctx.client.changeDocument("file:///test.maxon",
        \\function foo() returns int
        \\    return 1
        \\end 'foo'
        \\
        \\function bar() returns int
        \\    return 2
        \\end 'bar'
    , 2);

    // Verify updated content has both functions
    var result2 = try ctx.client.documentSymbols("file:///test.maxon");
    defer result2.deinit();
    try testing.expectEqual(@as(usize, 2), result2.symbols.len);

    try ctx.deinit();
}

test "didClose removes document" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    try ctx.client.openDocument("file:///test.maxon",
        \\function main() returns int
        \\    return 0
        \\end 'main'
    );

    // Verify document exists
    var result1 = try ctx.client.documentSymbols("file:///test.maxon");
    defer result1.deinit();
    try testing.expect(result1.symbols.len >= 1);

    try ctx.client.closeDocument("file:///test.maxon");

    // After close, symbols request should return empty
    var result2 = try ctx.client.documentSymbols("file:///test.maxon");
    defer result2.deinit();
    try testing.expectEqual(@as(usize, 0), result2.symbols.len);

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

test "completion for string methods" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    const source =
        \\function main() returns int
        \\    var s = "hello"
        \\    return s.count
        \\end 'main'
    ;

    try ctx.client.openDocument("file:///test.maxon", source);

    // Request completion after "s." (line 2, column 13)
    var result = try ctx.client.completionWithTrigger("file:///test.maxon", 2, 13, ".");
    defer result.deinit();

    // Check we got some string completions
    try testing.expect(result.items.len > 0);

    // Verify count is in the completions
    var found_count = false;
    for (result.items) |item| {
        if (std.mem.eql(u8, item.label, "count")) {
            found_count = true;
            break;
        }
    }
    try testing.expect(found_count);

    try ctx.deinit();
}

test "completion for struct fields" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    const source =
        \\type Point
        \\    var x int
        \\    var y int
        \\end 'Point'
        \\
        \\function test() returns int
        \\    var p = Point{x: 0, y: 0}
        \\    return p.x
        \\end 'test'
    ;

    try ctx.client.openDocument("file:///test.maxon", source);

    // Request completion after "p." (line 7, column 13)
    var result = try ctx.client.completionWithTrigger("file:///test.maxon", 7, 13, ".");
    defer result.deinit();

    // Check we got some completions
    try testing.expect(result.items.len > 0);

    // Verify x and y are in the completions
    var found_x = false;
    var found_y = false;
    for (result.items) |item| {
        if (std.mem.eql(u8, item.label, "x")) found_x = true;
        if (std.mem.eql(u8, item.label, "y")) found_y = true;
    }
    try testing.expect(found_x);
    try testing.expect(found_y);

    try ctx.deinit();
}

test "completion for enum members" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    const source =
        \\enum Color
        \\    red
        \\    green
        \\    blue
        \\end 'Color'
        \\
        \\function test() returns Color
        \\    return Color.red
        \\end 'test'
    ;

    try ctx.client.openDocument("file:///test.maxon", source);

    // Request completion after "Color." (line 7, column 17)
    var result = try ctx.client.completionWithTrigger("file:///test.maxon", 7, 17, ".");
    defer result.deinit();

    // Check we got some completions for enum members
    try testing.expect(result.items.len > 0);

    // Verify enum members are in the completions
    var found_red = false;
    var found_green = false;
    var found_blue = false;
    for (result.items) |item| {
        if (std.mem.eql(u8, item.label, "red")) found_red = true;
        if (std.mem.eql(u8, item.label, "green")) found_green = true;
        if (std.mem.eql(u8, item.label, "blue")) found_blue = true;
    }
    try testing.expect(found_red);
    try testing.expect(found_green);
    try testing.expect(found_blue);

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

    // Request hover on 'PI' at line 1, character 8 (start of "PI")
    // Line 1: "    let PI = 3.14159"
    //          01234567890
    var result = try ctx.client.hover("file:///test.maxon", 1, 8);
    defer result.deinit();

    // The hover should contain the value for immutable variables
    try testing.expect(result.content != null);
    const content = result.content.?;
    try testing.expect(std.mem.indexOf(u8, content, "3.14159") != null);

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
    try testing.expect(result.content != null);
    const content = result.content.?;
    try testing.expect(std.mem.indexOf(u8, content, "function add") != null);
    try testing.expect(std.mem.indexOf(u8, content, "a int") != null);

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
    try testing.expect(result.content != null);
    const content = result.content.?;
    try testing.expect(std.mem.indexOf(u8, content, "string") != null);
    try testing.expect(std.mem.indexOf(u8, content, "float") == null);

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
    try testing.expect(result.content != null);
    const content = result.content.?;
    try testing.expect(std.mem.indexOf(u8, content, "intrinsic") != null);
    try testing.expect(std.mem.indexOf(u8, content, "__write_file_binary") != null);

    try ctx.deinit();
}

test "hover shows math builtin signature and help text" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    const source =
        \\function test() returns int
        \\    let x = sqrt(16.0)
        \\    return trunc(x)
        \\end 'test'
    ;

    try ctx.client.openDocument("file:///test.maxon", source);

    // Request hover on 'sqrt' at line 1
    // Line 1: "    let x = sqrt(16.0)" - 'sqrt' starts at position 12
    var result = try ctx.client.hover("file:///test.maxon", 1, 13);
    defer result.deinit();

    // The hover should show the function signature and help text
    try testing.expect(result.content != null);
    const content = result.content.?;

    // Should show signature
    try testing.expect(std.mem.indexOf(u8, content, "sqrt") != null);
    try testing.expect(std.mem.indexOf(u8, content, "float") != null);

    // Should show help text from registry
    try testing.expect(std.mem.indexOf(u8, content, "square root") != null);

    try ctx.deinit();
}

test "hover shows trunc builtin with help text" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    const source =
        \\function test() returns int
        \\    return trunc(3.7)
        \\end 'test'
    ;

    try ctx.client.openDocument("file:///test.maxon", source);

    // Request hover on 'trunc' at line 1
    // Line 1: "    return trunc(3.7)" - 'trunc' starts at position 11
    var result = try ctx.client.hover("file:///test.maxon", 1, 12);
    defer result.deinit();

    try testing.expect(result.content != null);
    const content = result.content.?;

    // Should show signature with parameter name and types
    try testing.expect(std.mem.indexOf(u8, content, "trunc") != null);
    try testing.expect(std.mem.indexOf(u8, content, "value") != null);
    try testing.expect(std.mem.indexOf(u8, content, "float") != null);
    try testing.expect(std.mem.indexOf(u8, content, "int") != null);

    // Should show help text
    try testing.expect(std.mem.indexOf(u8, content, "Truncates") != null);

    try ctx.deinit();
}

test "hover shows keyword info" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    const source =
        \\function test() returns int
        \\    return 42
        \\end 'test'
    ;

    try ctx.client.openDocument("file:///test.maxon", source);

    // Request hover on 'return' keyword at line 1, character 4
    var result = try ctx.client.hover("file:///test.maxon", 1, 4);
    defer result.deinit();

    try testing.expect(result.content != null);
    const content = result.content.?;
    try testing.expect(std.mem.indexOf(u8, content, "return") != null);

    try ctx.deinit();
}

test "hover shows type keyword info" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    const source =
        \\type MyStruct
        \\    var value int
        \\end 'MyStruct'
    ;

    try ctx.client.openDocument("file:///test.maxon", source);

    // Request hover on 'type' keyword at line 0, character 2
    var result = try ctx.client.hover("file:///test.maxon", 0, 2);
    defer result.deinit();

    try testing.expect(result.content != null);
    const content = result.content.?;
    try testing.expect(std.mem.indexOf(u8, content, "type") != null);

    try ctx.deinit();
}

test "hover excludes text in line comments" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    const source =
        \\// This function uses for loops and if statements
        \\function test() returns int
        \\    return 42
        \\end 'test'
    ;

    try ctx.client.openDocument("file:///test.maxon", source);

    // Request hover on 'for' inside the line comment (line 0, col 24)
    var result = try ctx.client.hover("file:///test.maxon", 0, 24);
    defer result.deinit();

    // Should have no hover for text inside comments
    try testing.expect(result.content == null);

    try ctx.deinit();
}

test "hover excludes text in block comments" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    const source =
        \\/* This is a block comment
        \\   that mentions for loops
        \\   and if statements */
        \\function test() returns int
        \\    return 42
        \\end 'test'
    ;

    try ctx.client.openDocument("file:///test.maxon", source);

    // Request hover on 'for' in the block comment (line 1, col 17)
    var result = try ctx.client.hover("file:///test.maxon", 1, 17);
    defer result.deinit();

    // Should have no hover for text inside comments
    try testing.expect(result.content == null);

    try ctx.deinit();
}

test "hover works after block comment ends" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    const source =
        \\/* comment */ function test() returns int
        \\    return 42
        \\end 'test'
    ;

    try ctx.client.openDocument("file:///test.maxon", source);

    // Request hover on 'function' keyword after the block comment (line 0, col 18)
    var result = try ctx.client.hover("file:///test.maxon", 0, 18);
    defer result.deinit();

    try testing.expect(result.content != null);
    const content = result.content.?;
    try testing.expect(std.mem.indexOf(u8, content, "function") != null);

    try ctx.deinit();
}

test "hover shows struct field info" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    const source =
        \\type MyStruct
        \\    var count int
        \\    var capacity int
        \\end 'MyStruct'
    ;

    try ctx.client.openDocument("file:///test.maxon", source);

    // Request hover on 'capacity' field declaration (line 2, col 8)
    var result = try ctx.client.hover("file:///test.maxon", 2, 8);
    defer result.deinit();

    try testing.expect(result.content != null);
    const content = result.content.?;
    // Should show field info, not array.capacity() function
    try testing.expect(std.mem.indexOf(u8, content, "capacity") != null);
    try testing.expect(std.mem.indexOf(u8, content, "function capacity(self array)") == null);

    try ctx.deinit();
}

test "hover shows variable type inside struct method" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    const source =
        \\type MyType
        \\    var value int
        \\
        \\    function doSomething() returns int
        \\        var count = 42
        \\        return count
        \\    end 'doSomething'
        \\end 'MyType'
    ;

    try ctx.client.openDocument("file:///test.maxon", source);

    // Request hover on 'count' in the return statement (line 5, col 15)
    var result = try ctx.client.hover("file:///test.maxon", 5, 15);
    defer result.deinit();

    try testing.expect(result.content != null);
    const content = result.content.?;
    try testing.expect(std.mem.indexOf(u8, content, "count") != null);
    try testing.expect(std.mem.indexOf(u8, content, "int") != null);

    try ctx.deinit();
}

test "hover shows variable type inside interface implementation method" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    const source =
        \\interface TestInterface
        \\    function doSomething() returns int
        \\end 'TestInterface'
        \\
        \\type MyType is TestInterface
        \\    var value int
        \\    function TestInterface.doSomething() returns int
        \\        var count = 42
        \\        return count
        \\    end 'doSomething'
        \\end 'MyType'
    ;

    try ctx.client.openDocument("file:///test.maxon", source);

    // Request hover on 'count' in the return statement (line 8, col 15)
    var result = try ctx.client.hover("file:///test.maxon", 8, 15);
    defer result.deinit();

    try testing.expect(result.content != null);
    const content = result.content.?;
    try testing.expect(std.mem.indexOf(u8, content, "count") != null);
    try testing.expect(std.mem.indexOf(u8, content, "int") != null);

    try ctx.deinit();
}

test "hover shows struct definition" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    const source =
        \\type Point
        \\    var x int
        \\    var y int
        \\end 'Point'
        \\
        \\function test() returns int
        \\    var p Point
        \\    return 0
        \\end 'test'
    ;

    try ctx.client.openDocument("file:///test.maxon", source);

    // Request hover on 'Point' in the var declaration (line 6, col 10)
    var result = try ctx.client.hover("file:///test.maxon", 6, 10);
    defer result.deinit();

    try testing.expect(result.content != null);
    const content = result.content.?;
    try testing.expect(std.mem.indexOf(u8, content, "Point") != null);

    try ctx.deinit();
}

test "hover shows user function signature" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    const source =
        \\function multiply(x int, y int) returns int
        \\    return x * y
        \\end 'multiply'
        \\
        \\function main() returns int
        \\    return multiply(3, 4)
        \\end 'main'
    ;

    try ctx.client.openDocument("file:///test.maxon", source);

    // Request hover on 'multiply' call (line 5, col 11)
    var result = try ctx.client.hover("file:///test.maxon", 5, 11);
    defer result.deinit();

    try testing.expect(result.content != null);
    const content = result.content.?;
    try testing.expect(std.mem.indexOf(u8, content, "function") != null);
    try testing.expect(std.mem.indexOf(u8, content, "multiply") != null);
    try testing.expect(std.mem.indexOf(u8, content, "x") != null);
    try testing.expect(std.mem.indexOf(u8, content, "y") != null);

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

test "definition for var variable" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    const source =
        \\function test() returns int
        \\    var counter = 0
        \\    counter = counter + 1
        \\    return counter
        \\end 'test'
    ;

    try ctx.client.openDocument("file:///test.maxon", source);

    // Go to definition on 'counter' usage in assignment (line 2, col 4)
    var result = try ctx.client.definition("file:///test.maxon", 2, 4);
    defer result.deinit();

    try testing.expect(result.uri != null);
    try testing.expectEqual(@as(u32, 1), result.line.?);

    try ctx.deinit();
}

test "definition for let variable" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    const source =
        \\function test() returns int
        \\    let value = 42
        \\    return value
        \\end 'test'
    ;

    try ctx.client.openDocument("file:///test.maxon", source);

    // Go to definition on 'value' usage in return (line 2, col 11)
    var result = try ctx.client.definition("file:///test.maxon", 2, 11);
    defer result.deinit();

    try testing.expect(result.uri != null);
    try testing.expectEqual(@as(u32, 1), result.line.?);

    try ctx.deinit();
}

test "definition for function parameter" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    const source =
        \\function add(a int, b int) returns int
        \\    return a + b
        \\end 'add'
    ;

    try ctx.client.openDocument("file:///test.maxon", source);

    // Go to definition on 'a' usage in return (line 1, col 11)
    var result = try ctx.client.definition("file:///test.maxon", 1, 11);
    defer result.deinit();

    try testing.expect(result.uri != null);
    try testing.expectEqual(@as(u32, 0), result.line.?);

    try ctx.deinit();
}

test "definition for struct type" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    const source =
        \\type Point
        \\    var x int
        \\    var y int
        \\end 'Point'
        \\
        \\function test() returns int
        \\    var p = Point{x: 0, y: 0}
        \\    return 0
        \\end 'test'
    ;

    try ctx.client.openDocument("file:///test.maxon", source);

    // Go to definition on 'Point' type in struct literal (line 6, col 12)
    var result = try ctx.client.definition("file:///test.maxon", 6, 12);
    defer result.deinit();

    try testing.expect(result.uri != null);
    try testing.expectEqual(@as(u32, 0), result.line.?);

    try ctx.deinit();
}

test "definition for struct field" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    const source =
        \\type Point
        \\    var x int
        \\    var y int
        \\end 'Point'
        \\
        \\function test() returns int
        \\    var p = Point{x: 0, y: 0}
        \\    p.x = 10
        \\    return p.x
        \\end 'test'
    ;

    try ctx.client.openDocument("file:///test.maxon", source);

    // Go to definition on 'x' field access (line 7, col 6)
    var result = try ctx.client.definition("file:///test.maxon", 7, 6);
    defer result.deinit();

    try testing.expect(result.uri != null);
    try testing.expectEqual(@as(u32, 1), result.line.?);

    try ctx.deinit();
}

test "definition for interface type" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    const source =
        \\interface Printable
        \\    function print() returns int
        \\end 'Printable'
        \\
        \\type Message is Printable
        \\    var text string
        \\
        \\    function Printable.print() returns int
        \\        return 42
        \\    end 'print'
        \\end 'Message'
    ;

    try ctx.client.openDocument("file:///test.maxon", source);

    // Go to definition on 'Printable' in conformance (line 4, col 18)
    var result = try ctx.client.definition("file:///test.maxon", 4, 18);
    defer result.deinit();

    try testing.expect(result.uri != null);
    try testing.expectEqual(@as(u32, 0), result.line.?);

    try ctx.deinit();
}

test "definition for variable in nested scope" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    const source =
        \\function test() returns int
        \\    var outer = 1
        \\    if outer == 1 'check'
        \\        var inner = 2
        \\        return inner + outer
        \\    end 'check'
        \\    return outer
        \\end 'test'
    ;

    try ctx.client.openDocument("file:///test.maxon", source);

    // Go to definition on 'inner' usage (line 4, col 15)
    var result = try ctx.client.definition("file:///test.maxon", 4, 15);
    defer result.deinit();

    try testing.expect(result.uri != null);
    try testing.expectEqual(@as(u32, 3), result.line.?);

    try ctx.deinit();
}

test "definition on keyword returns nothing" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    const source =
        \\function test() returns int
        \\    return 42
        \\end 'test'
    ;

    try ctx.client.openDocument("file:///test.maxon", source);

    // Try go to definition on 'return' keyword (line 1, col 4)
    var result = try ctx.client.definition("file:///test.maxon", 1, 4);
    defer result.deinit();

    try testing.expect(result.uri == null);

    try ctx.deinit();
}

test "definition on number literal returns nothing" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    const source =
        \\function test() returns int
        \\    return 42
        \\end 'test'
    ;

    try ctx.client.openDocument("file:///test.maxon", source);

    // Try go to definition on '42' literal (line 1, col 11)
    var result = try ctx.client.definition("file:///test.maxon", 1, 11);
    defer result.deinit();

    try testing.expect(result.uri == null);

    try ctx.deinit();
}

test "definition for recursive function call" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    const source =
        \\function factorial(n int) returns int
        \\    if n <= 1 'base'
        \\        return 1
        \\    end 'base'
        \\    return n * factorial(n - 1)
        \\end 'factorial'
    ;

    try ctx.client.openDocument("file:///test.maxon", source);

    // Go to definition on recursive 'factorial' call (line 4, col 16)
    var result = try ctx.client.definition("file:///test.maxon", 4, 16);
    defer result.deinit();

    try testing.expect(result.uri != null);
    try testing.expectEqual(@as(u32, 0), result.line.?);

    try ctx.deinit();
}

test "definition for struct in return type" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    const source =
        \\type Point
        \\    var x int
        \\    var y int
        \\end 'Point'
        \\
        \\function createPoint() returns Point
        \\    return Point{x: 0, y: 0}
        \\end 'createPoint'
    ;

    try ctx.client.openDocument("file:///test.maxon", source);

    // Go to definition on 'Point' return type (line 5, col 31)
    var result = try ctx.client.definition("file:///test.maxon", 5, 31);
    defer result.deinit();

    try testing.expect(result.uri != null);
    try testing.expectEqual(@as(u32, 0), result.line.?);

    try ctx.deinit();
}

test "definition for struct literal type" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    const source =
        \\type Point
        \\    var x int
        \\    var y int
        \\end 'Point'
        \\
        \\function test() returns Point
        \\    return Point{x: 0, y: 0}
        \\end 'test'
    ;

    try ctx.client.openDocument("file:///test.maxon", source);

    // Go to definition on 'Point' in struct literal (line 6, col 11)
    var result = try ctx.client.definition("file:///test.maxon", 6, 11);
    defer result.deinit();

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

    // Code with else block - uses correct Maxon syntax: end 'label' else 'else_label'
    const source =
        \\function test() returns int
        \\if true 'check'
        \\return 1
        \\end 'check' else 'other'
        \\return 0
        \\end 'other'
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

    // The else line should be indented 1 level (same as if)
    try testing.expect(std.mem.indexOf(u8, new_text, "\tend 'check' else 'other'") != null);

    // The end 'other' should be indented 1 level
    try testing.expect(std.mem.indexOf(u8, new_text, "\tend 'other'") != null);

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

    // Should have folding ranges for function (lines 0-5) and if block (lines 1-3)
    try testing.expectEqual(@as(usize, 2), result.ranges.len);

    // Verify we have ranges for both blocks
    var found_function = false;
    var found_if = false;
    for (result.ranges) |range| {
        if (range.start_line == 0 and range.end_line == 5) {
            found_function = true;
        }
        if (range.start_line == 1 and range.end_line == 3) {
            found_if = true;
        }
    }
    try testing.expect(found_function);
    try testing.expect(found_if);

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

// ============================================================================
// TDD: Rename Tests (server does not implement textDocument/rename yet)
// These tests will fail until the feature is implemented
// ============================================================================

test "rename block identifier updates all occurrences" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    const source =
        \\function test() returns int
        \\    if true 'myBlock'
        \\        return 1
        \\    end 'myBlock'
        \\    return 0
        \\end 'test'
    ;

    try ctx.client.openDocument("file:///test.maxon", source);

    // Request rename on 'myBlock' (line 1, col 14)
    var result = try ctx.client.rename("file:///test.maxon", 1, 14, "newBlock");
    defer result.deinit();

    // Should have edits for both occurrences
    try testing.expect(result.has_edits);
    try testing.expectEqual(@as(usize, 2), result.edit_count);

    try ctx.deinit();
}

test "rename function updates function name and end label" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    const source =
        \\function oldName() returns int
        \\    return 42
        \\end 'oldName'
    ;

    try ctx.client.openDocument("file:///test.maxon", source);

    // Request rename on 'oldName' (line 0, col 12)
    var result = try ctx.client.rename("file:///test.maxon", 0, 12, "newName");
    defer result.deinit();

    // Should have edits for function name and end label
    try testing.expect(result.has_edits);
    try testing.expectEqual(@as(usize, 2), result.edit_count);

    try ctx.deinit();
}

// ============================================================================
// TDD: Code Action Tests (server does not implement textDocument/codeAction yet)
// ============================================================================

test "code action offers fix for unused variable" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    const source =
        \\function test() returns int
        \\    var unused = 42
        \\    return 0
        \\end 'test'
    ;

    try ctx.client.openDocument("file:///test.maxon", source);

    // Request code actions on the unused variable line
    var result = try ctx.client.codeAction("file:///test.maxon", 1, 4, 1, 19);
    defer result.deinit();

    // Should have at least one code action (e.g., remove unused variable)
    try testing.expect(result.action_count > 0);

    try ctx.deinit();
}

// ============================================================================
// TDD: Semantic Tokens Tests (server does not implement semanticTokens yet)
// ============================================================================

test "semantic tokens returns token data" {
    var ctx = try TestContext.init();
    errdefer ctx.forceCleanup();

    const source =
        \\function test() returns int
        \\    var x = 42
        \\    return x
        \\end 'test'
    ;

    try ctx.client.openDocument("file:///test.maxon", source);

    var result = try ctx.client.semanticTokensFull("file:///test.maxon");
    defer result.deinit();

    // Should have semantic token data
    try testing.expect(result.has_data);
    try testing.expect(result.data_length > 0);

    try ctx.deinit();
}
