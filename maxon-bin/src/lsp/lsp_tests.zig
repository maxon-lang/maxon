const std = @import("std");
const test_client = @import("test_client.zig");
const TestClient = test_client.TestClient;
const testing = std.testing;

// ============================================================================
// LSP Tests
// In-process LSP tests using TestClient
// ============================================================================

// Fast allocator that detects leaks without slow stack traces
var test_gpa = std.heap.GeneralPurposeAllocator(.{}){};
const test_allocator = test_gpa.allocator();

test "initialize returns capabilities" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();

    const result = try client.initialize();

    try testing.expect(result.has_text_document_sync);
    try testing.expect(result.has_completion_provider);
}

test "shutdown and exit terminate cleanly" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();
}

test "server handles full workflow" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

    // Do some work that allocates memory
    try client.openDocument("file:///test.maxon",
        \\function main() returns int
        \\    var x = 42
        \\    return x
        \\end 'main'
    );
    try client.changeDocument("file:///test.maxon",
        \\function main() returns int
        \\    var arr = [1, 2, 3]
        \\    arr.push(4)
        \\    return 0
        \\end 'main'
    , 2);

    // Request completion after "arr." (line 2, column 8)
    // This should return array methods
    var result = try client.completionWithTrigger("file:///test.maxon", 2, 8, ".");
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

    try client.closeDocument("file:///test.maxon");
}

test "full lifecycle with document" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

    // Open, modify, and close a document - verify no errors
    try client.openDocument("file:///test.maxon",
        \\function main() returns int
        \\    var x = 1
        \\    return x
        \\end 'main'
    );

    // Request hover on 'x' variable at line 1, character 8
    // Line 1: "    var x = 1" - 'x' is at position 8
    var hover_result = try client.hover("file:///test.maxon", 1, 8);
    defer hover_result.deinit();
    try testing.expect(hover_result.contents.len > 0);
    // Verify hover shows the variable type (int)
    try testing.expect(std.mem.indexOf(u8, hover_result.contents, "int") != null);

    try client.changeDocument("file:///test.maxon",
        \\function main() returns int
        \\    var x = 2
        \\    return x
        \\end 'main'
    , 2);
    try client.closeDocument("file:///test.maxon");
}

test "didOpen registers document" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

    try client.openDocument("file:///test.maxon",
        \\function main() returns int
        \\    return 0
        \\end 'main'
    );

    // Verify document is accessible by requesting symbols
    var result = try client.documentSymbols("file:///test.maxon");
    defer result.deinit();
    try testing.expectEqual(@as(usize, 1), result.symbols.len);
    try testing.expect(std.mem.eql(u8, result.symbols[0].name, "main"));
    try testing.expectEqual(@as(i64, 12), @intFromEnum(result.symbols[0].kind)); // function = 12
}

test "didChange updates document content" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

    try client.openDocument("file:///test.maxon",
        \\function foo() returns int
        \\    return 1
        \\end 'foo'
    );

    // Verify initial content
    var result1 = try client.documentSymbols("file:///test.maxon");
    defer result1.deinit();
    try testing.expect(result1.symbols.len == 1);
    try testing.expect(std.mem.eql(u8, result1.symbols[0].name, "foo"));

    // Update to add another function
    try client.changeDocument("file:///test.maxon",
        \\function foo() returns int
        \\    return 1
        \\end 'foo'
        \\
        \\function bar() returns int
        \\    return 2
        \\end 'bar'
    , 2);

    // Verify updated content has both functions
    var result2 = try client.documentSymbols("file:///test.maxon");
    defer result2.deinit();
    try testing.expectEqual(@as(usize, 2), result2.symbols.len);
}

test "didClose removes document" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

    try client.openDocument("file:///test.maxon",
        \\function main() returns int
        \\    return 0
        \\end 'main'
    );

    // Verify document exists
    var result1 = try client.documentSymbols("file:///test.maxon");
    defer result1.deinit();
    try testing.expect(result1.symbols.len >= 1);

    try client.closeDocument("file:///test.maxon");

    // After close, symbols request should return empty
    var result2 = try client.documentSymbols("file:///test.maxon");
    defer result2.deinit();
    try testing.expectEqual(@as(usize, 0), result2.symbols.len);
}

test "completion for array methods" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

    // Open a document with an array literal - use valid complete code first
    // LSP lines are 0-indexed, so line 2 is "    arr."
    const source =
        \\function main() returns int
        \\    var arr = [1, 2, 3]
        \\    arr.push(4)
        \\    return 0
        \\end 'main'
    ;

    try client.openDocument("file:///test.maxon", source);

    // Request completion after "arr." (line 2, column 8 - right after the dot)
    // Line 2 = "    arr.push(4)" - after "arr." is column 8
    // Use trigger character "." to simulate typing a dot
    var result = try client.completionWithTrigger("file:///test.maxon", 2, 8, ".");
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
        "resize",
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
        try testing.expect(found);
    }
}

test "completion for string methods" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

    const source =
        \\function main() returns int
        \\    var s = "hello"
        \\    return s.cstr()
        \\end 'main'
    ;

    try client.openDocument("file:///test.maxon", source);

    // Request completion after "s." (line 2, column 13)
    var result = try client.completionWithTrigger("file:///test.maxon", 2, 13, ".");
    defer result.deinit();

    // Check we got some string completions
    try testing.expect(result.items.len > 0);

    // Verify cstr is in the completions (it's a valid string method)
    var found_cstr = false;
    for (result.items) |item| {
        if (std.mem.eql(u8, item.label, "cstr")) {
            found_cstr = true;
            break;
        }
    }
    try testing.expect(found_cstr);
}

test "completion for struct fields" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

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

    try client.openDocument("file:///test.maxon", source);

    // Request completion after "p." (line 7, column 13)
    var result = try client.completionWithTrigger("file:///test.maxon", 7, 13, ".");
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
}

test "completion for enum members" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

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

    try client.openDocument("file:///test.maxon", source);

    // Request completion after "Color." (line 7, column 17)
    var result = try client.completionWithTrigger("file:///test.maxon", 7, 17, ".");
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
}

