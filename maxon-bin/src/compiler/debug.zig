const std = @import("std");

/// Debug logging configuration - disabled by default, enable with -v flag
pub var enabled: bool = false;

/// Log a debug message
pub fn log(comptime fmt: []const u8, args: anytype) void {
    if (enabled) {
        std.debug.print("[DEBUG] " ++ fmt ++ "\n", args);
    }
}

/// Log with a specific component tag
pub fn logComponent(comptime component: []const u8, comptime fmt: []const u8, args: anytype) void {
    if (enabled) {
        std.debug.print("[" ++ component ++ "] " ++ fmt ++ "\n", args);
    }
}

/// Lexer-specific logging
pub fn lexer(comptime fmt: []const u8, args: anytype) void {
    logComponent("LEXER", fmt, args);
}

/// Parser-specific logging
pub fn parser(comptime fmt: []const u8, args: anytype) void {
    logComponent("PARSER", fmt, args);
}

/// AST to IR conversion logging
pub fn astToIr(comptime fmt: []const u8, args: anytype) void {
    logComponent("AST->IR", fmt, args);
}

/// IR logging
pub fn ir(comptime fmt: []const u8, args: anytype) void {
    logComponent("IR", fmt, args);
}

/// Code generation logging
pub fn codegen(comptime fmt: []const u8, args: anytype) void {
    _ = fmt;
    _ = args;
    // logComponent("CODEGEN", fmt, args);
}
