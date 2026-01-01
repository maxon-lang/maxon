const std = @import("std");

pub const Program = struct {
    types: []TypeDecl,
    enums: []EnumDecl,
    interfaces: []InterfaceDecl,
    functions: []FunctionDecl,
};

pub const InterfaceMethod = struct {
    name: []const u8,
    is_static: bool,
    params: []const ParamDecl,
    return_type: ?TypeExpr, // null for void
    has_default_impl: bool,
    default_body: ?[]Statement,
};

pub const InterfaceDecl = struct {
    name: []const u8,
    is_export: bool,
    generic_params: []const []const u8, // ["Element"] for `uses Element`
    extends: []const []const u8, // ["Collection", "Iterable"] for `extends Collection, Iterable`
    methods: []InterfaceMethod,
};

pub const EnumMember = struct {
    name: []const u8,
    value: ?*const Expression, // Optional explicit value (e.g., red = 1)
};

pub const EnumDecl = struct {
    name: []const u8,
    backing_type: ?[]const u8, // Optional backing type (int, string, etc.)
    members: []const EnumMember,
};

pub const InterfaceConformance = struct {
    interface_name: []const u8,
    type_args: []const []const u8,
};

pub const MethodDecl = struct {
    name: []const u8,
    qualified_name: ?[]const u8, // "Collection.count" for interface methods
    is_static: bool,
    is_export: bool,
    params: []const ParamDecl,
    return_type: ?TypeExpr, // null for void
    body: []Statement,
};

pub const TypeDecl = struct {
    name: []const u8,
    is_export: bool,
    generic_params: []const []const u8, // ["Element"] for `uses Element`
    conformances: []const InterfaceConformance,
    fields: []FieldDecl,
    methods: []MethodDecl,
};

pub const FieldDecl = struct {
    name: []const u8,
    type_expr: TypeExpr,
    is_mutable: bool,
};

pub const GenericTypeExpr = struct {
    base_type: []const u8, // Array, Map, etc.
    type_args: []const []const u8, // [int], [string, int], etc.
};

pub const TypeExpr = union(enum) {
    simple: []const u8, // int, float, MyStruct
    generic: GenericTypeExpr, // Array of int, Map of string int
    optional: *const TypeExpr, // T or nil
};

pub const ParamDecl = struct {
    name: []const u8,
    type_expr: TypeExpr,
};

pub const FunctionDecl = struct {
    name: []const u8,
    is_export: bool,
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

pub const IfStmt = struct {
    condition: Expression,
    body: []Statement,
    label: []const u8,
    else_body: ?[]Statement, // For else clause
    else_label: ?[]const u8, // Label for else block
    else_if: ?*const IfStmt, // For else-if chain (recursive)

    // If-let binding (optional): if let name = expr 'label'
    binding_name: ?[]const u8 = null,
};

pub const WhileStmt = struct {
    condition: Expression,
    body: []Statement,
    label: []const u8,
};

pub const ForStmt = struct {
    var_name: []const u8, // loop variable name
    iterable: Expression, // expression to iterate over
    body: []Statement,
    label: []const u8,
};

pub const BreakStmt = struct {
    label: ?[]const u8 = null, // Optional label to break to
};
pub const ContinueStmt = struct {
    label: ?[]const u8 = null, // Optional label to continue to
};

pub const ElseUnwrapDecl = struct {
    var_name: []const u8,
    optional_expr: *const Expression,
    default_body: []Statement,
    label: []const u8,
};

pub const StatementKind = union(enum) {
    @"return": ReturnStmt,
    let_decl: VarDecl,
    var_decl: VarDecl,
    index_assign: IndexAssign,
    assign: AssignStmt,
    field_assign: FieldAssign,
    call: CallExpr,
    method_call: MethodCallExpr,
    if_stmt: IfStmt,
    while_stmt: WhileStmt,
    for_stmt: ForStmt,
    break_stmt: BreakStmt,
    continue_stmt: ContinueStmt,
    else_unwrap_decl: ElseUnwrapDecl,
};

pub const Statement = struct {
    kind: StatementKind,
    line: u32,
    column: u32,
};

pub const VarDecl = struct {
    name: []const u8,
    type_annotation: ?TypeExpr, // Optional explicit type
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
    band, // bitwise AND
    bitor, // bitwise OR
    bxor, // bitwise XOR
    shl, // left shift
    shr, // right shift
};

pub const CompareOp = enum {
    eq,
    ne,
    lt,
    le,
    gt,
    ge,
};

pub const LogicalOp = enum {
    @"and",
    @"or",
};

pub const LogicalExpr = struct {
    left: *const Expression,
    op: LogicalOp,
    right: *const Expression,
};

pub const UnaryOp = enum {
    negate, // -x
    not, // not x (for future use)
};

pub const UnaryExpr = struct {
    op: UnaryOp,
    operand: *const Expression,
};

pub const BinaryExpr = struct {
    left: *const Expression,
    op: BinaryOp,
    right: *const Expression,
};

pub const CompareExpr = struct {
    left: *const Expression,
    op: CompareOp,
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
    type_args: []const []const u8, // ["int"] for `Container of int{...}`
    fields: []const FieldInit,
};

pub const FieldAccessExpr = struct {
    base: *const Expression,
    field_name: []const u8,
};

pub const ArrayLiteralExpr = struct {
    elements: []const Expression,
};

pub const MapEntry = struct {
    key: *const Expression,
    value: *const Expression,
};

pub const MapLiteralExpr = struct {
    entries: []const MapEntry,
};

pub const IndexExpr = struct {
    base: *const Expression,
    index: *const Expression,
};

pub const ArrayTypeExpr = struct {
    size: *const Expression,
    element_type: []const u8,
};

pub const MethodCallExpr = struct {
    base: *const Expression,
    method_name: []const u8,
    args: []const Expression,
};

pub const NilCoalesceExpr = struct {
    optional: *const Expression,
    default: *const Expression,
};

pub const CastExpr = struct {
    expr: *const Expression,
    target_type: []const u8, // "int", "byte", "float", etc.
};

pub const InterpolatedStringPart = struct {
    is_expression: bool,
    literal_value: ?[]const u8, // For literal parts
    expr: ?*const Expression, // For expression parts
    format_spec: ?[]const u8, // Optional format specifier (text after : in {expr:fmt})
};

pub const InterpolatedStringExpr = struct {
    parts: []const InterpolatedStringPart,
};

pub const ClosureParam = struct {
    name: []const u8,
    type_name: []const u8,
};

pub const ClosureExpr = struct {
    params: []const ClosureParam,
    body: *const Expression,
};

pub const Expression = union(enum) {
    integer: i64,
    float_lit: f64,
    bool_lit: bool,
    nil_lit,
    self_expr, // reference to current instance in methods
    identifier: []const u8,
    string_literal: []const u8, // double-quoted string literal "hello"
    interpolated_string: InterpolatedStringExpr, // string with interpolation "hello {name}!"
    char_literal: []const u8, // single-quoted character literal 'a' (grapheme cluster, UTF-8 bytes)
    unary: UnaryExpr,
    binary: BinaryExpr,
    compare: CompareExpr,
    logical: LogicalExpr,
    call: CallExpr,
    struct_init: StructInitExpr,
    field_access: FieldAccessExpr,
    array_literal: ArrayLiteralExpr,
    map_literal: MapLiteralExpr,
    index: IndexExpr,
    array_type: ArrayTypeExpr,
    method_call: MethodCallExpr,
    nil_coalesce: NilCoalesceExpr,
    cast: CastExpr,
    closure: ClosureExpr,
};