test "hover shows value for immutable variables" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

    const source =
        \\function main() returns int
        \\    let PI = 3.14159
        \\    return 0
        \\end 'main'
    ;

    try client.openDocument("file:///test.maxon", source);

    // Request hover on 'PI' at line 1, character 8 (start of "PI")
    // Line 1: "    let PI = 3.14159"
    //          01234567890
    var result = try client.hover("file:///test.maxon", 1, 8);
    defer result.deinit();

    // The hover should contain the value for immutable variables
    try testing.expect(result.contents.len > 0);
    const content = result.contents;
    try testing.expect(std.mem.indexOf(u8, content, "3.14159") != null);
}

test "hover shows type and value for immutable variable at usage site" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

    const source =
        \\function main() returns int
        \\    let SOLAR_MASS = 39.478417604357432
        \\    let mass = 9.547 * SOLAR_MASS
        \\    return 0
        \\end 'main'
    ;

    try client.openDocument("file:///test.maxon", source);

    // Request hover on 'SOLAR_MASS' at usage site (line 2, col 21)
    // Line 2: "    let mass = 9.547 * SOLAR_MASS"
    //          0123456789012345678901234567890
    var result = try client.hover("file:///test.maxon", 2, 23);
    defer result.deinit();

    // The hover should contain both the type and value for immutable variables
    try testing.expect(result.contents.len > 0);
    const content = result.contents;
    // Should show the value (may be truncated due to float formatting)
    try testing.expect(std.mem.indexOf(u8, content, "39.478417") != null);
    // Should show the type
    try testing.expect(std.mem.indexOf(u8, content, "float") != null);
}

test "hover shows local function signature" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

    const source =
        \\function add(a int, b int) returns int
        \\    return a + b
        \\end 'add'
        \\
        \\function main() returns int
        \\    return add(1, 2)
        \\end 'main'
    ;

    try client.openDocument("file:///test.maxon", source);

    // Request hover on 'add' at the call site (line 5)
    // Line 5: "    return add(1, 2)" - 'add' starts at position 11
    var result = try client.hover("file:///test.maxon", 5, 12);
    defer result.deinit();

    // The hover should show the function signature
    try testing.expect(result.contents.len > 0);
    const content = result.contents;
    try testing.expect(std.mem.indexOf(u8, content, "function add") != null);
    try testing.expect(std.mem.indexOf(u8, content, "a int") != null);
}

test "hover shows correct type for function parameter" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

    const source =
        \\function greet(name string) returns int
        \\    var x = name
        \\    return 0
        \\end 'greet'
    ;

    try client.openDocument("file:///test.maxon", source);

    // Request hover on 'name' at line 1
    // Line 1: "    var x = name" - 'name' starts at position 12
    var result = try client.hover("file:///test.maxon", 1, 13);
    defer result.deinit();

    // The hover should show the parameter type as string
    try testing.expect(result.contents.len > 0);
    const content = result.contents;
    try testing.expect(std.mem.indexOf(u8, content, "string") != null);
    try testing.expect(std.mem.indexOf(u8, content, "float") == null);
}

test "hover shows intrinsic signature" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

    const source =
        \\function test()
        \\    var cs = "test".cstr()
        \\    var arr = [1 as byte, 2 as byte]
        \\    __write_file_binary(cs, arr)
        \\end 'test'
    ;

    try client.openDocument("file:///test.maxon", source);

    // Request hover on '__write_file_binary' at line 3
    var result = try client.hover("file:///test.maxon", 3, 10);
    defer result.deinit();

    // The hover should show the function signature
    try testing.expect(result.contents.len > 0);
    const content = result.contents;
    try testing.expect(std.mem.indexOf(u8, content, "function") != null);
    try testing.expect(std.mem.indexOf(u8, content, "__write_file_binary") != null);
}

test "hover shows math builtin signature and help text" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

    const source =
        \\function test() returns int
        \\    let x = sqrt(16.0)
        \\    return trunc(x)
        \\end 'test'
    ;

    try client.openDocument("file:///test.maxon", source);

    // Request hover on 'sqrt' at line 1
    // Line 1: "    let x = sqrt(16.0)" - 'sqrt' starts at position 12
    var result = try client.hover("file:///test.maxon", 1, 13);
    defer result.deinit();

    // The hover should show the function signature and help text
    try testing.expect(result.contents.len > 0);
    const content = result.contents;

    // Should show signature
    try testing.expect(std.mem.indexOf(u8, content, "sqrt") != null);
    try testing.expect(std.mem.indexOf(u8, content, "float") != null);

    // Should show help text from registry
    try testing.expect(std.mem.indexOf(u8, content, "square root") != null);
}

test "hover shows trunc builtin with help text" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

    const source =
        \\function test() returns int
        \\    return trunc(3.7)
        \\end 'test'
    ;

    try client.openDocument("file:///test.maxon", source);

    // Request hover on 'trunc' at line 1
    // Line 1: "    return trunc(3.7)" - 'trunc' starts at position 11
    var result = try client.hover("file:///test.maxon", 1, 12);
    defer result.deinit();

    try testing.expect(result.contents.len > 0);
    const content = result.contents;

    // Should show signature with parameter name and types
    try testing.expect(std.mem.indexOf(u8, content, "trunc") != null);
    try testing.expect(std.mem.indexOf(u8, content, "value") != null);
    try testing.expect(std.mem.indexOf(u8, content, "float") != null);
    try testing.expect(std.mem.indexOf(u8, content, "int") != null);

    // Should show help text
    try testing.expect(std.mem.indexOf(u8, content, "Truncates") != null);
}

test "hover shows keyword info" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

    const source =
        \\function test() returns int
        \\    return 42
        \\end 'test'
    ;

    try client.openDocument("file:///test.maxon", source);

    // Request hover on 'return' keyword at line 1, character 4
    var result = try client.hover("file:///test.maxon", 1, 4);
    defer result.deinit();

    try testing.expect(result.contents.len > 0);
    const content = result.contents;
    try testing.expect(std.mem.indexOf(u8, content, "return") != null);
}

