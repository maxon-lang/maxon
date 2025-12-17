const std = @import("std");

pub const Program = struct {
    types: []TypeDecl,
    functions: []FunctionDecl,
};

pub const TypeDecl = struct {
    name: []const u8,
    fields: []FieldDecl,
};

pub const FieldDecl = struct {
    name: []const u8,
    type_name: []const u8,
    is_mutable: bool,
};

pub const ParamDecl = struct {
    name: []const u8,
    type_name: []const u8,
};

pub const FunctionDecl = struct {
    name: []const u8,
    params: []const ParamDecl,
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

pub const FieldInit = struct {
    name: []const u8,
    value: *const Expression,
};

pub const StructInitExpr = struct {
    type_name: []const u8,
    fields: []const FieldInit,
};

pub const FieldAccessExpr = struct {
    base: *const Expression,
    field_name: []const u8,
};

pub const Expression = union(enum) {
    integer: i64,
    float_lit: f64,
    identifier: []const u8,
    binary: BinaryExpr,
    call: CallExpr,
    struct_init: StructInitExpr,
    field_access: FieldAccessExpr,
};
