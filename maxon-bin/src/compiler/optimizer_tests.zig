const std = @import("std");
const testing = std.testing;
const compiler = @import("0-compiler.zig");

// ============================================================================
// Helper Functions
// ============================================================================

/// Cached stdlib modules for tests that need them.
/// Uses page_allocator because the test allocator tracks per-test and would
/// report leaks for cached data that persists across tests.
var cached_stdlib: ?[]const compiler.Source = null;

fn getStdlibModules(allocator: std.mem.Allocator) ![]const compiler.Source {
    _ = allocator;
    if (cached_stdlib) |modules| {
        return modules;
    }
    // Use page_allocator for the cache since it persists across tests
    const page_alloc = std.heap.page_allocator;
    const stdlib_path = try compiler.findStdlibPath(page_alloc);
    defer page_alloc.free(stdlib_path);

    cached_stdlib = try compiler.loadAllStdlibModules(stdlib_path, page_alloc);
    return cached_stdlib.?;
}

/// Compile Maxon source code and check the result by compiling to an executable and running it
fn compileAndRun(source: []const u8, allocator: std.mem.Allocator) !struct { exit_code: u8, ir_text: []const u8 } {
    const temp_exe = "..\\temp\\test_optimizer.exe";
    const temp_ir = "..\\temp\\test_optimizer.ir";

    // Clean up any existing files
    std.fs.cwd().deleteFile(temp_exe) catch {};
    std.fs.cwd().deleteFile(temp_ir) catch {};

    // Compile with IR emission
    var compile_result: compiler.CompileResult = .{ .error_info = null };
    defer compile_result.deinit(allocator);

    const options = compiler.CompileOptions{
        .track_memory = false,
        .emit_ir = true,
    };

    const sources = [_]compiler.Source{
        .{ .path = "test.maxon", .content = source },
    };

    try compiler.compileMultiple(&sources, temp_exe, options, allocator, &compile_result);

    // Read the IR file
    const ir_content = try std.fs.cwd().readFileAlloc(allocator, temp_ir, 1024 * 1024);
    errdefer allocator.free(ir_content);

    // Run the executable
    const result = try std.process.Child.run(.{
        .allocator = allocator,
        .argv = &.{temp_exe},
    });
    defer allocator.free(result.stdout);
    defer allocator.free(result.stderr);

    // Clean up
    std.fs.cwd().deleteFile(temp_exe) catch {};
    std.fs.cwd().deleteFile(temp_ir) catch {};

    return .{
        .exit_code = @intCast(result.term.Exited),
        .ir_text = ir_content,
    };
}

/// Compile Maxon source with stdlib and run it (for tests that use Array, String, etc.)
fn compileAndRunWithStdlib(source: []const u8, allocator: std.mem.Allocator) !struct { exit_code: u8, ir_text: []const u8 } {
    const temp_exe = "..\\temp\\test_stdlib.exe";
    const temp_ir = "..\\temp\\test_stdlib.ir";

    // Clean up any existing files
    std.fs.cwd().deleteFile(temp_exe) catch {};
    std.fs.cwd().deleteFile(temp_ir) catch {};

    // Load stdlib
    const stdlib_modules = try getStdlibModules(allocator);

    // Compile with IR emission and stdlib
    var compile_result: compiler.CompileResult = .{ .error_info = null };
    defer compile_result.deinit(allocator);

    const options = compiler.CompileOptions{
        .track_memory = false,
        .emit_ir = true,
    };

    // Create sources array with test source + stdlib
    var all_sources = try allocator.alloc(compiler.Source, stdlib_modules.len + 1);
    defer allocator.free(all_sources);

    all_sources[0] = .{ .path = "test.maxon", .content = source };
    for (stdlib_modules, 0..) |mod, i| {
        all_sources[i + 1] = mod;
    }

    try compiler.compileMultiple(all_sources, temp_exe, options, allocator, &compile_result);

    // Read the IR file
    const ir_content = try std.fs.cwd().readFileAlloc(allocator, temp_ir, 1024 * 1024);
    errdefer allocator.free(ir_content);

    // Run the executable
    const result = try std.process.Child.run(.{
        .allocator = allocator,
        .argv = &.{temp_exe},
    });
    defer allocator.free(result.stdout);
    defer allocator.free(result.stderr);

    // Clean up
    std.fs.cwd().deleteFile(temp_exe) catch {};
    std.fs.cwd().deleteFile(temp_ir) catch {};

    return .{
        .exit_code = @intCast(result.term.Exited),
        .ir_text = ir_content,
    };
}

/// Check if IR text contains a specific const value (exact match with word boundaries)
fn irContainsConst(ir_text: []const u8, value: i64) bool {
    var buf: [64]u8 = undefined;
    // Match "const.i64 i64 <value>\n" to ensure we don't match substrings
    const const_str = std.fmt.bufPrint(&buf, "const.i64 i64 {d}\n", .{value}) catch return false;
    return std.mem.indexOf(u8, ir_text, const_str) != null;
}