test "hover shows type keyword info" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

    const source =
        \\type MyStruct
        \\    var value int
        \\end 'MyStruct'
    ;

    try client.openDocument("file:///test.maxon", source);

    // Request hover on 'type' keyword at line 0, character 2
    var result = try client.hover("file:///test.maxon", 0, 2);
    defer result.deinit();

    try testing.expect(result.contents.len > 0);
    const content = result.contents;
    try testing.expect(std.mem.indexOf(u8, content, "type") != null);
}

test "hover excludes text in line comments" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

    const source =
        \\// This function uses for loops and if statements
        \\function test() returns int
        \\    return 42
        \\end 'test'
    ;

    try client.openDocument("file:///test.maxon", source);

    // Request hover on 'for' inside the line comment (line 0, col 24)
    // Should return error since there's no hover for text inside comments
    const result = client.hover("file:///test.maxon", 0, 24);
    try testing.expectError(error.NoHoverInfo, result);
}

test "hover excludes text in block comments" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

    const source =
        \\/* This is a block comment
        \\   that mentions for loops
        \\   and if statements */
        \\function test() returns int
        \\    return 42
        \\end 'test'
    ;

    try client.openDocument("file:///test.maxon", source);

    // Request hover on 'for' in the block comment (line 1, col 17)
    // Should return error since there's no hover for text inside comments
    const result = client.hover("file:///test.maxon", 1, 17);
    try testing.expectError(error.NoHoverInfo, result);
}

test "hover works after block comment ends" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

    const source =
        \\/* comment */ function test() returns int
        \\    return 42
        \\end 'test'
    ;

    try client.openDocument("file:///test.maxon", source);

    // Request hover on 'function' keyword after the block comment (line 0, col 18)
    var result = try client.hover("file:///test.maxon", 0, 18);
    defer result.deinit();

    try testing.expect(result.contents.len > 0);
    const content = result.contents;
    try testing.expect(std.mem.indexOf(u8, content, "function") != null);
}

test "hover shows struct field info" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

    const source =
        \\type MyStruct
        \\    var count int
        \\    var capacity int
        \\end 'MyStruct'
    ;

    try client.openDocument("file:///test.maxon", source);

    // Request hover on 'capacity' field declaration (line 2, col 8)
    var result = try client.hover("file:///test.maxon", 2, 8);
    defer result.deinit();

    try testing.expect(result.contents.len > 0);
    const content = result.contents;
    // Should show field info, not array.capacity() function
    try testing.expect(std.mem.indexOf(u8, content, "capacity") != null);
    try testing.expect(std.mem.indexOf(u8, content, "function capacity(self array)") == null);
}

test "hover shows variable type inside struct method" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

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

    try client.openDocument("file:///test.maxon", source);

    // Request hover on 'count' in the return statement (line 5, col 15)
    var result = try client.hover("file:///test.maxon", 5, 15);
    defer result.deinit();

    try testing.expect(result.contents.len > 0);
    const content = result.contents;
    try testing.expect(std.mem.indexOf(u8, content, "count") != null);
    try testing.expect(std.mem.indexOf(u8, content, "int") != null);
}

test "hover shows variable type inside interface implementation method" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

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

    try client.openDocument("file:///test.maxon", source);

    // Request hover on 'count' in the return statement (line 8, col 15)
    var result = try client.hover("file:///test.maxon", 8, 15);
    defer result.deinit();

    try testing.expect(result.contents.len > 0);
    const content = result.contents;
    try testing.expect(std.mem.indexOf(u8, content, "count") != null);
    try testing.expect(std.mem.indexOf(u8, content, "int") != null);
}

test "hover shows array type" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

    const source =
        \\function main() returns int
        \\    var arr = [1, 2, 3]
        \\    arr.push(4)
        \\    return 0
        \\end 'main'
    ;

    try client.openDocument("file:///test.maxon", source);

    // Request hover on 'arr' at line 1 (var arr = [1, 2, 3])
    var result = try client.hover("file:///test.maxon", 1, 8);
    defer result.deinit();

    // The hover should show the array type with element type
    try testing.expect(result.contents.len > 0);
    const content = result.contents;
    try testing.expect(std.mem.indexOf(u8, content, "arr") != null);
    // Should show "Array of int" for the element type
    try testing.expect(std.mem.indexOf(u8, content, "Array of int") != null);
}

test "hover shows array of struct type" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

    const source =
        \\type Point
        \\    var x int
        \\    var y int
        \\end 'Point'
        \\
        \\function main() returns int
        \\    var points = [Point{x: 1, y: 2}, Point{x: 3, y: 4}]
        \\    return 0
        \\end 'main'
    ;

    try client.openDocument("file:///test.maxon", source);

    // Request hover on 'points' at line 6 (var points = ...)
    var result = try client.hover("file:///test.maxon", 6, 8);
    defer result.deinit();

    // The hover should show "Array of Point"
    try testing.expect(result.contents.len > 0);
    const content = result.contents;
    try testing.expect(std.mem.indexOf(u8, content, "points") != null);
    try testing.expect(std.mem.indexOf(u8, content, "Array of Point") != null);
}

test "hover shows struct definition" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

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

    try client.openDocument("file:///test.maxon", source);

    // Request hover on 'Point' in the struct literal (line 6, col 12)
    var result = try client.hover("file:///test.maxon", 6, 12);
    defer result.deinit();

    try testing.expect(result.contents.len > 0);
    const content = result.contents;
    try testing.expect(std.mem.indexOf(u8, content, "Point") != null);
}

