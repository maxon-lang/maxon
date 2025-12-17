const std = @import("std");

pub const Program = struct {
    functions: []FunctionDecl,
};

pub const FunctionDecl = struct {
    name: []const u8,
    return_type: ?[]const u8, // null for void
    body: []Statement,
};

pub const Statement = union(enum) {
    @"return": ReturnStmt,
};

pub const ReturnStmt = struct {
    value: ?Expression,
};

pub const Expression = union(enum) {
    integer: i64,
};