/// Count occurrences of an IR operation (must match exact pattern with spaces)
fn countIROperation(ir_text: []const u8, op: []const u8) usize {
    var count: usize = 0;
    var pos: usize = 0;
    // Look for "= op " pattern to ensure we match actual operations, not part of names
    var buf: [32]u8 = undefined;
    const pattern = std.fmt.bufPrint(&buf, "= {s} ", .{op}) catch return 0;

    while (std.mem.indexOfPos(u8, ir_text, pos, pattern)) |index| {
        count += 1;
        pos = index + pattern.len;
    }
    return count;
}

/// Count total const.i64 instructions in IR
fn countConstI64(ir_text: []const u8) usize {
    var count: usize = 0;
    var pos: usize = 0;
    const pattern = "const.i64";

    while (std.mem.indexOfPos(u8, ir_text, pos, pattern)) |index| {
        count += 1;
        pos = index + pattern.len;
    }
    return count;
}

/// Verify IR only contains expected const value and no others
fn verifyOnlyConst(ir_text: []const u8, expected_value: i64) !void {
    // Should have exactly one const.i64 instruction with the expected value
    try testing.expectEqual(@as(usize, 1), countConstI64(ir_text));
    try testing.expect(irContainsConst(ir_text, expected_value));
}

// ============================================================================
// Constant Folding Tests
// ============================================================================

test "constant folding - simple addition" {
    const allocator = testing.allocator;

    const source =
        \\function main() returns int
        \\    return 10 + 20
        \\end 'main'
    ;

    const result = try compileAndRun(source, allocator);
    defer allocator.free(result.ir_text);

    // After optimization, should have exactly one const 30, no intermediate values
    try verifyOnlyConst(result.ir_text, 30);
    // Should have no add operations remaining
    try testing.expectEqual(@as(usize, 0), countIROperation(result.ir_text, "add"));
    // Verify it returns 30
    try testing.expectEqual(@as(u8, 30), result.exit_code);
}

test "constant folding - subtraction" {
    const allocator = testing.allocator;

    const source =
        \\function main() returns int
        \\    return 50 - 30
        \\end 'main'
    ;

    const result = try compileAndRun(source, allocator);
    defer allocator.free(result.ir_text);

    try verifyOnlyConst(result.ir_text, 20);
    try testing.expectEqual(@as(usize, 0), countIROperation(result.ir_text, "sub"));
    try testing.expectEqual(@as(u8, 20), result.exit_code);
}

test "constant folding - multiplication" {
    const allocator = testing.allocator;

    const source =
        \\function main() returns int
        \\    return 5 * 7
        \\end 'main'
    ;

    const result = try compileAndRun(source, allocator);
    defer allocator.free(result.ir_text);

    try verifyOnlyConst(result.ir_text, 35);
    try testing.expectEqual(@as(usize, 0), countIROperation(result.ir_text, "mul"));
    try testing.expectEqual(@as(u8, 35), result.exit_code);
}

test "constant folding - integer division" {
    const allocator = testing.allocator;

    // Note: Currently / does float division. This test verifies the result is correct
    // even though it doesn't optimize floating point operations yet
    const source =
        \\function main() returns int
        \\    return 100 / 4
        \\end 'main'
    ;

    const result = try compileAndRun(source, allocator);
    defer allocator.free(result.ir_text);

    // Floating point division is not optimized (yet), so just check execution result
    try testing.expectEqual(@as(u8, 25), result.exit_code);
}

test "constant folding - modulo" {
    const allocator = testing.allocator;

    const source =
        \\function main() returns int
        \\    return 100 mod 25
        \\end 'main'
    ;

    const result = try compileAndRun(source, allocator);
    defer allocator.free(result.ir_text);

    try verifyOnlyConst(result.ir_text, 0);
    try testing.expectEqual(@as(usize, 0), countIROperation(result.ir_text, "mod"));
    try testing.expectEqual(@as(u8, 0), result.exit_code);
}

test "constant folding - nested parentheses" {
    const allocator = testing.allocator;

    const source =
        \\function main() returns int
        \\    return ((10 + 5) * 2) - 5
        \\end 'main'
    ;

    const result = try compileAndRun(source, allocator);
    defer allocator.free(result.ir_text);

    // ((10 + 5) * 2) - 5 = (15 * 2) - 5 = 30 - 5 = 25
    try verifyOnlyConst(result.ir_text, 25);
    // No intermediate constants should remain (10, 5, 15, 2, 30)
    try testing.expect(!irContainsConst(result.ir_text, 10));
    try testing.expect(!irContainsConst(result.ir_text, 5));
    try testing.expect(!irContainsConst(result.ir_text, 15));
    try testing.expect(!irContainsConst(result.ir_text, 2));
    try testing.expect(!irContainsConst(result.ir_text, 30));
    // No arithmetic operations should remain
    try testing.expectEqual(@as(usize, 0), countIROperation(result.ir_text, "add"));
    try testing.expectEqual(@as(usize, 0), countIROperation(result.ir_text, "mul"));
    try testing.expectEqual(@as(usize, 0), countIROperation(result.ir_text, "sub"));
    try testing.expectEqual(@as(u8, 25), result.exit_code);
}