test "hover shows user function signature" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

    const source =
        \\function multiply(x int, y int) returns int
        \\    return x * y
        \\end 'multiply'
        \\
        \\function main() returns int
        \\    return multiply(3, 4)
        \\end 'main'
    ;

    try client.openDocument("file:///test.maxon", source);

    // Request hover on 'multiply' call (line 5, col 11)
    var result = try client.hover("file:///test.maxon", 5, 11);
    defer result.deinit();

    try testing.expect(result.contents.len > 0);
    const content = result.contents;
    try testing.expect(std.mem.indexOf(u8, content, "function") != null);
    try testing.expect(std.mem.indexOf(u8, content, "multiply") != null);
    try testing.expect(std.mem.indexOf(u8, content, "x") != null);
    try testing.expect(std.mem.indexOf(u8, content, "y") != null);
}

test "hover shows doc comment for function" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

    const source =
        \\/// Adds two numbers together
        \\function add(x int, y int) returns int
        \\    return x + y
        \\end 'add'
        \\
        \\function main() returns int
        \\    return add(1, 2)
        \\end 'main'
    ;

    try client.openDocument("file:///test.maxon", source);

    // Request hover on 'add' call (line 6: "    return add(1, 2)")
    // "add" starts at column 11
    var result = try client.hover("file:///test.maxon", 6, 11);
    defer result.deinit();

    try testing.expect(result.contents.len > 0);
    const content = result.contents;
    // Should show the function signature
    try testing.expect(std.mem.indexOf(u8, content, "function") != null);
    try testing.expect(std.mem.indexOf(u8, content, "add") != null);
    // Should show the doc comment
    try testing.expect(std.mem.indexOf(u8, content, "Adds two numbers together") != null);
}

test "hover shows doc comment for method" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

    const source =
        \\type Counter
        \\    var count int
        \\
        \\    /// Increments the counter by one
        \\    function increment()
        \\        self.count = self.count + 1
        \\    end 'increment'
        \\end 'Counter'
        \\
        \\function main() returns int
        \\    var c = Counter{count: 0}
        \\    c.increment()
        \\    return c.count
        \\end 'main'
    ;

    try client.openDocument("file:///test.maxon", source);

    // Request hover on 'increment' method call (line 11: "    c.increment()")
    // "increment" starts at column 6
    var result = try client.hover("file:///test.maxon", 11, 6);
    defer result.deinit();

    try testing.expect(result.contents.len > 0);
    const content = result.contents;
    // Should show the method signature
    try testing.expect(std.mem.indexOf(u8, content, "increment") != null);
    // Should show the doc comment
    try testing.expect(std.mem.indexOf(u8, content, "Increments the counter by one") != null);
}

test "hover shows array push method with doc comment" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

    const source =
        \\function main() returns int
        \\    var arr = [1, 2, 3]
        \\    arr.push(4)
        \\    return 0
        \\end 'main'
    ;

    try client.openDocument("file:///test.maxon", source);

    // Request hover on 'push' method call (line 2: "    arr.push(4)")
    // "push" starts at column 8
    var result = try client.hover("file:///test.maxon", 2, 8);
    defer result.deinit();

    try testing.expect(result.contents.len > 0);
    const content = result.contents;
    // Should show the method signature for push
    try testing.expect(std.mem.indexOf(u8, content, "push") != null);
    // Should show the doc comment from stdlib Array.maxon
    try testing.expect(std.mem.indexOf(u8, content, "Append element to end") != null);
}

test "definition returns location" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

    const source =
        \\function helper() returns int
        \\    return 42
        \\end 'helper'
        \\
        \\function main() returns int
        \\    return helper()
        \\end 'main'
    ;

    try client.openDocument("file:///test.maxon", source);

    // Request definition on 'helper' call at line 5, character 11 (inside "helper")
    var result = try client.definition("file:///test.maxon", 5, 11);
    defer result.deinit();

    // Should point to helper() definition at line 0
    try testing.expect(result.uri.len > 0);
    try testing.expectEqual(@as(u32, 0), result.start_line);
}

test "definition for var variable" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

    const source =
        \\function test() returns int
        \\    var counter = 0
        \\    counter = counter + 1
        \\    return counter
        \\end 'test'
    ;

    try client.openDocument("file:///test.maxon", source);

    // Go to definition on 'counter' usage in assignment (line 2, col 4)
    var result = try client.definition("file:///test.maxon", 2, 4);
    defer result.deinit();

    try testing.expect(result.uri.len > 0);
    try testing.expectEqual(@as(u32, 1), result.start_line);
}

test "definition for let variable" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

    const source =
        \\function test() returns int
        \\    let value = 42
        \\    return value
        \\end 'test'
    ;

    try client.openDocument("file:///test.maxon", source);

    // Go to definition on 'value' usage in return (line 2, col 11)
    var result = try client.definition("file:///test.maxon", 2, 11);
    defer result.deinit();

    try testing.expect(result.uri.len > 0);
    try testing.expectEqual(@as(u32, 1), result.start_line);
}

test "definition for function parameter" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

    const source =
        \\function add(a int, b int) returns int
        \\    return a + b
        \\end 'add'
    ;

    try client.openDocument("file:///test.maxon", source);

    // Go to definition on 'a' usage in return (line 1, col 11)
    var result = try client.definition("file:///test.maxon", 1, 11);
    defer result.deinit();

    try testing.expect(result.uri.len > 0);
    try testing.expectEqual(@as(u32, 0), result.start_line);
}

test "definition for struct type" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

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

    try client.openDocument("file:///test.maxon", source);

    // Go to definition on 'Point' type in struct literal (line 6, col 12)
    var result = try client.definition("file:///test.maxon", 6, 12);
    defer result.deinit();

    try testing.expect(result.uri.len > 0);
    try testing.expectEqual(@as(u32, 0), result.start_line);
}

test "definition for struct field" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

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

    try client.openDocument("file:///test.maxon", source);

    // Go to definition on 'x' field access (line 7, col 6)
    var result = try client.definition("file:///test.maxon", 7, 6);
    defer result.deinit();

    try testing.expect(result.uri.len > 0);
    try testing.expectEqual(@as(u32, 1), result.start_line);
}

