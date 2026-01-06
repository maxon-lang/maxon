const std = @import("std");

/// Information about a code block's extent and identifier (for folding, formatting, etc.)
pub const BlockInfo = struct {
    end_line: u32,
    identifier: ?[]const u8 = null, // label for control flow, name for declarations

    /// Optional secondary block (for if-else constructs)
    secondary: ?SecondaryBlock = null,

    pub const SecondaryBlock = struct {
        start_line: u32,
        end_line: u32,
        identifier: ?[]const u8 = null,
    };
};

/// A top-level constant declaration: let NAME = expression
pub const GlobalConstant = struct {
    name: []const u8,
    is_export: bool,
    value: Expression,
    line: u32,
    column: u32,
};

pub const Program = struct {
    types: []TypeDecl,
    enums: []EnumDecl,
    interfaces: []InterfaceDecl,
    functions: []FunctionDecl,
    global_constants: []GlobalConstant,
};

pub const InterfaceMethod = struct {
    name: []const u8,
    is_static: bool,
    params: []const ParamDecl,
    return_type: ?TypeExpr, // null for void
    throws_type: ?[]const u8, // error type if method throws (must conform to Error)
    has_default_impl: bool,
    default_body: ?[]Statement,
};

pub const InterfaceDecl = struct {
    name: []const u8,
    is_export: bool,
    generic_params: []const []const u8, // ["Element"] for `uses Element`
    extends: []const []const u8, // ["Collection", "Iterable"] for `extends Collection, Iterable`
    methods: []InterfaceMethod,
    line: u32 = 0,
    column: u32 = 0,
    end_line: u32 = 0,
};

pub const EnumMember = struct {
    name: []const u8,
    value: ?*const Expression, // Optional explicit value for raw value enums (e.g., red = 1)
    associated_values: []const ParamDecl, // Associated values (e.g., value(n int))
    line: u32,
    column: u32,
};

pub const EnumDecl = struct {
    name: []const u8,
    backing_type: ?[]const u8, // Optional backing type (int, string, etc.)
    conformances: []const InterfaceConformance, // Interface conformances (e.g., is Error)
    members: []const EnumMember,
    methods: []MethodDecl, // Methods defined on the enum
    line: u32 = 0,
    column: u32 = 0,
    end_line: u32 = 0,
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
    throws_type: ?[]const u8, // error type if method throws (must conform to Error)
    body: []Statement,
    line: u32 = 0,
    column: u32 = 0,
    end_line: u32 = 0, // Line of the 'end' keyword
};

pub const TypeDecl = struct {
    name: []const u8,
    is_export: bool,
    generic_params: []const []const u8, // ["Element"] for `uses Element`
    conformances: []const InterfaceConformance,
    fields: []FieldDecl,
    methods: []MethodDecl,
    line: u32 = 0,
    column: u32 = 0,
    end_line: u32 = 0,
};

pub const FieldDecl = struct {
    name: []const u8,
    type_expr: TypeExpr,
    is_mutable: bool,
    is_export: bool = false, // Whether field is accessible outside the type
    default_value: ?*const Expression = null, // Optional default value for field
};

pub const GenericTypeExpr = struct {
    base_type: []const u8, // Array, Map, etc.
    type_args: []const []const u8, // [int], [string, int], etc.
};

pub const FunctionTypeExpr = struct {
    param_types: []const TypeExpr, // Parameter types
    param_names: []const ?[]const u8, // Optional parameter names for documentation
    return_type: ?*const TypeExpr, // null for void
};

pub const ErrorUnionTypeExpr = struct {
    success_type: *const TypeExpr, // The success type T
    error_type: []const u8, // The error type name (must conform to Error)
};

pub const TypeExpr = union(enum) {
    simple: []const u8, // int, float, MyStruct
    generic: GenericTypeExpr, // Array of int, Map of string int
    optional: *const TypeExpr, // T or nil
    error_union: ErrorUnionTypeExpr, // T or E (where E conforms to Error)
    function_type: FunctionTypeExpr, // (int, string) returns bool
};