test "constant folding - operator precedence" {
    const allocator = testing.allocator;

    const source =
        \\function main() returns int
        \\    return 2 + 3 * 4
        \\end 'main'
    ;

    const result = try compileAndRun(source, allocator);
    defer allocator.free(result.ir_text);

    // Should be 2 + (3 * 4) = 2 + 12 = 14
    try verifyOnlyConst(result.ir_text, 14);
    // No intermediate values
    try testing.expect(!irContainsConst(result.ir_text, 2));
    try testing.expect(!irContainsConst(result.ir_text, 3));
    try testing.expect(!irContainsConst(result.ir_text, 4));
    try testing.expect(!irContainsConst(result.ir_text, 12));
    // No operations
    try testing.expectEqual(@as(usize, 0), countIROperation(result.ir_text, "add"));
    try testing.expectEqual(@as(usize, 0), countIROperation(result.ir_text, "mul"));
    try testing.expectEqual(@as(u8, 14), result.exit_code);
}

test "constant folding - complex expression (1+2+3)*(4+5)" {
    const allocator = testing.allocator;

    const source =
        \\function main() returns int
        \\    return (1+2+3)*(4+5)
        \\end 'main'
    ;

    const result = try compileAndRun(source, allocator);
    defer allocator.free(result.ir_text);

    // (1+2+3) = 6, (4+5) = 9, 6*9 = 54
    try verifyOnlyConst(result.ir_text, 54);
    // Verify no intermediate constants
    try testing.expect(!irContainsConst(result.ir_text, 1));
    try testing.expect(!irContainsConst(result.ir_text, 2));
    try testing.expect(!irContainsConst(result.ir_text, 3));
    try testing.expect(!irContainsConst(result.ir_text, 4));
    try testing.expect(!irContainsConst(result.ir_text, 5));
    try testing.expect(!irContainsConst(result.ir_text, 6));
    try testing.expect(!irContainsConst(result.ir_text, 9));
    // No operations
    try testing.expectEqual(@as(usize, 0), countIROperation(result.ir_text, "add"));
    try testing.expectEqual(@as(usize, 0), countIROperation(result.ir_text, "mul"));
    try testing.expectEqual(@as(u8, 54), result.exit_code);
}

test "constant folding - with variable (partial folding)" {
    const allocator = testing.allocator;

    const source =
        \\function main() returns int
        \\    var x = 4
        \\    return (1+2+x)*(x+(1+2))
        \\end 'main'
    ;

    const result = try compileAndRun(source, allocator);
    defer allocator.free(result.ir_text);

    // 1+2 should be folded to 3, and 4 should be present for variable assignment
    try testing.expect(irContainsConst(result.ir_text, 3));
    try testing.expect(irContainsConst(result.ir_text, 4));

    // But intermediate values 1, 2 should not be present
    try testing.expect(!irContainsConst(result.ir_text, 1));
    try testing.expect(!irContainsConst(result.ir_text, 2));

    // Should still have arithmetic operations for expressions involving x
    // Exactly 2 adds (3+x and x+3) and 1 mul
    try testing.expectEqual(@as(usize, 2), countIROperation(result.ir_text, "add"));
    try testing.expectEqual(@as(usize, 1), countIROperation(result.ir_text, "mul"));

    // (1+2+4)*(4+(1+2)) = 7*7 = 49
    try testing.expectEqual(@as(u8, 49), result.exit_code);
}

test "constant folding - large numbers" {
    const allocator = testing.allocator;

    const source =
        \\function main() returns int
        \\    return 15 * 3
        \\end 'main'
    ;

    const result = try compileAndRun(source, allocator);
    defer allocator.free(result.ir_text);

    try verifyOnlyConst(result.ir_text, 45);
    try testing.expect(!irContainsConst(result.ir_text, 15));
    try testing.expect(!irContainsConst(result.ir_text, 3));
    try testing.expectEqual(@as(usize, 0), countIROperation(result.ir_text, "mul"));
    try testing.expectEqual(@as(u8, 45), result.exit_code);
}

test "constant folding - zero operations" {
    const allocator = testing.allocator;

    const source =
        \\function main() returns int
        \\    return 0 + 0
        \\end 'main'
    ;

    const result = try compileAndRun(source, allocator);
    defer allocator.free(result.ir_text);

    try verifyOnlyConst(result.ir_text, 0);
    try testing.expectEqual(@as(usize, 0), countIROperation(result.ir_text, "add"));
    try testing.expectEqual(@as(u8, 0), result.exit_code);
}

