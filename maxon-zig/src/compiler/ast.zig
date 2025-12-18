const std = @import("std");

pub const Program = struct {
    types: []TypeDecl,
    enums: []EnumDecl,
    functions: []FunctionDecl,
};

pub const EnumDecl = struct {
    name: []const u8,
    members: []const []const u8,
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

pub const ArrayTypeExpr = struct {
    size: ?i64, // null for unsized (array of int), value for sized (array of 3 int)
    element_type: []const u8,
};

pub const TypeExpr = union(enum) {
    simple: []const u8, // int, float, MyStruct
    array: ArrayTypeExpr, // array of int, array of 3 int
};

pub const ParamDecl = struct {
    name: []const u8,
    type_expr: TypeExpr,
};

pub const FunctionDecl = struct {
    name: []const u8,
    params: []const ParamDecl,
    return_type: ?TypeExpr, // null for void
    body: []Statement,
};

pub const IndexAssign = struct {
    base: *const Expression,
    index: *const Expression,
    value: Expression,
};

pub const AssignStmt = struct {
    target: []const u8,
    value: Expression,
};

pub const FieldAssign = struct {
    base: *const Expression,
    field_name: []const u8,
    value: Expression,
};

pub const Statement = union(enum) {
    @"return": ReturnStmt,
    let_decl: VarDecl,
    var_decl: VarDecl,
    index_assign: IndexAssign,
    assign: AssignStmt,
    field_assign: FieldAssign,
    call: CallExpr,
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

pub const ArrayLiteralExpr = struct {
    elements: []const Expression,
};

pub const IndexExpr = struct {
    base: *const Expression,
    index: *const Expression,
};

pub const SizedArrayExpr = struct {
    size: *const Expression,
    element_type: []const u8,
};

pub const Expression = union(enum) {
    integer: i64,
    float_lit: f64,
    identifier: []const u8,
    binary: BinaryExpr,
    call: CallExpr,
    struct_init: StructInitExpr,
    field_access: FieldAccessExpr,
    array_literal: ArrayLiteralExpr,
    index: IndexExpr,
    sized_array: SizedArrayExpr,
};