pub const ParamDecl = struct {
    name: []const u8,
    type_expr: TypeExpr,
    default_value: ?*const Expression = null, // Optional default value for parameter
};

/// A named argument at a call site: name = value
pub const NamedArg = struct {
    name: []const u8, // parameter name
    value: *const Expression, // argument value
    line: u32 = 0,
    column: u32 = 0,
};

pub const FunctionDecl = struct {
    name: []const u8,
    is_export: bool,
    params: []const ParamDecl,
    return_type: ?TypeExpr, // null for void
    throws_type: ?[]const u8, // error type if function throws (must conform to Error)
    body: []Statement,
    line: u32 = 0,
    column: u32 = 0,
    end_line: u32 = 0, // Line of the 'end' keyword
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
    end_line: u32 = 0, // Line of the 'end' keyword for then-block
    else_end_line: u32 = 0, // Line of the 'end' keyword for else-block (if present)
};

pub const WhileStmt = struct {
    condition: Expression,
    body: []Statement,
    label: []const u8,
    end_line: u32 = 0, // Line of the 'end' keyword
};

pub const ForStmt = struct {
    var_name: []const u8, // loop variable name
    iterable: Expression, // expression to iterate over
    body: []Statement,
    label: []const u8,
    end_line: u32 = 0, // Line of the 'end' keyword
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
    end_line: u32 = 0, // Line of the 'end' keyword
};

// Guard-let: let x = opt or 'label' ... end 'label'
// Body must contain exit (return/break/continue), x is unwrapped if optional has value
pub const GuardLetDecl = struct {
    var_name: []const u8,
    optional_expr: *const Expression,
    nil_body: []Statement, // Body executed when optional is nil (must exit)
    label: []const u8,
    end_line: u32 = 0, // Line of the 'end' keyword
};

// Error handling: throw statement
pub const ThrowStmt = struct {
    error_expr: Expression,
};

// Error handling: catch clause for do-catch blocks
pub const CatchClause = struct {
    binding_name: []const u8, // 'e' in 'catch e FileError'
    error_type: ?[]const u8, // null = catch any Error
    body: []Statement,
    label: []const u8,
};

// Error handling: do-catch block
pub const DoCatchStmt = struct {
    body: []Statement,
    label: []const u8,
    catches: []const CatchClause,
    end_line: u32 = 0, // Line of the 'end' keyword
};

// Match statements and expressions

// Match pattern binding for extracting associated values from enum cases
pub const PatternBinding = struct {
    case_name: []const u8, // enum case name (e.g., "value")
    bindings: []const []const u8, // binding names (e.g., ["n"] for value(n))
};

pub const MatchCase = struct {
    patterns: []const Expression, // Multiple patterns via 'or'
    pattern_bindings: []const ?PatternBinding, // Optional bindings for each pattern
    body: *const Statement, // Single statement (pointer to break circular dep)
    has_fallthrough: bool,
};

pub const MatchStmt = struct {
    scrutinee: Expression,
    cases: []const MatchCase,
    default_case: ?*const Statement, // Pointer to break circular dep
    label: []const u8,
    end_line: u32 = 0, // Line of the 'end' keyword
};

pub const MatchExprCase = struct {
    patterns: []const Expression,
    pattern_bindings: []const ?PatternBinding, // Optional bindings for each pattern
    result: Expression,
};