test "definition for interface type" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

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

    try client.openDocument("file:///test.maxon", source);

    // Go to definition on 'Printable' in conformance (line 4, col 18)
    var result = try client.definition("file:///test.maxon", 4, 18);
    defer result.deinit();

    try testing.expect(result.uri.len > 0);
    try testing.expectEqual(@as(u32, 0), result.start_line);
}

test "definition for variable in nested scope" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

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

    try client.openDocument("file:///test.maxon", source);

    // Go to definition on 'inner' usage (line 4, col 15)
    var result = try client.definition("file:///test.maxon", 4, 15);
    defer result.deinit();

    try testing.expect(result.uri.len > 0);
    try testing.expectEqual(@as(u32, 3), result.start_line);
}

test "definition on keyword returns nothing" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

    const source =
        \\function test() returns int
        \\    return 42
        \\end 'test'
    ;

    try client.openDocument("file:///test.maxon", source);

    // Try go to definition on 'return' keyword (line 1, col 4)
    var result = try client.definition("file:///test.maxon", 1, 4);
    defer result.deinit();

    try testing.expect(result.uri.len == 0);
}

test "definition on number literal returns nothing" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

    const source =
        \\function test() returns int
        \\    return 42
        \\end 'test'
    ;

    try client.openDocument("file:///test.maxon", source);

    // Try go to definition on '42' literal (line 1, col 11)
    var result = try client.definition("file:///test.maxon", 1, 11);
    defer result.deinit();

    try testing.expect(result.uri.len == 0);
}

test "definition for recursive function call" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

    const source =
        \\function factorial(n int) returns int
        \\    if n <= 1 'base'
        \\        return 1
        \\    end 'base'
        \\    return n * factorial(n - 1)
        \\end 'factorial'
    ;

    try client.openDocument("file:///test.maxon", source);

    // Go to definition on recursive 'factorial' call (line 4, col 16)
    var result = try client.definition("file:///test.maxon", 4, 16);
    defer result.deinit();

    try testing.expect(result.uri.len > 0);
    try testing.expectEqual(@as(u32, 0), result.start_line);
}

test "definition for struct in return type" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

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

    try client.openDocument("file:///test.maxon", source);

    // Go to definition on 'Point' return type (line 5, col 31)
    var result = try client.definition("file:///test.maxon", 5, 31);
    defer result.deinit();

    try testing.expect(result.uri.len > 0);
    try testing.expectEqual(@as(u32, 0), result.start_line);
}

test "definition for struct literal type" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

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

    try client.openDocument("file:///test.maxon", source);

    // Go to definition on 'Point' in struct literal (line 6, col 11)
    var result = try client.definition("file:///test.maxon", 6, 11);
    defer result.deinit();

    try testing.expect(result.uri.len > 0);
    try testing.expectEqual(@as(u32, 0), result.start_line);
}

test "formatting indents nested code correctly" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

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
    try client.openDocument("file:///test.maxon", source);

    var result = try client.formatting("file:///test.maxon", 4, false);
    defer result.deinit();

    // Verify we got formatted text
    try testing.expect(result.new_text.len > 0);
    const new_text = result.new_text;

    // Check that var x is indented (should have a tab before it)
    try testing.expect(std.mem.indexOf(u8, new_text, "\tvar x") != null);

    // Check that var y inside if block has two tabs
    try testing.expect(std.mem.indexOf(u8, new_text, "\t\tvar y") != null);

    // Check that end 'check' has one tab
    try testing.expect(std.mem.indexOf(u8, new_text, "\tend 'check'") != null);

    // Check that end 'main' has no leading tab
    try testing.expect(std.mem.indexOf(u8, new_text, "\nend 'main'") != null);
}

test "formatting indents type fields correctly" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

    // Type with no indentation on fields
    const source =
        \\type Point
        \\var x int
        \\var y int
        \\end 'Point'
    ;
    try client.openDocument("file:///test.maxon", source);

    var result = try client.formatting("file:///test.maxon", 4, false);
    defer result.deinit();

    try testing.expect(result.new_text.len > 0);
    const new_text = result.new_text;

    // Struct fields should be indented one level
    try testing.expect(std.mem.indexOf(u8, new_text, "\tvar x int") != null);
    try testing.expect(std.mem.indexOf(u8, new_text, "\tvar y int") != null);

    // end should be at level 0
    try testing.expect(std.mem.indexOf(u8, new_text, "\nend 'Point'") != null);
}

test "formatting handles else blocks correctly" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

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
    try client.openDocument("file:///test.maxon", source);

    var result = try client.formatting("file:///test.maxon", 4, false);
    defer result.deinit();

    try testing.expect(result.new_text.len > 0);
    const new_text = result.new_text;

    // The if should be indented 1 level
    try testing.expect(std.mem.indexOf(u8, new_text, "\tif true") != null);

    // The first return should be indented 2 levels
    try testing.expect(std.mem.indexOf(u8, new_text, "\t\treturn 1") != null);

    // The else line should be indented 1 level (same as if)
    try testing.expect(std.mem.indexOf(u8, new_text, "\tend 'check' else 'other'") != null);

    // The end 'other' should be indented 1 level
    try testing.expect(std.mem.indexOf(u8, new_text, "\tend 'other'") != null);
}

test "formatting uses spaces when insertSpaces is true" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

    // Code with no indentation
    const source =
        \\function main() returns int
        \\var x = 1
        \\return x
        \\end 'main'
    ;
    try client.openDocument("file:///test.maxon", source);

    var result = try client.formatting("file:///test.maxon", 4, true);
    defer result.deinit();

    try testing.expect(result.new_text.len > 0);
    const new_text = result.new_text;

    // With insertSpaces=true and tabSize=4, indentation should be 4 spaces
    try testing.expect(std.mem.indexOf(u8, new_text, "    var x") != null);
    try testing.expect(std.mem.indexOf(u8, new_text, "    return x") != null);

    // Should NOT have tabs
    try testing.expect(std.mem.indexOf(u8, new_text, "\tvar") == null);
}

