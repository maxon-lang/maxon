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
    let_decl: VarDecl,
    var_decl: VarDecl,
};

pub const VarDecl = struct {
    name: []const u8,
    value: Expression,
};

pub const ReturnStmt = struct {
    value: ?Expression,
};

pub const BinaryOp = enum {
    add,
    sub,
    mul,
    div,
    mod,
};

pub const BinaryExpr = struct {
    left: *const Expression,
    op: BinaryOp,
    right: *const Expression,
};

pub const CallExpr = struct {
    func_name: []const u8,
    args: []const Expression,
};

pub const Expression = union(enum) {
    integer: i64,
    float_lit: f64,
    identifier: []const u8,
    binary: BinaryExpr,
    call: CallExpr,
};