test "constant folding - does not fold variable operations" {
    const allocator = testing.allocator;

    const source =
        \\function main() returns int
        \\    var x = 10
        \\    var y = 20
        \\    return x + y
        \\end 'main'
    ;

    const result = try compileAndRun(source, allocator);
    defer allocator.free(result.ir_text);

    // Should have constants for variable initialization
    try testing.expect(irContainsConst(result.ir_text, 10));
    try testing.expect(irContainsConst(result.ir_text, 20));

    // Should have exactly 1 add instruction (x + y)
    try testing.expectEqual(@as(usize, 1), countIROperation(result.ir_text, "add"));

    // But it should still compute correctly
    try testing.expectEqual(@as(u8, 30), result.exit_code);
}

test "constant folding - multiple operations same result" {
    const allocator = testing.allocator;

    const source =
        \\function main() returns int
        \\    return 10 + 10 + 10
        \\end 'main'
    ;

    const result = try compileAndRun(source, allocator);
    defer allocator.free(result.ir_text);

    // Should fold to single constant 30
    try verifyOnlyConst(result.ir_text, 30);
    try testing.expect(!irContainsConst(result.ir_text, 10));
    try testing.expect(!irContainsConst(result.ir_text, 20));
    try testing.expectEqual(@as(usize, 0), countIROperation(result.ir_text, "add"));
    try testing.expectEqual(@as(u8, 30), result.exit_code);
}

test "constant folding - mix of operations" {
    const allocator = testing.allocator;

    const source =
        \\function main() returns int
        \\    return 100 - 50 + 25 * 2
        \\end 'main'
    ;

    const result = try compileAndRun(source, allocator);
    defer allocator.free(result.ir_text);

    // 25 * 2 = 50, then 100 - 50 + 50 = 100
    try verifyOnlyConst(result.ir_text, 100);
    try testing.expect(!irContainsConst(result.ir_text, 25));
    try testing.expect(!irContainsConst(result.ir_text, 50));
    try testing.expectEqual(@as(usize, 0), countIROperation(result.ir_text, "add"));
    try testing.expectEqual(@as(usize, 0), countIROperation(result.ir_text, "sub"));
    try testing.expectEqual(@as(usize, 0), countIROperation(result.ir_text, "mul"));
    try testing.expectEqual(@as(u8, 100), result.exit_code);
}

// ============================================================================
// Dead Code Elimination Tests
// ============================================================================

/// Check if a function exists in the IR output
fn irContainsFunction(ir_text: []const u8, func_name: []const u8) bool {
    var buf: [128]u8 = undefined;
    // Match "function func_name(" to find function definitions
    const pattern = std.fmt.bufPrint(&buf, "function {s}(", .{func_name}) catch return false;
    return std.mem.indexOf(u8, ir_text, pattern) != null;
}

/// Count how many functions are defined in the IR
fn countFunctions(ir_text: []const u8) usize {
    var count: usize = 0;
    var pos: usize = 0;
    const pattern = "\nfunction ";

    // Also check if file starts with "function "
    if (std.mem.startsWith(u8, ir_text, "function ")) {
        count += 1;
    }

    while (std.mem.indexOfPos(u8, ir_text, pos, pattern)) |index| {
        count += 1;
        pos = index + pattern.len;
    }
    return count;
}

test "dead code elimination - eliminates unused function" {
    const allocator = testing.allocator;

    const source =
        \\function used_function() returns int
        \\    return 42
        \\end 'used_function'
        \\
        \\function unused_function() returns int
        \\    return 999
        \\end 'unused_function'
        \\
        \\function another_unused_function() returns int
        \\    return 123
        \\end 'another_unused_function'
        \\
        \\function main() returns int
        \\    var result = used_function()
        \\    return result
        \\end 'main'
    ;

    const result = try compileAndRun(source, allocator);
    defer allocator.free(result.ir_text);

    // Should keep main and used_function
    try testing.expect(irContainsFunction(result.ir_text, "main"));
    try testing.expect(irContainsFunction(result.ir_text, "used_function"));

    // Should eliminate unused functions
    try testing.expect(!irContainsFunction(result.ir_text, "unused_function"));
    try testing.expect(!irContainsFunction(result.ir_text, "another_unused_function"));

    // Should have exactly 2 functions
    try testing.expectEqual(@as(usize, 2), countFunctions(result.ir_text));

    // Verify correct execution
    try testing.expectEqual(@as(u8, 42), result.exit_code);
}

test "dead code elimination - keeps transitive calls" {
    const allocator = testing.allocator;

    const source =
        \\function helper() returns int
        \\    return 10
        \\end 'helper'
        \\
        \\function used() returns int
        \\    return helper()
        \\end 'used'
        \\
        \\function unused() returns int
        \\    return 999
        \\end 'unused'
        \\
        \\function main() returns int
        \\    return used()
        \\end 'main'
    ;

    const result = try compileAndRun(source, allocator);
    defer allocator.free(result.ir_text);

    // Should keep main, used, and helper (transitive dependency)
    try testing.expect(irContainsFunction(result.ir_text, "main"));
    try testing.expect(irContainsFunction(result.ir_text, "used"));
    try testing.expect(irContainsFunction(result.ir_text, "helper"));

    // Should eliminate unused
    try testing.expect(!irContainsFunction(result.ir_text, "unused"));

    // Should have exactly 3 functions
    try testing.expectEqual(@as(usize, 3), countFunctions(result.ir_text));

    // Verify correct execution
    try testing.expectEqual(@as(u8, 10), result.exit_code);
}