test "formatting preserves implicit type declarations" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

    // Code with type inference - no explicit type annotations
    const source =
        \\function main() returns int
        \\var x = 1
        \\let y = 2.5
        \\return x
        \\end 'main'
    ;
    try client.openDocument("file:///test.maxon", source);

    var result = try client.formatting("file:///test.maxon", 4, false);
    defer result.deinit();

    try testing.expect(result.new_text.len > 0);
    const new_text = result.new_text;

    // The formatter should NOT add explicit type annotations
    // "var x = 1" should stay as "var x = 1", not become "var x int = 1"
    try testing.expect(std.mem.indexOf(u8, new_text, "var x = 1") != null);
    try testing.expect(std.mem.indexOf(u8, new_text, "var x int") == null);

    // "let y = 2.5" should stay as "let y = 2.5", not become "let y float = 2.5"
    try testing.expect(std.mem.indexOf(u8, new_text, "let y = 2.5") != null);
    try testing.expect(std.mem.indexOf(u8, new_text, "let y float") == null);
}

test "formatting preserves comments" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

    const source =
        \\// This is a comment
        \\function main() returns int
        \\// Another comment
        \\var x = 1
        \\return x
        \\end 'main'
    ;
    try client.openDocument("file:///test.maxon", source);

    var result = try client.formatting("file:///test.maxon", 4, false);
    defer result.deinit();

    try testing.expect(result.new_text.len > 0);
    const new_text = result.new_text;

    // Comments should be preserved
    try testing.expect(std.mem.indexOf(u8, new_text, "// This is a comment") != null);
    try testing.expect(std.mem.indexOf(u8, new_text, "// Another comment") != null);
}

test "formatting preserves blank lines" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

    const source =
        \\function foo() returns int
        \\return 1
        \\end 'foo'
        \\
        \\function main() returns int
        \\return 0
        \\end 'main'
    ;
    try client.openDocument("file:///test.maxon", source);

    var result = try client.formatting("file:///test.maxon", 4, false);
    defer result.deinit();

    try testing.expect(result.new_text.len > 0);
    const new_text = result.new_text;

    // Blank line between functions should be preserved
    try testing.expect(std.mem.indexOf(u8, new_text, "end 'foo'\n\nfunction main") != null);
}

test "formatting formats multiple interfaces at top level" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

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
    try client.openDocument("file:///test.maxon", source);

    var result = try client.formatting("file:///test.maxon", 4, false);
    defer result.deinit();

    try testing.expect(result.new_text.len > 0);
    const new_text = result.new_text;

    // Verify interfaces are NOT indented (no tab before interface keyword)
    try testing.expect(std.mem.indexOf(u8, new_text, "\tinterface") == null);

    // Method signatures should be indented one level
    try testing.expect(std.mem.indexOf(u8, new_text, "\tfunction hash()") != null);
    try testing.expect(std.mem.indexOf(u8, new_text, "\tfunction equals(") != null);
}

test "documentSymbol returns symbols" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

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

    try client.openDocument("file:///test.maxon", source);

    var result = try client.documentSymbols("file:///test.maxon");
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
            try testing.expectEqual(@as(i64, 23), @intFromEnum(sym.kind)); // struct
        }
        if (std.mem.eql(u8, sym.name, "main")) {
            found_main = true;
            try testing.expectEqual(@as(i64, 12), @intFromEnum(sym.kind)); // function
        }
    }
    try testing.expect(found_point);
    try testing.expect(found_main);
}

test "foldingRange returns ranges" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

    const source =
        \\function main() returns int
        \\    if true 'check'
        \\        return 1
        \\    end 'check'
        \\    return 0
        \\end 'main'
    ;
    try client.openDocument("file:///test.maxon", source);

    var result = try client.foldingRange("file:///test.maxon");
    defer result.deinit();

    // Should have folding ranges for function (lines 0-5) and if block (lines 1-3)
    try testing.expectEqual(@as(usize, 2), result.ranges.len);

    // Verify we have ranges for both blocks
    var found_function = false;
    var found_if = false;
    for (result.ranges) |range| {
        if (range.startLine == 0 and range.endLine == 5) {
            found_function = true;
        }
        if (range.startLine == 1 and range.endLine == 3) {
            found_if = true;
        }
    }
    try testing.expect(found_function);
    try testing.expect(found_if);
}

test "linkedEditingRange returns ranges for block labels" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

    const source =
        \\function main() returns int
        \\for i in range(0, 10) 'loop'
        \\var x = i
        \\end 'loop'
        \\return 0
        \\end 'main'
    ;
    try client.openDocument("file:///test.maxon", source);

    // Request linked editing on the 'loop' label at line 1, character 23
    var result = try client.linkedEditingRange("file:///test.maxon", 1, 23);
    defer result.deinit();

    // Should return linked ranges for both 'loop' occurrences (start and end)
    try testing.expectEqual(@as(usize, 2), result.ranges.len);
}

test "linkedEditingRange returns ranges for function names" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

    const source =
        \\function myFunction() returns int
        \\return 42
        \\end 'myFunction'
    ;
    try client.openDocument("file:///test.maxon", source);

    // Request linked editing on the function name "myFunction" at line 0, character 12
    var result = try client.linkedEditingRange("file:///test.maxon", 0, 12);
    defer result.deinit();

    // Should return 2 ranges: function name and end label
    try testing.expectEqual(@as(usize, 2), result.ranges.len);

    // Check ranges point to the right locations
    // Range 0: function name at line 0, characters 9-19 (myFunction)
    // Range 1: end label at line 2, characters 5-15 (myFunction inside quotes)
    var has_declaration = false;
    var has_end_label = false;
    for (result.ranges) |range| {
        if (range.start.line == 0 and range.start.character == 9 and range.end.line == 0 and range.end.character == 19) {
            has_declaration = true;
        }
        if (range.start.line == 2 and range.start.character == 5 and range.end.line == 2 and range.end.character == 15) {
            has_end_label = true;
        }
    }
    try testing.expect(has_declaration);
    try testing.expect(has_end_label);
}

