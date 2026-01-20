const std = @import("std");

/// Information about a code block's extent and identifier (for folding, formatting, etc.)
pub const BlockInfo = struct {
    start_line: u32 = 0,
    start_column: u32 = 0,
    end_line: u32 = 0,
    identifier: ?[]const u8 = null, // label for control flow, name for declarations
};

/// Role/type of a child block within a statement
pub const BlockRole = enum {
    primary, // Main body (if body, while body, for body, do body)
    else_clause, // Else body for if statements
    else_if, // Else-if chain (contains nested IfStmt)
    catch_handler, // Catch clause body
    match_case, // Match case body
    default_case, // Default case for match
};

/// A child block with its role and metadata
pub const ChildBlock = struct {
    role: BlockRole,
    statements: []const Statement,
    info: BlockInfo = .{},

    // For catch clauses
    catch_binding: ?[]const u8 = null,
    catch_error_type: ?[]const u8 = null,

    // For match cases
    match_patterns: []const Expression = &.{},
    pattern_bindings: []const ?PatternBinding = &.{},
    has_fallthrough: bool = false,
    pattern_line: u32 = 0,
    pattern_column: u32 = 0,

    // For if-try else clauses with error binding
    error_binding: ?[]const u8 = null,
};

/// A top-level constant declaration: let NAME = expression
pub const GlobalConstant = struct {
    name: []const u8,
    is_export: bool,
    value: Expression,
    line: u32,
    column: u32,
};

/// A top-level mutable variable declaration: var NAME = expression
pub const GlobalVariable = struct {
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
    extensions: []ExtensionDecl,
    functions: []FunctionDecl,
    global_constants: []GlobalConstant,
    global_variables: []GlobalVariable,
    type_aliases: []TypeAliasDecl,
};

/// Type alias declaration (top-level or associated type in interface/type)
/// Syntax: typealias Name is GenericType with (TypeArg1, TypeArg2)
pub const TypeAliasDecl = struct {
    name: []const u8, // e.g., "ElementArray"
    base_type: []const u8, // e.g., "Array"
    type_args: []const []const u8, // e.g., ["Element"] or ["Key", "Value"]
    is_export: bool = false, // Whether this type alias is exported
    line: u32 = 0,
    column: u32 = 0,
};

pub const InterfaceMethod = struct {
    name: []const u8,
    is_static: bool,
    params: []const ParamDecl,
    return_type: ?TypeExpr, // null for void
    throws_type: ?[]const u8, // error type if method throws (must conform to Error)
};

pub const InterfaceDecl = struct {
    name: []const u8,
    is_export: bool,
    generic_params: []const []const u8, // ["Element"] for `uses Element`
    extends: []const []const u8, // ["Collection", "Iterable"] for `extends Collection, Iterable`
    associated_types: []TypeAliasDecl, // Associated type declarations
    methods: []InterfaceMethod,
    block: BlockInfo = .{},
};

pub const ExtensionMethod = struct {
    name: []const u8,
    params: []const ParamDecl,
    return_type: ?TypeExpr,
    throws_type: ?[]const u8,
    body: []Statement, // Required - extensions must have bodies
    block: BlockInfo = .{},
};

pub const ExtensionDecl = struct {
    interface_name: []const u8,
    is_export: bool,
    associated_types: []TypeAliasDecl, // Associated type declarations
    methods: []ExtensionMethod,
    block: BlockInfo = .{},
};

pub const EnumMember = struct {
    name: []const u8,
    value: ?*const Expression, // Optional explicit value for raw value enums (e.g., red = 1)
    associated_values: []const ParamDecl, // Associated values (e.g., value(n int))
    line: u32,
    column: u32,
    name_is_string_literal: bool = false, // true if name came from "quoted" string
    name_is_char_literal: bool = false, // true if name came from 'c' character
};