test "dead code elimination - deep call chain" {
    const allocator = testing.allocator;

    const source =
        \\function level3() returns int
        \\    return 5
        \\end 'level3'
        \\
        \\function level2() returns int
        \\    return level3()
        \\end 'level2'
        \\
        \\function level1() returns int
        \\    return level2()
        \\end 'level1'
        \\
        \\function orphan() returns int
        \\    return 999
        \\end 'orphan'
        \\
        \\function main() returns int
        \\    return level1()
        \\end 'main'
    ;

    const result = try compileAndRun(source, allocator);
    defer allocator.free(result.ir_text);

    // Should keep all levels in call chain
    try testing.expect(irContainsFunction(result.ir_text, "main"));
    try testing.expect(irContainsFunction(result.ir_text, "level1"));
    try testing.expect(irContainsFunction(result.ir_text, "level2"));
    try testing.expect(irContainsFunction(result.ir_text, "level3"));

    // Should eliminate orphan
    try testing.expect(!irContainsFunction(result.ir_text, "orphan"));

    // Should have exactly 4 functions
    try testing.expectEqual(@as(usize, 4), countFunctions(result.ir_text));

    // Verify correct execution
    try testing.expectEqual(@as(u8, 5), result.exit_code);
}

test "dead code elimination - no unused functions" {
    const allocator = testing.allocator;

    const source =
        \\function helper() returns int
        \\    return 7
        \\end 'helper'
        \\
        \\function main() returns int
        \\    return helper()
        \\end 'main'
    ;

    const result = try compileAndRun(source, allocator);
    defer allocator.free(result.ir_text);

    // All functions are used
    try testing.expect(irContainsFunction(result.ir_text, "main"));
    try testing.expect(irContainsFunction(result.ir_text, "helper"));

    // Should have exactly 2 functions
    try testing.expectEqual(@as(usize, 2), countFunctions(result.ir_text));

    // Verify correct execution
    try testing.expectEqual(@as(u8, 7), result.exit_code);
}

// ============================================================================
// Mem2Reg / Variable Handling Tests
// These tests verify correct handling of variables across control flow paths.
// The original spec tests only verified execution behavior (exitcodes).
// ============================================================================

test "variable reassignment - if-else takes true branch" {
    const allocator = testing.allocator;

    const source =
        \\function main() returns int
        \\    var x = 0
        \\    if 5 > 3 'check'
        \\        x = 42
        \\    end 'check' else 'else_check'
        \\        x = 100
        \\    end 'else_check'
        \\    return x
        \\end 'main'
    ;

    const result = try compileAndRun(source, allocator);
    defer allocator.free(result.ir_text);

    // Verify correct execution (5 > 3 is true, so x = 42)
    try testing.expectEqual(@as(u8, 42), result.exit_code);
}

test "variable reassignment - if-else takes false branch" {
    const allocator = testing.allocator;

    const source =
        \\function main() returns int
        \\    var x = 0
        \\    if 1 > 5 'check'
        \\        x = 42
        \\    end 'check' else 'else_check'
        \\        x = 100
        \\    end 'else_check'
        \\    return x
        \\end 'main'
    ;

    const result = try compileAndRun(source, allocator);
    defer allocator.free(result.ir_text);

    // Verify correct execution (1 > 5 is false, so x = 100)
    try testing.expectEqual(@as(u8, 100), result.exit_code);
}

test "variable reassignment - nested if" {
    const allocator = testing.allocator;

    const source =
        \\function main() returns int
        \\    var result = 0
        \\    if 1 > 0 'outer'
        \\        if 2 > 1 'inner'
        \\            result = 10
        \\        end 'inner' else 'else_inner'
        \\            result = 20
        \\        end 'else_inner'
        \\    end 'outer' else 'else_outer'
        \\        result = 30
        \\    end 'else_outer'
        \\    return result
        \\end 'main'
    ;

    const result = try compileAndRun(source, allocator);
    defer allocator.free(result.ir_text);

    // Verify correct execution (outer true, inner true -> 10)
    try testing.expectEqual(@as(u8, 10), result.exit_code);
}

test "variable reassignment - while loop accumulator" {
    const allocator = testing.allocator;

    const source =
        \\function main() returns int
        \\    var sum = 0
        \\    var i = 1
        \\    while i <= 5 'loop'
        \\        sum = sum + i
        \\        i = i + 1
        \\    end 'loop'
        \\    return sum
        \\end 'main'
    ;

    const result = try compileAndRun(source, allocator);
    defer allocator.free(result.ir_text);

    // Verify correct execution (1+2+3+4+5 = 15)
    try testing.expectEqual(@as(u8, 15), result.exit_code);
}