test "linkedEditingRange returns ranges for interface method names" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

    // Type with interface method implementation
    // Line 0: interface Countable
    // Line 1: function count() returns int
    // Line 2: end 'Countable'
    // Line 3: (empty)
    // Line 4: type MyStruct is Countable
    // Line 5:     var value int
    // Line 6: (empty)
    // Line 7:     function Countable.count() returns int
    // Line 8:         return value
    // Line 9:     end 'count'
    // Line 10: end 'MyStruct'
    const source =
        \\interface Countable
        \\function count() returns int
        \\end 'Countable'
        \\
        \\type MyStruct is Countable
        \\    var value int
        \\
        \\    function Countable.count() returns int
        \\        return value
        \\    end 'count'
        \\end 'MyStruct'
    ;
    try client.openDocument("file:///test.maxon", source);

    // Request linked editing on the method name "count" at line 7, character 24
    // "    function Countable.count() returns int"
    //                        ^^^^^
    // Position: "    function Countable." = 23 chars, so "count" starts at 23
    var result = try client.linkedEditingRange("file:///test.maxon", 7, 24);
    defer result.deinit();

    // Should return 2 ranges: method name and end label
    try testing.expectEqual(@as(usize, 2), result.ranges.len);

    // Check ranges point to the right locations
    // Range 0: method name at line 7, characters 23-28 (count)
    // Range 1: end label at line 9, characters 9-14 (count inside quotes)
    // "    end 'count'"
    //          ^^^^^
    // Position: "    end '" = 9 chars, so "count" is at 9-14
    var has_method_name = false;
    var has_end_label = false;
    for (result.ranges) |range| {
        if (range.start.line == 7 and range.start.character == 23 and range.end.line == 7 and range.end.character == 28) {
            has_method_name = true;
        }
        if (range.start.line == 9 and range.start.character == 9 and range.end.line == 9 and range.end.character == 14) {
            has_end_label = true;
        }
    }
    try testing.expect(has_method_name);
    try testing.expect(has_end_label);
}

test "rename block identifier updates all occurrences" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

    const source =
        \\function test() returns int
        \\    if true 'myBlock'
        \\        return 1
        \\    end 'myBlock'
        \\    return 0
        \\end 'test'
    ;

    try client.openDocument("file:///test.maxon", source);

    // Request rename on 'myBlock' (line 1, col 14)
    var result = try client.rename("file:///test.maxon", 1, 14, "newBlock");
    defer result.deinit();

    // Should have edits for both occurrences
    try testing.expect(result.edits.len > 0);
    try testing.expectEqual(@as(usize, 2), result.edits.len);
}

test "rename function updates function name and end label" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

    const source =
        \\function oldName() returns int
        \\    return 42
        \\end 'oldName'
    ;

    try client.openDocument("file:///test.maxon", source);

    // Request rename on 'oldName' (line 0, col 12)
    var result = try client.rename("file:///test.maxon", 0, 12, "newName");
    defer result.deinit();

    // Should have edits for function name and end label
    try testing.expect(result.edits.len > 0);
    try testing.expectEqual(@as(usize, 2), result.edits.len);
}

test "code action offers fix for unused variable" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

    const source =
        \\function test() returns int
        \\    var unused = 42
        \\    return 0
        \\end 'test'
    ;

    try client.openDocument("file:///test.maxon", source);

    // Request code actions on the unused variable line
    var result = try client.codeAction("file:///test.maxon", 1, 4, 1, 19);
    defer result.deinit();

    // Should have at least one code action (e.g., remove unused variable)
    try testing.expect(result.actions.len > 0);
}

test "semantic tokens returns token data" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

    const source =
        \\function test() returns int
        \\    var x = 42
        \\    return x
        \\end 'test'
    ;

    try client.openDocument("file:///test.maxon", source);

    var result = try client.semanticTokensFull("file:///test.maxon");
    defer result.deinit();

    // Should have semantic token data
    try testing.expect(result.data.len > 0);
    try testing.expect(result.data.len > 0);
}

test "formatting indents while loop body correctly" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

    // This test reproduces the issue where while loop body was being
    // indented with double the expected tabs (4 instead of 2)
    const source =
        \\function log(x float) returns float
        \\var k = 1
        \\while k < 20 'series'
        \\term = term * z_squared
        \\k = k + 1
        \\end 'series'
        \\return 0.0
        \\end 'log'
    ;
    try client.openDocument("file:///test.maxon", source);

    var result = try client.formatting("file:///test.maxon", 4, false);
    defer result.deinit();

    try testing.expect(result.new_text.len > 0);
    const new_text = result.new_text;

    // The while statement should be at 1 tab (inside function)
    try testing.expect(std.mem.indexOf(u8, new_text, "\twhile k < 20 'series'") != null);

    // Code inside while should be at 2 tabs (inside function + inside while)
    // NOT 4 tabs which was the bug
    try testing.expect(std.mem.indexOf(u8, new_text, "\t\tterm = term * z_squared") != null);
    try testing.expect(std.mem.indexOf(u8, new_text, "\t\tk = k + 1") != null);

    // Verify it's NOT using 3 or 4 tabs (the bug)
    try testing.expect(std.mem.indexOf(u8, new_text, "\t\t\tterm") == null);
    try testing.expect(std.mem.indexOf(u8, new_text, "\t\t\t\tterm") == null);

    // The end 'series' should be at 1 tab
    try testing.expect(std.mem.indexOf(u8, new_text, "\tend 'series'") != null);
}

test "hover shows variable type from intrinsic function call" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

    // Test: variable assigned from an intrinsic function call
    // This tests the case from Directory.maxon where filename = __find_filename(handle)
    const source =
        \\function test()
        \\    var handle = __find_first_file("test".cstr())
        \\    var filename = __find_filename(handle)
        \\    var name = String.fromCString(filename)
        \\end 'test'
    ;

    try client.openDocument("file:///test.maxon", source);

    // Request hover on 'filename' at usage site (line 3, col 34)
    // Line 3: "    var name = String.fromCString(filename)"
    //          0         1         2         3
    //          0123456789012345678901234567890123456789
    var result = try client.hover("file:///test.maxon", 3, 34);
    defer result.deinit();

    // The hover should show the variable type
    try testing.expect(result.contents.len > 0);
    const content = result.contents;
    try testing.expect(std.mem.indexOf(u8, content, "filename") != null);
    try testing.expect(std.mem.indexOf(u8, content, "cstring") != null);
}