pub const EnumDecl = struct {
    name: []const u8,
    is_export: bool = false, // true if declared with 'export enum'
    conformances: []const InterfaceConformance, // Interface conformances (e.g., is Error)
    members: []const EnumMember,
    methods: []MethodDecl, // Methods defined on the enum
    block: BlockInfo = .{},
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
    block: BlockInfo = .{},
    doc_comment: ?[]const u8 = null, // Doc comment (/// or /** */) preceding the method
};

pub const TypeDecl = struct {
    name: []const u8,
    is_export: bool,
    generic_params: []const []const u8, // ["Element"] for `uses Element`
    conformances: []const InterfaceConformance,
    associated_types: []TypeAliasDecl, // Associated type declarations
    fields: []FieldDecl,
    methods: []MethodDecl,
    block: BlockInfo = .{},
};

pub const FieldDecl = struct {
    name: []const u8,
    type_expr: TypeExpr,
    is_mutable: bool,
    is_export: bool = false, // Whether field is accessible outside the type
    is_static: bool = false, // Whether this is a static field (shared across all instances)
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

pub const TypeExpr = struct {
    expr: TypeExprKind,
    line: u32 = 0,
    column: u32 = 0,

    pub const TypeExprKind = union(enum) {
        simple: []const u8, // int, float, MyStruct
        generic: GenericTypeExpr, // Array of int, Map of string int
        error_union: ErrorUnionTypeExpr, // T or E (where E conforms to Error)
        function_type: FunctionTypeExpr, // (int, string) returns bool
    };
};

pub const ParamDecl = struct {
    name: []const u8,
    type_expr: TypeExpr,
    default_value: ?*const Expression = null, // Optional default value for parameter
    line: u32 = 0,
    column: u32 = 0,
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
    block: BlockInfo = .{},
    doc_comment: ?[]const u8 = null, // Doc comment (/// or /** */) preceding the function
    line: u32 = 0,
    column: u32 = 0,
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

/// If-try condition: if try expr or if let x = try expr
pub const IfTryCondition = struct {
    try_expr: *const Expression, // The expression being tried
    binding_name: ?[]const u8 = null, // Variable name for binding form, null for boolean form
};

pub const IfStmt = struct {
    condition: Expression,
    if_try: ?*const IfTryCondition = null, // For if-try forms (mutually exclusive with condition usage)
    block: BlockInfo = .{},
    children: []const ChildBlock = &.{}, // primary body + optional else_clause or else_if
};

pub const WhileStmt = struct {
    condition: Expression,
    block: BlockInfo = .{},
    children: []const ChildBlock = &.{}, // primary body
};

pub const ForStmt = struct {
    var_name: []const u8, // loop variable name
    iterable: Expression, // expression to iterate over
    block: BlockInfo = .{},
    children: []const ChildBlock = &.{}, // primary body
};

pub const BreakStmt = struct {
    label: ?[]const u8 = null, // Optional label to break to
};
pub const ContinueStmt = struct {
    label: ?[]const u8 = null, // Optional label to continue to
};

// Error handling: throw statement
pub const ThrowStmt = struct {
    error_expr: Expression,
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
    block: BlockInfo = .{},
    children: []const ChildBlock = &.{}, // match_cases + optional default_case
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
    start_line: u32 = 0, // Line of 'match' keyword
    end_line: u32 = 0, // Line of 'end' keyword
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
    // Error handling
    throw_stmt: ThrowStmt,
    try_stmt: TryExpr, // try expression used as statement (for void-returning throwing functions)
    // Match statements
    match_stmt: MatchStmt,

    /// Returns block information if this statement kind contains a block
    pub fn getBlockInfo(self: StatementKind) ?BlockInfo {
        return switch (self) {
            .if_stmt => |s| if (s.block.end_line > 0) s.block else null,
            .while_stmt => |s| if (s.block.end_line > 0) s.block else null,
            .for_stmt => |s| if (s.block.end_line > 0) s.block else null,
            .match_stmt => |s| if (s.block.end_line > 0) s.block else null,
            else => null,
        };
    }

    /// Returns all child blocks for uniform iteration
    pub fn getChildBlocks(self: StatementKind) []const ChildBlock {
        return switch (self) {
            .if_stmt => |s| s.children,
            .while_stmt => |s| s.children,
            .for_stmt => |s| s.children,
            .match_stmt => |s| s.children,
            else => &.{},
        };
    }
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
    named_args: []const NamedArg = &.{}, // Named arguments (name = value)
};

pub const FieldInit = struct {
    name: []const u8,
    value: *const Expression,
};

pub const StructInitExpr = struct {
    type_name: ?[]const u8, // null for anonymous literals like {x: 1}
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

pub const MethodCallExpr = struct {
    base: *const Expression,
    method_name: []const u8,
    args: []const Expression,
    named_args: []const NamedArg = &.{}, // Named arguments (name = value)
    method_line: u32 = 0, // Line of the method name (for error reporting)
    method_column: u32 = 0, // Column of the method name (for error reporting)
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
    type_name: ?[]const u8, // null means infer from context
    type_line: u32 = 0, // line number of type annotation (for error reporting)
    type_column: u32 = 0, // column number of type annotation
};

pub const ClosureExpr = struct {
    params: []const ClosureParam,
    body: *const Expression,
};

// InitableFromArrayLiteral: TypeName from [1, 2, 3]
// Used by any type conforming to InitableFromArrayLiteral interface
pub const InitFromArrayExpr = struct {
    type_name: []const u8, // Type conforming to InitableFromArrayLiteral
    type_args: []const []const u8, // Generic type arguments
    elements: *const Expression, // The array literal expression
};

// Error handling: otherwise clause modes
pub const OtherwiseMode = enum {
    default_expr, // try expr otherwise defaultExpr
    ignore, // try expr otherwise ignore
    block, // try expr otherwise 'label' ... end 'label'
    block_with_err, // try expr otherwise (err) 'label' ... end 'label'
};

// Error handling: otherwise clause for try expressions
pub const OtherwiseClause = struct {
    mode: OtherwiseMode,
    default_expr: ?*const Expression = null,
    error_binding: ?[]const u8 = null,
    block: BlockInfo = .{},
    body: []const Statement = &.{},
};

// Error handling: try expression
pub const TryExpr = struct {
    expr: *const Expression,
    otherwise: ?*const OtherwiseClause = null,
};

pub const Expression = union(enum) {
    integer: i64,
    float_lit: f64,
    bool_lit: bool,
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
    method_call: MethodCallExpr,
    cast: CastExpr,
    closure: ClosureExpr,
    // InitableFromArrayLiteral: TypeName from [1, 2, 3]
    init_from_array: InitFromArrayExpr,
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
        }
        allocator.free(iface.methods);
        allocator.free(iface.generic_params);
        allocator.free(iface.extends);
    }
    allocator.free(program.interfaces);

    for (program.extensions) |ext| {
        for (ext.methods) |method| {
            for (method.params) |param| {
                freeTypeExpr(param.type_expr, allocator);
            }
            allocator.free(method.params);
            freeTypeExpr(method.return_type, allocator);
            for (method.body) |stmt| {
                freeStatementArgs(stmt, allocator);
            }
            allocator.free(method.body);
        }
        allocator.free(ext.methods);
    }
    allocator.free(program.extensions);

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

    for (program.global_variables) |variable| {
        freeExpressionArgs(variable.value, allocator);
    }
    allocator.free(program.global_variables);
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
            for (call.named_args) |named| {
                freeExpressionArgs(named.value.*, allocator);
            }
            if (call.named_args.len > 0) allocator.free(call.named_args);
        },
        .method_call => |mcall| {
            freeExpressionArgs(mcall.base.*, allocator);
            for (mcall.args) |arg| {
                freeExpressionArgs(arg, allocator);
            }
            allocator.free(mcall.args);
            for (mcall.named_args) |named| {
                freeExpressionArgs(named.value.*, allocator);
            }
            if (mcall.named_args.len > 0) allocator.free(mcall.named_args);
        },
        .field_assign => |assign| {
            freeExpressionArgs(assign.base.*, allocator);
            freeExpressionArgs(assign.value, allocator);
        },
        .if_stmt => |if_s| {
            freeExpressionArgs(if_s.condition, allocator);
            if (if_s.if_try) |if_try| {
                freeExpressionArgs(if_try.try_expr.*, allocator);
                allocator.destroy(if_try);
            }
            freeChildBlocks(if_s.children, allocator);
        },
        .while_stmt => |while_s| {
            freeExpressionArgs(while_s.condition, allocator);
            freeChildBlocks(while_s.children, allocator);
        },
        .for_stmt => |for_s| {
            freeExpressionArgs(for_s.iterable, allocator);
            freeChildBlocks(for_s.children, allocator);
        },
        .break_stmt, .continue_stmt => {},
        .throw_stmt => |throw_s| {
            freeExpressionArgs(throw_s.error_expr, allocator);
        },
        .try_stmt => |try_s| {
            freeExpressionArgs(try_s.expr.*, allocator);
            if (try_s.otherwise) |ow| {
                freeOtherwiseClause(ow, allocator);
            }
        },
        .match_stmt => |match_s| {
            freeExpressionArgs(match_s.scrutinee, allocator);
            freeChildBlocks(match_s.children, allocator);
        },
    }
}