pub const MatchExpr = struct {
    scrutinee: *const Expression,
    cases: []const MatchExprCase,
    default_expr: ?*const Expression,
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
    guard_let_decl: GuardLetDecl,
    // Error handling
    throw_stmt: ThrowStmt,
    do_catch_stmt: DoCatchStmt,
    // Match statements
    match_stmt: MatchStmt,

    /// Returns block information if this statement kind contains a block
    pub fn getBlockInfo(self: StatementKind) ?BlockInfo {
        return switch (self) {
            .if_stmt => |if_s| if (if_s.end_line > 0) BlockInfo{
                .end_line = if_s.end_line,
                .identifier = if (if_s.label.len > 0) if_s.label else null,
                .secondary = if (if_s.else_body != null and if_s.else_end_line > 0)
                    BlockInfo.SecondaryBlock{
                        .start_line = if_s.end_line,
                        .end_line = if_s.else_end_line,
                        .identifier = if_s.else_label,
                    }
                else
                    null,
            } else null,
            .while_stmt => |w| if (w.end_line > 0) BlockInfo{
                .end_line = w.end_line,
                .identifier = if (w.label.len > 0) w.label else null,
            } else null,
            .for_stmt => |f| if (f.end_line > 0) BlockInfo{
                .end_line = f.end_line,
                .identifier = if (f.label.len > 0) f.label else null,
            } else null,
            .match_stmt => |m| if (m.end_line > 0) BlockInfo{
                .end_line = m.end_line,
                .identifier = if (m.label.len > 0) m.label else null,
            } else null,
            .do_catch_stmt => |d| if (d.end_line > 0) BlockInfo{
                .end_line = d.end_line,
                .identifier = if (d.label.len > 0) d.label else null,
            } else null,
            .else_unwrap_decl => |e| if (e.end_line > 0) BlockInfo{
                .end_line = e.end_line,
                .identifier = if (e.label.len > 0) e.label else null,
            } else null,
            .guard_let_decl => |g| if (g.end_line > 0) BlockInfo{
                .end_line = g.end_line,
                .identifier = if (g.label.len > 0) g.label else null,
            } else null,
            else => null,
        };
    }

    /// Returns child statement bodies for recursive block traversal
    pub fn getChildBodies(self: StatementKind) ChildBodies {
        return switch (self) {
            .if_stmt => |if_s| .{
                .primary = if_s.body,
                .secondary = if_s.else_body,
                .else_if = if_s.else_if,
            },
            .while_stmt => |w| .{ .primary = w.body },
            .for_stmt => |f| .{ .primary = f.body },
            .match_stmt => |m| .{ .match_cases = m.cases, .default_case = m.default_case },
            .do_catch_stmt => |d| .{ .primary = d.body, .catch_clauses = d.catches },
            .else_unwrap_decl => |e| .{ .primary = e.default_body },
            .guard_let_decl => |g| .{ .primary = g.nil_body },
            else => .{},
        };
    }

    pub const ChildBodies = struct {
        primary: []const Statement = &.{},
        secondary: ?[]const Statement = null,
        else_if: ?*const IfStmt = null,
        match_cases: []const MatchCase = &.{},
        default_case: ?*const Statement = null,
        catch_clauses: []const CatchClause = &.{},
    };
};

/// Helper to get block info from any declaration type that has line/end_line/name fields
pub fn getDeclBlockInfo(comptime T: type, decl: T) ?BlockInfo {
    if (@hasField(T, "end_line") and @hasField(T, "line")) {
        if (decl.end_line > 0) {
            const identifier = if (@hasField(T, "name")) decl.name else null;
            return BlockInfo{
                .end_line = decl.end_line,
                .identifier = identifier,
            };
        }
    }
    return null;
}

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
    named_args: []const NamedArg = &.{}, // Named arguments (name = value)
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