test "hover shows static method signature" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

    // Test: hovering over a static method call like String.fromCString
    const source =
        \\function test()
        \\    var cs = "hello".cstr()
        \\    var name = String.fromCString(cs)
        \\end 'test'
    ;

    try client.openDocument("file:///test.maxon", source);

    // Request hover on 'fromCString' at line 2
    // Line 2: "    var name = String.fromCString(cs)"
    //          0         1         2
    //          0123456789012345678901234567
    var result = try client.hover("file:///test.maxon", 2, 22);
    defer result.deinit();

    // The hover should show the static method signature
    try testing.expect(result.contents.len > 0);
    const content = result.contents;
    try testing.expect(std.mem.indexOf(u8, content, "fromCString") != null);
}

test "formatting indents match expression cases correctly" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

    // Match expression with no indentation - formatter should add proper indentation
    const source =
        \\function dispatch(command String) returns int
        \\return match command 'dispatch'
        \\"compile" gives 1
        \\"run" gives 2
        \\default gives 0
        \\end 'dispatch'
        \\end 'dispatch'
    ;
    try client.openDocument("file:///test.maxon", source);

    var result = try client.formatting("file:///test.maxon", 4, false);
    defer result.deinit();

    // Verify we got formatted text
    try testing.expect(result.new_text.len > 0);
    const new_text = result.new_text;

    // Check that 'return match' is indented one level (inside function)
    try testing.expect(std.mem.indexOf(u8, new_text, "\treturn match") != null);

    // Check that match cases are indented two levels (inside function + inside match)
    try testing.expect(std.mem.indexOf(u8, new_text, "\t\t\"compile\" gives") != null);
    try testing.expect(std.mem.indexOf(u8, new_text, "\t\t\"run\" gives") != null);
    try testing.expect(std.mem.indexOf(u8, new_text, "\t\tdefault gives") != null);

    // Check that end 'dispatch' for match has one tab (back to function level)
    try testing.expect(std.mem.indexOf(u8, new_text, "\tend 'dispatch'") != null);
}

test "formatting indents try-otherwise block correctly" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

    // try-otherwise block with no indentation - formatter should add proper indentation
    const source =
        \\function readFile(path String) returns int
        \\let content = try File.readText(path) otherwise 'noFile'
        \\print("Error reading file\n")
        \\return 1
        \\end 'noFile'
        \\return 0
        \\end 'readFile'
    ;
    try client.openDocument("file:///test.maxon", source);

    var result = try client.formatting("file:///test.maxon", 4, false);
    defer result.deinit();

    // Verify we got formatted text
    try testing.expect(result.new_text.len > 0);
    const new_text = result.new_text;

    // Check that 'let content' is indented one level (inside function)
    try testing.expect(std.mem.indexOf(u8, new_text, "\tlet content = try") != null);

    // Check that statements inside otherwise block are indented two levels
    try testing.expect(std.mem.indexOf(u8, new_text, "\t\tprint(\"Error") != null);
    try testing.expect(std.mem.indexOf(u8, new_text, "\t\treturn 1") != null);

    // Check that end 'noFile' has one tab (back to function level, same as let)
    try testing.expect(std.mem.indexOf(u8, new_text, "\tend 'noFile'") != null);

    // Check that return 0 has one tab (inside function)
    try testing.expect(std.mem.indexOf(u8, new_text, "\treturn 0") != null);
}

test "formatting indents multiline map literal correctly" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

    // Multiline map literal with no indentation - formatter should add proper indentation
    const source =
        \\function getConfig()
        \\var config = [
        \\"width": 100,
        \\"height": 200
        \\]
        \\return config
        \\end 'getConfig'
    ;
    try client.openDocument("file:///test.maxon", source);

    var result = try client.formatting("file:///test.maxon", 4, false);
    defer result.deinit();

    // Verify we got formatted text
    try testing.expect(result.new_text.len > 0);
    const new_text = result.new_text;

    // Check that 'var config = [' is indented one level (inside function)
    try testing.expect(std.mem.indexOf(u8, new_text, "\tvar config = [") != null);

    // Check that map entries are indented two levels (inside function + inside map literal)
    try testing.expect(std.mem.indexOf(u8, new_text, "\t\t\"width\":") != null);
    try testing.expect(std.mem.indexOf(u8, new_text, "\t\t\"height\":") != null);

    // Check that closing ] has one tab (back to function level)
    try testing.expect(std.mem.indexOf(u8, new_text, "\t]") != null);
}

test "formatting indents top-level multiline map literal correctly" {
    var client = try TestClient.init(test_allocator);
    defer client.deinit();
    _ = try client.initialize();

    // Top-level map literal with no indentation - formatter should add proper indentation
    const source =
        \\let config = [
        \\"width": 100,
        \\"height": 200
        \\]
    ;
    try client.openDocument("file:///test.maxon", source);

    var result = try client.formatting("file:///test.maxon", 4, false);
    defer result.deinit();

    // Verify we got formatted text
    try testing.expect(result.new_text.len > 0);
    const new_text = result.new_text;

    // Check that 'let config = [' is at top level (no indentation)
    try testing.expect(std.mem.startsWith(u8, new_text, "let config = ["));

    // Check that map entries are indented one level (inside map literal)
    try testing.expect(std.mem.indexOf(u8, new_text, "\t\"width\":") != null);
    try testing.expect(std.mem.indexOf(u8, new_text, "\t\"height\":") != null);

    // Check that closing ] is at top level (no indentation)
    try testing.expect(std.mem.indexOf(u8, new_text, "\n]") != null);
}