fn freeChildBlocks(children: []const ChildBlock, allocator: std.mem.Allocator) void {
    for (children) |child| {
        // Free match patterns if present
        for (child.match_patterns) |pattern| {
            freeExpressionArgs(pattern, allocator);
        }
        if (child.match_patterns.len > 0) {
            allocator.free(child.match_patterns);
        }
        if (child.pattern_bindings.len > 0) {
            allocator.free(child.pattern_bindings);
        }
        // Free statements in the block
        for (child.statements) |stmt| {
            freeStatementArgs(stmt, allocator);
        }
        allocator.free(child.statements);
    }
    allocator.free(children);
}

fn freeExpressionArgs(expr: Expression, allocator: std.mem.Allocator) void {
    switch (expr) {
        .call => |call| {
            for (call.args) |arg| {
                freeExpressionArgs(arg, allocator);
            }
            allocator.free(call.args);
            for (call.named_args) |named| {
                freeExpressionArgs(named.value.*, allocator);
            }
            if (call.named_args.len > 0) allocator.free(call.named_args);
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
        .init_from_array => |ifa| {
            freeExpressionArgs(ifa.elements.*, allocator);
        },
        .index => |idx| {
            freeExpressionArgs(idx.base.*, allocator);
            freeExpressionArgs(idx.index.*, allocator);
        },
        .method_call => |mcall| {
            freeExpressionArgs(mcall.base.*, allocator);
            for (mcall.args) |arg| {
                freeExpressionArgs(arg, allocator);
            }
            allocator.free(mcall.args);
            for (mcall.named_args) |named| {
                freeExpressionArgs(named.value.*, allocator);
            }
            if (mcall.named_args.len > 0) allocator.free(mcall.named_args);
        },
        .unary => |un| {
            freeExpressionArgs(un.operand.*, allocator);
        },
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
            if (te.otherwise) |ow| {
                freeOtherwiseClause(ow, allocator);
            }
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
        .integer, .float_lit, .bool_lit, .self_expr, .identifier, .string_literal, .char_literal => {},
    }
}

fn freeOtherwiseClause(ow: *const OtherwiseClause, allocator: std.mem.Allocator) void {
    if (ow.default_expr) |expr| {
        freeExpressionArgs(expr.*, allocator);
    }
    for (ow.body) |stmt| {
        freeStatementArgs(stmt, allocator);
    }
    if (ow.body.len > 0) allocator.free(ow.body);
    allocator.destroy(@constCast(ow));
}

fn freeTypeExpr(type_expr: ?TypeExpr, allocator: std.mem.Allocator) void {
    const te = type_expr orelse return;
    switch (te.expr) {
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