// Enum case construction with associated values (e.g., Result.success(42))
pub const EnumCaseExpr = struct {
    enum_name: []const u8,
    case_name: []const u8,
    args: []const Expression,
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
    named_args: []const NamedArg = &.{}, // Named arguments (name = value)
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

// Set from array literal: Set from [1, 2, 3]
pub const SetFromExpr = struct {
    type_name: []const u8, // "Set" or other InitableFromArrayLiteral conforming type
    type_args: []const []const u8, // Generic type arguments ["int"] for Set of int
    elements: *const Expression, // The array literal expression
};

// Error handling: try expression modes
pub const TryMode = enum {
    propagate, // try expr - propagates error to caller
};

pub const TryExpr = struct {
    expr: *const Expression,
    mode: TryMode,
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
    enum_case: EnumCaseExpr, // Enum case construction with associated values
    array_literal: ArrayLiteralExpr,
    map_literal: MapLiteralExpr,
    index: IndexExpr,
    array_type: ArrayTypeExpr,
    method_call: MethodCallExpr,
    nil_coalesce: NilCoalesceExpr,
    cast: CastExpr,
    closure: ClosureExpr,
    // Set/collection from array literal: Set from [1, 2, 3]
    set_from: SetFromExpr,
    // Error handling
    try_expr: TryExpr,
    // Match expressions
    match_expr: MatchExpr,
};

// ============================================================================
// AST Memory Management
// ============================================================================

pub fn freeProgram(program: Program, allocator: std.mem.Allocator) void {
    for (program.types) |type_decl| {
        for (type_decl.fields) |field| {
            freeTypeExpr(field.type_expr, allocator);
        }
        allocator.free(type_decl.fields);
        for (type_decl.methods) |method| {
            for (method.params) |param| {
                freeTypeExpr(param.type_expr, allocator);
            }
            allocator.free(method.params);
            for (method.body) |stmt| {
                freeStatementArgs(stmt, allocator);
            }
            allocator.free(method.body);
            if (method.qualified_name) |qn| {
                allocator.free(qn);
            }
            freeTypeExpr(method.return_type, allocator);
        }
        allocator.free(type_decl.methods);
        for (type_decl.conformances) |conformance| {
            for (conformance.type_args) |type_arg| {
                allocator.free(type_arg);
            }
            allocator.free(conformance.type_args);
        }
        allocator.free(type_decl.conformances);
        allocator.free(type_decl.generic_params);
    }
    allocator.free(program.types);

    for (program.enums) |enum_decl| {
        for (enum_decl.members) |member| {
            allocator.free(member.associated_values);
        }
        allocator.free(enum_decl.members);
        for (enum_decl.methods) |method| {
            for (method.params) |param| {
                freeTypeExpr(param.type_expr, allocator);
            }
            allocator.free(method.params);
            for (method.body) |stmt| {
                freeStatementArgs(stmt, allocator);
            }
            allocator.free(method.body);
            if (method.qualified_name) |qn| {
                allocator.free(qn);
            }
            freeTypeExpr(method.return_type, allocator);
        }
        allocator.free(enum_decl.methods);
        for (enum_decl.conformances) |conformance| {
            for (conformance.type_args) |type_arg| {
                allocator.free(type_arg);
            }
            allocator.free(conformance.type_args);
        }
        allocator.free(enum_decl.conformances);
    }
    allocator.free(program.enums);

    for (program.interfaces) |iface| {
        for (iface.methods) |method| {
            for (method.params) |param| {
                freeTypeExpr(param.type_expr, allocator);
            }
            allocator.free(method.params);
            freeTypeExpr(method.return_type, allocator);
            if (method.default_body) |body| {
                for (body) |stmt| {
                    freeStatementArgs(stmt, allocator);
                }
                allocator.free(body);
            }
        }
        allocator.free(iface.methods);
        allocator.free(iface.generic_params);
        allocator.free(iface.extends);
    }
    allocator.free(program.interfaces);

    for (program.functions) |func| {
        for (func.body) |stmt| {
            freeStatementArgs(stmt, allocator);
        }
        allocator.free(func.body);
        for (func.params) |param| {
            freeTypeExpr(param.type_expr, allocator);
        }
        allocator.free(func.params);
        freeTypeExpr(func.return_type, allocator);
    }
    allocator.free(program.functions);

    for (program.global_constants) |constant| {
        freeExpressionArgs(constant.value, allocator);
    }
    allocator.free(program.global_constants);
}

fn freeStatementArgs(stmt: Statement, allocator: std.mem.Allocator) void {
    switch (stmt.kind) {
        .let_decl, .var_decl => |decl| {
            freeTypeExpr(decl.type_annotation, allocator);
            freeExpressionArgs(decl.value, allocator);
        },
        .@"return" => |ret| {
            if (ret.value) |expr| {
                freeExpressionArgs(expr, allocator);
            }
        },
        .index_assign => |idx_assign| {
            freeExpressionArgs(idx_assign.base.*, allocator);
            freeExpressionArgs(idx_assign.index.*, allocator);
            freeExpressionArgs(idx_assign.value, allocator);
        },
        .assign => |assign| {
            freeExpressionArgs(assign.value, allocator);
        },
        .call => |call| {
            for (call.args) |arg| {
                freeExpressionArgs(arg, allocator);
            }
            allocator.free(call.args);
        },
        .method_call => |mcall| {
            freeExpressionArgs(mcall.base.*, allocator);
            for (mcall.args) |arg| {
                freeExpressionArgs(arg, allocator);
            }
            allocator.free(mcall.args);
        },
        .field_assign => |assign| {
            freeExpressionArgs(assign.base.*, allocator);
            freeExpressionArgs(assign.value, allocator);
        },
        .if_stmt => |if_s| {
            freeIfStmt(if_s, allocator);
        },
        .while_stmt => |while_s| {
            freeExpressionArgs(while_s.condition, allocator);
            for (while_s.body) |body_stmt| {
                freeStatementArgs(body_stmt, allocator);
            }
            allocator.free(while_s.body);
        },
        .for_stmt => |for_s| {
            freeExpressionArgs(for_s.iterable, allocator);
            for (for_s.body) |body_stmt| {
                freeStatementArgs(body_stmt, allocator);
            }
            allocator.free(for_s.body);
        },
        .break_stmt, .continue_stmt => {},
        .else_unwrap_decl => |unwrap| {
            freeExpressionArgs(unwrap.optional_expr.*, allocator);
            for (unwrap.default_body) |body_stmt| {
                freeStatementArgs(body_stmt, allocator);
            }
            allocator.free(unwrap.default_body);
        },
        .guard_let_decl => |guard| {
            freeExpressionArgs(guard.optional_expr.*, allocator);
            for (guard.nil_body) |body_stmt| {
                freeStatementArgs(body_stmt, allocator);
            }
            allocator.free(guard.nil_body);
        },
        .throw_stmt => |throw_s| {
            freeExpressionArgs(throw_s.error_expr, allocator);
        },
        .do_catch_stmt => |do_catch| {
            for (do_catch.body) |body_stmt| {
                freeStatementArgs(body_stmt, allocator);
            }
            allocator.free(do_catch.body);
            for (do_catch.catches) |catch_clause| {
                for (catch_clause.body) |catch_stmt| {
                    freeStatementArgs(catch_stmt, allocator);
                }
                allocator.free(catch_clause.body);
            }
            allocator.free(do_catch.catches);
        },
        .match_stmt => |match_s| {
            freeExpressionArgs(match_s.scrutinee, allocator);
            for (match_s.cases) |match_case| {
                for (match_case.patterns) |pattern| {
                    freeExpressionArgs(pattern, allocator);
                }
                allocator.free(match_case.patterns);
                freeStatementArgs(match_case.body.*, allocator);
                allocator.destroy(match_case.body);
            }
            allocator.free(match_s.cases);
            if (match_s.default_case) |default| {
                freeStatementArgs(default.*, allocator);
                allocator.destroy(default);
            }
        },
    }
}

fn freeIfStmt(if_s: IfStmt, allocator: std.mem.Allocator) void {
    freeExpressionArgs(if_s.condition, allocator);
    for (if_s.body) |body_stmt| {
        freeStatementArgs(body_stmt, allocator);
    }
    allocator.free(if_s.body);

    if (if_s.else_body) |else_body| {
        for (else_body) |else_stmt| {
            freeStatementArgs(else_stmt, allocator);
        }
        allocator.free(else_body);
    }

    if (if_s.else_if) |else_if| {
        freeIfStmt(else_if.*, allocator);
        allocator.destroy(else_if);
    }
}

fn freeExpressionArgs(expr: Expression, allocator: std.mem.Allocator) void {
    switch (expr) {
        .call => |call| {
            for (call.args) |arg| {
                freeExpressionArgs(arg, allocator);
            }
            allocator.free(call.args);
        },
        .binary => |bin| {
            freeExpressionArgs(bin.left.*, allocator);
            freeExpressionArgs(bin.right.*, allocator);
        },
        .compare => |cmp| {
            freeExpressionArgs(cmp.left.*, allocator);
            freeExpressionArgs(cmp.right.*, allocator);
        },
        .logical => |log| {
            freeExpressionArgs(log.left.*, allocator);
            freeExpressionArgs(log.right.*, allocator);
        },
        .struct_init => |sinit| {
            for (sinit.fields) |field| {
                freeExpressionArgs(field.value.*, allocator);
            }
            allocator.free(sinit.fields);
            if (sinit.type_args.len > 0) {
                allocator.free(sinit.type_args);
            }
        },
        .field_access => |fa| {
            freeExpressionArgs(fa.base.*, allocator);
        },
        .array_literal => |arr| {
            for (arr.elements) |elem| {
                freeExpressionArgs(elem, allocator);
            }
            allocator.free(arr.elements);
        },
        .map_literal => |map| {
            for (map.entries) |entry| {
                freeExpressionArgs(entry.key.*, allocator);
                freeExpressionArgs(entry.value.*, allocator);
            }
            allocator.free(map.entries);
        },
        .set_from => |sf| {
            freeExpressionArgs(sf.elements.*, allocator);
        },
        .index => |idx| {
            freeExpressionArgs(idx.base.*, allocator);
            freeExpressionArgs(idx.index.*, allocator);
        },
        .array_type => |arr| {
            freeExpressionArgs(arr.size.*, allocator);
        },
        .method_call => |mcall| {
            freeExpressionArgs(mcall.base.*, allocator);
            for (mcall.args) |arg| {
                freeExpressionArgs(arg, allocator);
            }
            allocator.free(mcall.args);
        },
        .unary => |un| {
            freeExpressionArgs(un.operand.*, allocator);
        },
        .nil_coalesce => {},
        .cast => |c| {
            freeExpressionArgs(c.expr.*, allocator);
        },
        .interpolated_string => |interp| {
            for (interp.parts) |part| {
                if (part.expr) |e| {
                    freeExpressionArgs(e.*, allocator);
                }
            }
            allocator.free(interp.parts);
        },
        .closure => |clos| {
            freeExpressionArgs(clos.body.*, allocator);
            allocator.free(clos.params);
        },
        .try_expr => |te| {
            freeExpressionArgs(te.expr.*, allocator);
        },
        .match_expr => |me| {
            freeExpressionArgs(me.scrutinee.*, allocator);
            for (me.cases) |match_case| {
                for (match_case.patterns) |pattern| {
                    freeExpressionArgs(pattern, allocator);
                }
                allocator.free(match_case.patterns);
                freeExpressionArgs(match_case.result, allocator);
            }
            allocator.free(me.cases);
            if (me.default_expr) |default| {
                freeExpressionArgs(default.*, allocator);
            }
        },
        .enum_case => |ec| {
            for (ec.args) |arg| {
                freeExpressionArgs(arg, allocator);
            }
            allocator.free(ec.args);
        },
        .integer, .float_lit, .bool_lit, .nil_lit, .self_expr, .identifier, .string_literal, .char_literal => {},
    }
}

fn freeTypeExpr(type_expr: ?TypeExpr, allocator: std.mem.Allocator) void {
    const te = type_expr orelse return;
    switch (te) {
        .optional => |inner| {
            freeTypeExpr(inner.*, allocator);
            allocator.destroy(@constCast(inner));
        },
        .generic => |g| {
            if (g.type_args.len > 0) {
                allocator.free(g.type_args);
            }
        },
        .function_type => |ft| {
            for (ft.param_types) |pt| {
                freeTypeExpr(pt, allocator);
            }
            if (ft.param_types.len > 0) {
                allocator.free(ft.param_types);
            }
            if (ft.param_names.len > 0) {
                allocator.free(ft.param_names);
            }
            if (ft.return_type) |rt| {
                freeTypeExpr(rt.*, allocator);
                allocator.destroy(@constCast(rt));
            }
        },
        .error_union => |eu| {
            freeTypeExpr(eu.success_type.*, allocator);
            allocator.destroy(@constCast(eu.success_type));
        },
        .simple => {},
    }
}