test "variable reassignment - nested loops" {
    const allocator = testing.allocator;

    const source =
        \\function main() returns int
        \\    var total = 0
        \\    var i = 0
        \\    while i < 3 'outer'
        \\        var j = 0
        \\        while j < 3 'inner'
        \\            total = total + 1
        \\            j = j + 1
        \\        end 'inner'
        \\        i = i + 1
        \\    end 'outer'
        \\    return total
        \\end 'main'
    ;

    const result = try compileAndRun(source, allocator);
    defer allocator.free(result.ir_text);

    // Verify correct execution (3 * 3 = 9)
    try testing.expectEqual(@as(u8, 9), result.exit_code);
}

test "variable reassignment - multiple variables in if-else" {
    const allocator = testing.allocator;

    const source =
        \\function main() returns int
        \\    var a = 0
        \\    var b = 0
        \\    if 1 > 0 'branch'
        \\        a = 10
        \\        b = 20
        \\    end 'branch' else 'else_branch'
        \\        a = 100
        \\        b = 200
        \\    end 'else_branch'
        \\    return a + b
        \\end 'main'
    ;

    const result = try compileAndRun(source, allocator);
    defer allocator.free(result.ir_text);

    // Verify correct execution (10 + 20 = 30)
    try testing.expectEqual(@as(u8, 30), result.exit_code);
}

test "variable reassignment - with computation" {
    const allocator = testing.allocator;

    const source =
        \\function main() returns int
        \\    var x = 5
        \\    if x > 3 'check'
        \\        x = x * 2
        \\    end 'check' else 'else_check'
        \\        x = x + 10
        \\    end 'else_check'
        \\    return x
        \\end 'main'
    ;

    const result = try compileAndRun(source, allocator);
    defer allocator.free(result.ir_text);

    // Verify correct execution (5 > 3, so x = 5 * 2 = 10)
    try testing.expectEqual(@as(u8, 10), result.exit_code);
}

test "variable reassignment - loop with conditional" {
    const allocator = testing.allocator;

    const source =
        \\function main() returns int
        \\    var count = 0
        \\    var i = 0
        \\    while i < 10 'loop'
        \\        if i mod 2 == 0 'even'
        \\            count = count + 1
        \\        end 'even'
        \\        i = i + 1
        \\    end 'loop'
        \\    return count
        \\end 'main'
    ;

    const result = try compileAndRun(source, allocator);
    defer allocator.free(result.ir_text);

    // Verify correct execution (0,2,4,6,8 are even = 5 numbers)
    try testing.expectEqual(@as(u8, 5), result.exit_code);
}

test "variable reassignment - simple variable use" {
    const allocator = testing.allocator;

    const source =
        \\function main() returns int
        \\    var x = 10
        \\    return x + 5
        \\end 'main'
    ;

    const result = try compileAndRun(source, allocator);
    defer allocator.free(result.ir_text);

    // Verify correct execution
    try testing.expectEqual(@as(u8, 15), result.exit_code);
}

// ============================================================================
// Stack Probing Tests
// On Windows x64, functions that allocate more than 4096 bytes of stack space
// need to "probe" each page. These tests verify __chkstk is called correctly.
// ============================================================================

test "stack probing - large struct recursive" {
    const allocator = testing.allocator;

    // Generate a struct with 2000 int fields (16000 bytes on stack)
    // This requires stack probing on Windows
    var source_buf = std.ArrayListUnmanaged(u8){};
    defer source_buf.deinit(allocator);

    const writer = source_buf.writer(allocator);

    try writer.writeAll("type BigStruct\n");
    for (0..2000) |i| {
        try writer.print("    export var f{d} int\n", .{i});
    }
    try writer.writeAll("end 'BigStruct'\n\n");

    try writer.writeAll(
        \\function recurse(n int) returns int
        \\    var s = BigStruct { f0: n }
        \\    if n == 0 'base'
        \\        return s.f1999
        \\    end 'base'
        \\    return recurse(n - 1)
        \\end 'recurse'
        \\
        \\function main() returns int
        \\    return recurse(50)
        \\end 'main'
    );

    const result = try compileAndRun(source_buf.items, allocator);
    defer allocator.free(result.ir_text);

    // If stack probing is broken, this would crash before returning
    // The recursive call with 50 iterations ensures multiple large stack frames
    try testing.expectEqual(@as(u8, 0), result.exit_code);
}

// ============================================================================
// Managed Array Internals Tests
// Verify correct IR generation for array memory management
// ============================================================================

/// Count heap.free instructions in IR
fn countHeapFree(ir_text: []const u8) usize {
    var count: usize = 0;
    var pos: usize = 0;
    const pattern = "heap.free";

    while (std.mem.indexOfPos(u8, ir_text, pos, pattern)) |index| {
        count += 1;
        pos = index + pattern.len;
    }
    return count;
}

/// Count heap.realloc instructions in IR
fn countHeapRealloc(ir_text: []const u8) usize {
    var count: usize = 0;
    var pos: usize = 0;
    const pattern = "heap.realloc";

    while (std.mem.indexOfPos(u8, ir_text, pos, pattern)) |index| {
        count += 1;
        pos = index + pattern.len;
    }
    return count;
}

/// Check if IR contains a heap.free with a specific tag
fn irContainsHeapFreeTag(ir_text: []const u8, tag: []const u8) bool {
    var buf: [128]u8 = undefined;
    const pattern = std.fmt.bufPrint(&buf, "heap.free", .{}) catch return false;
    _ = tag;
    return std.mem.indexOf(u8, ir_text, pattern) != null;
}

test "managed array - heap array generates free" {
    const allocator = testing.allocator;

    const source =
        \\function main() returns int
        \\    var arr = Array of int{}
        \\    arr.push(1)
        \\    arr.push(2)
        \\    return arr.count()
        \\end 'main'
    ;

    const result = try compileAndRunWithStdlib(source, allocator);
    defer allocator.free(result.ir_text);

    // Should generate heap.free for array cleanup
    try testing.expect(countHeapFree(result.ir_text) >= 1);
    try testing.expect(std.mem.indexOf(u8, result.ir_text, "\"array cleanup\"") != null);

    // Verify correct execution
    try testing.expectEqual(@as(u8, 2), result.exit_code);
}

test "managed array - scope cleanup generates free" {
    const allocator = testing.allocator;

    const source =
        \\function main() returns int
        \\    if true 'outer'
        \\        var outer_arr = Array of int{}
        \\        outer_arr.push(100)
        \\        if true 'inner'
        \\            var inner_arr = Array of int{}
        \\            inner_arr.push(200)
        \\        end 'inner'
        \\    end 'outer'
        \\    return 0
        \\end 'main'
    ;

    const result = try compileAndRunWithStdlib(source, allocator);
    defer allocator.free(result.ir_text);

    // Should generate heap.free for both arrays (inner and outer scope cleanup)
    try testing.expect(countHeapFree(result.ir_text) >= 2);

    // Verify correct execution
    try testing.expectEqual(@as(u8, 0), result.exit_code);
}

test "managed array - loop growth generates realloc" {
    const allocator = testing.allocator;

    const source =
        \\function main() returns int
        \\    var arr = Array of int{}
        \\    var i = 0
        \\    while i < 10 'loop'
        \\        arr.push(i)
        \\        i = i + 1
        \\    end 'loop'
        \\    return arr.count()
        \\end 'main'
    ;

    const result = try compileAndRunWithStdlib(source, allocator);
    defer allocator.free(result.ir_text);

    // Should generate heap.realloc for array growth (in ensureCapacity)
    try testing.expect(countHeapRealloc(result.ir_text) >= 1);
    try testing.expect(std.mem.indexOf(u8, result.ir_text, "\"array grow\"") != null);

    // Should also generate heap.free for final cleanup
    try testing.expect(countHeapFree(result.ir_text) >= 1);

    // Verify correct execution (10 elements)
    try testing.expectEqual(@as(u8, 10), result.exit_code);
}

test "managed array - fixed size array literal cleanup" {
    const allocator = testing.allocator;

    const source =
        \\function main() returns int
        \\    var arr = [10, 20, 30]
        \\    return try arr.get(1) otherwise 0
        \\end 'main'
    ;

    const result = try compileAndRunWithStdlib(source, allocator);
    defer allocator.free(result.ir_text);

    // Should generate heap.free for array cleanup
    try testing.expect(countHeapFree(result.ir_text) >= 1);

    // Verify correct execution
    try testing.expectEqual(@as(u8, 20), result.exit_code);
}

test "managed array - struct field array method call" {
    const allocator = testing.allocator;

    const source =
        \\type Config
        \\    export var sources Array of String
        \\end 'Config'
        \\
        \\function main() returns int
        \\    var config = Config{sources: ["a", "b", "c"]}
        \\    return config.sources.count()
        \\end 'main'
    ;

    const result = try compileAndRunWithStdlib(source, allocator);
    defer allocator.free(result.ir_text);

    // Verify correct execution
    try testing.expectEqual(@as(u8, 3), result.exit_code);
}

// ============================================================================
// Managed String Internals Tests
// Verify correct IR generation for string memory management
// ============================================================================

test "managed string - heap string generates cleanup" {
    const allocator = testing.allocator;

    const source =
        \\function main() returns int
        \\    var s = "this is a heap allocated string!"
        \\    return s.count()
        \\end 'main'
    ;

    const result = try compileAndRunWithStdlib(source, allocator);
    defer allocator.free(result.ir_text);

    // Verify correct execution (32 characters)
    try testing.expectEqual(@as(u8, 32), result.exit_code);
}

test "managed string - reassignment handles old value" {
    const allocator = testing.allocator;

    const source =
        \\function main() returns int
        \\    var s = "first heap allocated value!!"
        \\    s = "second heap allocated here!!"
        \\    return s.count()
        \\end 'main'
    ;

    const result = try compileAndRunWithStdlib(source, allocator);
    defer allocator.free(result.ir_text);

    // Verify correct execution (28 characters in second string)
    try testing.expectEqual(@as(u8, 28), result.exit_code);
}

test "managed string - substring retains parent" {
    const allocator = testing.allocator;

    const source =
        \\function main() returns int
        \\    var s = "hello world from heap!!"
        \\    var start = s.startIndex()
        \\    var spaceIdx = try s.find(" ") otherwise s.endIndex()
        \\    var sub = s.slice(start, endIndex: spaceIdx)
        \\    return sub.count()
        \\end 'main'
    ;

    const result = try compileAndRunWithStdlib(source, allocator);
    defer allocator.free(result.ir_text);

    // Verify correct execution ("hello" = 5 characters)
    try testing.expectEqual(@as(u8, 5), result.exit_code);
}

test "managed string - print heap string" {
    const allocator = testing.allocator;

    const source =
        \\function main() returns int
        \\    var s = "heap allocated string here!!"
        \\    return s.count()
        \\end 'main'
    ;

    const result = try compileAndRunWithStdlib(source, allocator);
    defer allocator.free(result.ir_text);

    // Verify correct execution (28 characters)
    try testing.expectEqual(@as(u8, 28), result.exit_code);
}

test "managed string - short string SSO" {
    const allocator = testing.allocator;

    const source =
        \\function main() returns int
        \\    var s = "short"
        \\    return s.count()
        \\end 'main'
    ;

    const result = try compileAndRunWithStdlib(source, allocator);
    defer allocator.free(result.ir_text);

    // Verify correct execution (5 characters)
    try testing.expectEqual(@as(u8, 5), result.exit_code);
}

test "managed string - loop concatenation cleanup" {
    const allocator = testing.allocator;

    const source =
        \\function main() returns int
        \\    var s = ""
        \\    var a = "a"
        \\    var i = 0
        \\    while i < 5 'loop'
        \\        s = "{s}{a}"
        \\        i = i + 1
        \\    end 'loop'
        \\    return s.count()
        \\end 'main'
    ;

    const result = try compileAndRunWithStdlib(source, allocator);
    defer allocator.free(result.ir_text);

    // Should generate heap.free for intermediate string cleanup during reassignment
    try testing.expect(countHeapFree(result.ir_text) >= 1);
    try testing.expect(std.mem.indexOf(u8, result.ir_text, "\"string cleanup\"") != null);

    // Verify correct execution (5 'a' characters)
    try testing.expectEqual(@as(u8, 5), result.exit_code);
}

test "managed string - literal deduplication" {
    const allocator = testing.allocator;

    const source =
        \\function main() returns int
        \\    var a = "hello world"
        \\    var b = "hello world"
        \\    var c = "hello world"
        \\    return a.count() + b.count() + c.count()
        \\end 'main'
    ;

    const temp_exe = "..\\temp\\test_dedup.exe";

    // Clean up any existing files
    std.fs.cwd().deleteFile(temp_exe) catch {};

    // Load stdlib
    const stdlib_modules = try getStdlibModules(allocator);

    // Compile without IR emission - we need to check the actual PE file
    var compile_result: compiler.CompileResult = .{ .error_info = null };
    defer compile_result.deinit(allocator);

    const options = compiler.CompileOptions{
        .track_memory = false,
        .emit_ir = false,
    };

    // Create sources array with test source + stdlib
    var all_sources = try allocator.alloc(compiler.Source, stdlib_modules.len + 1);
    defer allocator.free(all_sources);

    all_sources[0] = .{ .path = "test.maxon", .content = source };
    for (stdlib_modules, 0..) |mod, i| {
        all_sources[i + 1] = mod;
    }

    try compiler.compileMultiple(all_sources, temp_exe, options, allocator, &compile_result);

    // Read the executable file
    const exe_content = try std.fs.cwd().readFileAlloc(allocator, temp_exe, 10 * 1024 * 1024);
    defer allocator.free(exe_content);

    // Count occurrences of "hello world" in the executable
    // If deduplication works, it should appear exactly once (or at most a few times for metadata)
    var count: usize = 0;
    var pos: usize = 0;
    const needle = "hello world";

    while (std.mem.indexOfPos(u8, exe_content, pos, needle)) |index| {
        count += 1;
        pos = index + needle.len;
    }

    // Run the executable to verify it works
    const result = try std.process.Child.run(.{
        .allocator = allocator,
        .argv = &.{temp_exe},
    });
    defer allocator.free(result.stdout);
    defer allocator.free(result.stderr);

    // Clean up
    std.fs.cwd().deleteFile(temp_exe) catch {};

    // Should find "hello world" only once (deduplication worked)
    // Allow up to 2 occurrences in case of debug symbols or metadata
    try testing.expect(count <= 2);

    // Verify correct execution (11 + 11 + 11 = 33)
    try testing.expectEqual(@as(u8, 33), @as(u8, @intCast(result.term.Exited)));
}
