const std = @import("std");
const lexer_mod = @import("1-lexer.zig");
const Token = lexer_mod.Token;
const TokenType = lexer_mod.TokenType;
const Lexer = lexer_mod.Lexer;
const ast = @import("ast.zig");
const debug = @import("debug.zig");
const err = @import("error.zig");

pub const ParseError = error{
    OutOfMemory,
    ExpectedExpression,
    UnexpectedToken,
    InvalidNumber,
    ExpectedNewline,
    PositionalAfterNamed,
};

pub const Parser = struct {
    tokens: []const Token,
    pos: usize,
    allocator: std.mem.Allocator,
    expr_ptrs: std.ArrayListUnmanaged(*ast.Expression),
    ifstmt_ptrs: std.ArrayListUnmanaged(*ast.IfStmt),
    stmt_ptrs: std.ArrayListUnmanaged(*ast.Statement),
    /// Source file path for error messages
    source_file: ?[]const u8,
    /// Last error with location info
    last_error: ?err.CompileError,

    pub fn init(tokens: []const Token, allocator: std.mem.Allocator) Parser {
        return initWithFile(tokens, allocator, null);
    }

    pub fn initWithFile(tokens: []const Token, allocator: std.mem.Allocator, source_file: ?[]const u8) Parser {
        return .{
            .tokens = tokens,
            .pos = 0,
            .allocator = allocator,
            .expr_ptrs = .empty,
            .ifstmt_ptrs = .empty,
            .stmt_ptrs = .empty,
            .source_file = source_file,
            .last_error = null,
        };
    }

    /// Create a Statement with a specific line and column number
    fn stmtAt(kind: ast.StatementKind, line: u32, column: u32) ast.Statement {
        return .{ .kind = kind, .line = line, .column = column };
    }

    pub fn deinit(self: *Parser) void {
        for (self.expr_ptrs.items) |ptr| {
            self.allocator.destroy(ptr);
        }
        self.expr_ptrs.deinit(self.allocator);
        // Note: ifstmt_ptrs are freed by freeProgram in 0-compiler.zig
        self.ifstmt_ptrs.deinit(self.allocator);
        // Note: stmt_ptrs are freed by freeProgram in 0-compiler.zig
        self.stmt_ptrs.deinit(self.allocator);
        // Free allocated error message if any
        if (self.last_error) |*e| {
            if (e.message_allocated) {
                self.allocator.free(e.message);
            }
        }
    }

    pub fn parse(self: *Parser) ParseError!ast.Program {
        var types: std.ArrayListUnmanaged(ast.TypeDecl) = .empty;
        errdefer types.deinit(self.allocator);
        var enums: std.ArrayListUnmanaged(ast.EnumDecl) = .empty;
        errdefer enums.deinit(self.allocator);
        var interfaces: std.ArrayListUnmanaged(ast.InterfaceDecl) = .empty;
        errdefer interfaces.deinit(self.allocator);
        var functions: std.ArrayListUnmanaged(ast.FunctionDecl) = .empty;
        errdefer functions.deinit(self.allocator);
        var global_constants: std.ArrayListUnmanaged(ast.GlobalConstant) = .empty;
        errdefer global_constants.deinit(self.allocator);

        // Skip leading newlines
        self.skipNewlines();

        while (!self.isAtEnd()) {
            // Check for type declaration (with optional 'export' prefix)
            if (self.check(.type)) {
                try types.append(self.allocator, try self.parseTypeDecl());
            } else if (self.check(.interface)) {
                try interfaces.append(self.allocator, try self.parseInterfaceDecl());
            } else if (self.check(.let)) {
                // Top-level let declaration (non-exported)
                try global_constants.append(self.allocator, try self.parseGlobalConstant(false));
            } else if (self.check(.@"export")) {
                // Peek ahead to see if this is 'export type', 'export interface', 'export function', or 'export let'
                if (self.peek(1)) |next| {
                    if (next.type == .type) {
                        try types.append(self.allocator, try self.parseTypeDecl());
                    } else if (next.type == .interface) {
                        try interfaces.append(self.allocator, try self.parseInterfaceDecl());
                    } else if (next.type == .function) {
                        _ = self.advance(); // skip 'export'
                        try functions.append(self.allocator, try self.parseFunctionWithExport(true));
                    } else if (next.type == .let) {
                        _ = self.advance(); // skip 'export'
                        try global_constants.append(self.allocator, try self.parseGlobalConstant(true));
                    } else {
                        self.reportErrorWithDetails(.E002, next.text);
                        return error.UnexpectedToken;
                    }
                } else {
                    self.reportErrorWithDetails(.E002, self.current().text);
                    return error.UnexpectedToken;
                }
            } else if (self.check(.@"enum")) {
                try enums.append(self.allocator, try self.parseEnumDecl());
            } else if (self.check(.function)) {
                try functions.append(self.allocator, try self.parseFunction());
            } else {
                // Report unexpected token with context
                const token = self.current();
                debug.parser("Parse error at line {d}: unexpected token '{s}' (type: {s})\n", .{
                    token.line,
                    token.text,
                    @tagName(token.type),
                });
                debug.parser("  Expected: 'type', 'enum', 'interface', 'function', or 'let' declaration\n", .{});
                self.reportErrorWithDetails(.E002, token.text);
                return error.UnexpectedToken;
            }
            self.skipNewlines();
        }

        return .{
            .types = try types.toOwnedSlice(self.allocator),
            .enums = try enums.toOwnedSlice(self.allocator),
            .interfaces = try interfaces.toOwnedSlice(self.allocator),
            .functions = try functions.toOwnedSlice(self.allocator),
            .global_constants = try global_constants.toOwnedSlice(self.allocator),
        };
    }

    fn current(self: *Parser) Token {
        if (self.pos < self.tokens.len) {
            return self.tokens[self.pos];
        }
        // Return EOF with last known position
        if (self.tokens.len > 0) {
            const last = self.tokens[self.tokens.len - 1];
            return .{ .type = .eof, .text = "", .line = last.line, .column = last.column };
        }
        return .{ .type = .eof, .text = "", .line = 1, .column = 1 };
    }

    fn reportError(self: *Parser, code: err.ErrorCode) void {
        const tok = self.current();
        self.last_error = .{
            .code = code,
            .message = code.message(),
            .location = .{
                .file = self.source_file,
                .line = tok.line,
                .column = tok.column,
            },
        };
    }

    fn reportErrorWithMessage(self: *Parser, message: []const u8) void {
        const tok = self.current();
        self.last_error = .{
            .code = .E029, // default case must be last
            .message = message,
            .location = .{
                .file = self.source_file,
                .line = tok.line,
                .column = tok.column,
            },
        };
    }

    fn reportErrorWithDetails(self: *Parser, code: err.ErrorCode, details: []const u8) void {
        const tok = self.current();
        const formatted = std.fmt.allocPrint(self.allocator, "{s}: '{s}'", .{ code.message(), details }) catch {
            // Fall back to basic message on allocation failure
            self.reportError(code);
            return;
        };
        self.last_error = .{
            .code = code,
            .message = formatted,
            .location = .{
                .file = self.source_file,
                .line = tok.line,
                .column = tok.column,
            },
            .message_allocated = true,
        };
    }

    fn reportBlockIdMismatch(self: *Parser, expected: []const u8, got: []const u8) void {
        const tok = self.current();
        const formatted = std.fmt.allocPrint(self.allocator, "{s}: expected '{s}', got '{s}'", .{ err.ErrorCode.E043.message(), expected, got }) catch {
            self.reportError(.E043);
            return;
        };
        self.last_error = .{
            .code = .E043,
            .message = formatted,
            .location = .{
                .file = self.source_file,
                .line = tok.line,
                .column = tok.column,
            },
            .message_allocated = true,
        };
    }

    fn parseTypeDecl(self: *Parser) !ast.TypeDecl {
        const is_export = self.check(.@"export");
        if (is_export) _ = self.advance();

        _ = try self.expect(.type);
        const name_token = try self.expect(.identifier);

        // Parse optional generic params: uses TypeParam, ...
        const generic_params = if (self.check(.uses)) blk: {
            _ = self.advance();
            break :blk try self.parseIdentifierList();
        } else &[_][]const u8{};

        // Parse optional interface conformances: is InterfaceName with TypeArg, ...
        var conformances: std.ArrayListUnmanaged(ast.InterfaceConformance) = .empty;
        errdefer conformances.deinit(self.allocator);

        if (self.check(.is)) {
            _ = self.advance();
            try conformances.append(self.allocator, try self.parseInterfaceConformance());

            while (self.check(.comma)) {
                _ = self.advance();
                try conformances.append(self.allocator, try self.parseInterfaceConformance());
            }
        }

        _ = try self.expect(.newline);

        // Parse fields and methods
        var fields: std.ArrayListUnmanaged(ast.FieldDecl) = .empty;
        errdefer fields.deinit(self.allocator);
        var methods: std.ArrayListUnmanaged(ast.MethodDecl) = .empty;
        errdefer methods.deinit(self.allocator);

        while (!self.check(.end) and !self.isAtEnd()) {
            if (self.check(.newline)) {
                _ = self.advance();
                continue;
            }

            // Check for method: export? static? function ...
            // export followed by static or function is a method
            // export followed by var or let is an exported field
            if (self.check(.@"export")) {
                if (self.peek(1)) |next| {
                    if (next.type == .static or next.type == .function) {
                        try methods.append(self.allocator, try self.parseMethodDecl());
                        continue;
                    }
                    // Otherwise fall through to field parsing (export var/let)
                }
            } else if (self.check(.static) or self.check(.function)) {
                try methods.append(self.allocator, try self.parseMethodDecl());
                continue;
            }

            // Parse field: export? let/var name [type] [= default_value]
            const field_is_export = self.check(.@"export");
            if (field_is_export) _ = self.advance();

            const is_mutable = self.check(.@"var");
            if (!is_mutable and !self.check(.let)) {
                self.reportErrorWithDetails(.E002, self.current().text);
                return error.UnexpectedToken;
            }
            _ = self.advance();

            const field_name = try self.expect(.identifier);

            // Check if type is specified or if we have `= expr` for type inference
            var field_type: ast.TypeExpr = undefined;
            var default_value: ?*const ast.Expression = null;

            if (self.check(.equals)) {
                // Type inferred from default value: `var count = 0`
                _ = self.advance(); // consume '='
                const default_expr = try self.parseExpression() orelse {
                    self.reportError(.E003);
                    return error.ExpectedExpression;
                };
                default_value = try self.createExpr(default_expr);
                // Infer type from the default expression
                field_type = try self.inferTypeFromExpr(default_expr);
            } else {
                // Explicit type specified
                field_type = try self.parseTypeExpr();

                // Check for optional default value: `var value int = 0`
                if (self.check(.equals)) {
                    _ = self.advance(); // consume '='
                    const default_expr = try self.parseExpression() orelse {
                        self.reportError(.E003);
                        return error.ExpectedExpression;
                    };
                    default_value = try self.createExpr(default_expr);
                }
            }

            try fields.append(self.allocator, .{
                .name = field_name.text,
                .type_expr = field_type,
                .is_mutable = is_mutable,
                .is_export = field_is_export,
                .default_value = default_value,
            });

            _ = try self.expect(.newline);
        }

        _ = try self.expectEndLabel(name_token.text);
        try self.expectEndNewline();

        return .{
            .name = name_token.text,
            .is_export = is_export,
            .generic_params = generic_params,
            .conformances = try conformances.toOwnedSlice(self.allocator),
            .fields = try fields.toOwnedSlice(self.allocator),
            .methods = try methods.toOwnedSlice(self.allocator),
            .line = name_token.line,
            .column = name_token.column,
        };
    }

    /// Parse a top-level constant declaration: let NAME = expression
    fn parseGlobalConstant(self: *Parser, is_export: bool) !ast.GlobalConstant {
        const let_token = try self.expect(.let);
        const name_token = try self.expect(.identifier);
        _ = try self.expect(.equals);

        const value_expr = try self.parseExpression() orelse {
            self.reportError(.E003);
            return error.ExpectedExpression;
        };

        _ = try self.expect(.newline);

        return .{
            .name = name_token.text,
            .is_export = is_export,
            .value = value_expr,
            .line = let_token.line,
            .column = let_token.column,
        };
    }

    fn parseInterfaceConformance(self: *Parser) !ast.InterfaceConformance {
        const interface_name = try self.expect(.identifier);

        var type_args: std.ArrayListUnmanaged([]const u8) = .empty;
        errdefer type_args.deinit(self.allocator);

        // Parse optional: with TypeArg or with (TypeArg1, TypeArg2)
        if (self.check(.with)) {
            _ = self.advance();

            // Check for parenthesized type args: with (X, Y)
            if (self.check(.lparen)) {
                _ = self.advance(); // consume '('
                const first_arg = try self.parseConformanceTypeArg();
                try type_args.append(self.allocator, first_arg);

                while (self.check(.comma)) {
                    _ = self.advance(); // consume comma
                    const arg = try self.parseConformanceTypeArg();
                    try type_args.append(self.allocator, arg);
                }

                _ = try self.expect(.rparen); // consume ')'
            } else {
                // Non-parenthesized: with TypeArg, TypeArg2, ...
                // Duplicate the string so all type_args are consistently allocated
                const first_arg = try self.expectTypeName();
                try type_args.append(self.allocator, try self.allocator.dupe(u8, first_arg));

                // Additional type args separated by commas (stop at newline or next conformance)
                while (self.check(.comma)) {
                    // Peek to check if this comma separates type args or conformances
                    if (self.peek(1)) |next| {
                        // If next is an identifier or type keyword, check if after that is 'with' (new conformance)
                        if (next.type == .identifier or self.isTypeKeyword(next.type)) {
                            if (self.peek(2)) |after_ident| {
                                if (after_ident.type == .with) {
                                    // This comma separates conformances, not type args
                                    break;
                                }
                            }
                        }
                    }
                    _ = self.advance(); // consume comma
                    const arg = try self.expectTypeName();
                    try type_args.append(self.allocator, try self.allocator.dupe(u8, arg));
                }
            }
        }

        return .{
            .interface_name = interface_name.text,
            .type_args = try type_args.toOwnedSlice(self.allocator),
        };
    }

    /// Parse a type argument in a conformance clause, supporting nested generics like "Pair with (Key, Value)"
    fn parseConformanceTypeArg(self: *Parser) ![]const u8 {
        var result: std.ArrayListUnmanaged(u8) = .empty;
        errdefer result.deinit(self.allocator);

        // Parse first type name
        const first = try self.expectTypeName();
        try result.appendSlice(self.allocator, first);

        // Check for nested "with" which indicates a generic type like "Pair with (Key, Value)"
        if (self.check(.with)) {
            _ = self.advance();
            try result.appendSlice(self.allocator, " with ");

            if (self.check(.lparen)) {
                _ = self.advance();
                try result.append(self.allocator, '(');

                // Parse first nested type arg
                const nested1 = try self.parseConformanceTypeArg();
                defer self.allocator.free(nested1);
                try result.appendSlice(self.allocator, nested1);

                // Parse additional nested type args
                while (self.check(.comma)) {
                    _ = self.advance();
                    try result.appendSlice(self.allocator, ", ");
                    const nested = try self.parseConformanceTypeArg();
                    defer self.allocator.free(nested);
                    try result.appendSlice(self.allocator, nested);
                }

                _ = try self.expect(.rparen);
                try result.append(self.allocator, ')');
            } else {
                // Single type arg without parens
                const nested = try self.expectTypeName();
                try result.appendSlice(self.allocator, nested);
            }
        }

        return try result.toOwnedSlice(self.allocator);
    }

    /// Check if token type is a primitive type keyword
    fn isTypeKeyword(self: *Parser, tt: TokenType) bool {
        _ = self;
        return tt == .int or tt == .float;
    }

    fn parseMethodDecl(self: *Parser) !ast.MethodDecl {
        // Parse optional modifiers
        const method_is_export = self.check(.@"export");
        if (method_is_export) _ = self.advance();
        const is_static = self.check(.static);
        if (is_static) _ = self.advance();

        _ = try self.expect(.function);

        // Parse method name - can be qualified like "Collection.count"
        const first_name = try self.expect(.identifier);
        var name: []const u8 = first_name.text;
        var qualified_name: ?[]const u8 = null;

        if (self.check(.dot)) {
            _ = self.advance();
            const second_name = try self.expect(.identifier);
            qualified_name = try std.fmt.allocPrint(self.allocator, "{s}.{s}", .{ first_name.text, second_name.text });
            name = second_name.text;
        }

        _ = try self.expect(.lparen);
        const params = try self.parseParamList();
        const return_type = try self.parseOptionalReturnType();
        const throws_type = try self.parseOptionalThrowsClause();
        _ = try self.expect(.newline);
        const body_result = try self.parseBodyUntilEnd(name);
        try self.expectEndNewline();
        if (self.check(.newline)) _ = self.advance();

        return .{
            .name = name,
            .qualified_name = qualified_name,
            .is_static = is_static,
            .is_export = method_is_export,
            .params = params,
            .return_type = return_type,
            .throws_type = throws_type,
            .body = body_result.body,
            .line = first_name.line,
            .column = first_name.column,
            .end_line = body_result.end_line,
        };
    }

    // ============================================
    // Common parsing helpers
    // ============================================

    /// Parse a comma-separated list of identifier names (for `uses T, U` or `extends A, B`)
    fn parseIdentifierList(self: *Parser) ![]const []const u8 {
        var items: std.ArrayListUnmanaged([]const u8) = .empty;
        errdefer items.deinit(self.allocator);

        const first = try self.expect(.identifier);
        try items.append(self.allocator, first.text);

        while (self.check(.comma)) {
            _ = self.advance();
            const item = try self.expect(.identifier);
            try items.append(self.allocator, item.text);
        }

        return items.toOwnedSlice(self.allocator);
    }

    /// Parse parameter list: (name type, name type = default, ...)
    /// Expects lparen already consumed, consumes up to and including rparen
    fn parseParamList(self: *Parser) ![]const ast.ParamDecl {
        var params: std.ArrayListUnmanaged(ast.ParamDecl) = .empty;
        errdefer params.deinit(self.allocator);

        if (!self.check(.rparen)) {
            const first_name = try self.expect(.identifier);
            const first_type = try self.parseTypeExpr();
            const first_default = try self.parseOptionalDefaultValue();
            try params.append(self.allocator, .{
                .name = first_name.text,
                .type_expr = first_type,
                .default_value = first_default,
            });

            while (self.check(.comma)) {
                _ = self.advance();
                const param_name = try self.expect(.identifier);
                const param_type = try self.parseTypeExpr();
                const param_default = try self.parseOptionalDefaultValue();
                try params.append(self.allocator, .{
                    .name = param_name.text,
                    .type_expr = param_type,
                    .default_value = param_default,
                });
            }
        }

        _ = try self.expect(.rparen);
        return params.toOwnedSlice(self.allocator);
    }

    /// Parse optional default value for parameter: = expression
    fn parseOptionalDefaultValue(self: *Parser) !?*const ast.Expression {
        if (self.check(.equals)) {
            _ = self.advance(); // consume '='
            const expr = try self.parseExpression() orelse {
                self.reportError(.E003);
                return error.ExpectedExpression;
            };
            return try self.createExpr(expr);
        }
        return null;
    }

    /// Parse optional return type: returns Type
    fn parseOptionalReturnType(self: *Parser) !?ast.TypeExpr {
        if (self.check(.returns)) {
            _ = self.advance();
            return try self.parseTypeExpr();
        }
        return null;
    }

    /// Parse optional throws clause: throws ErrorType
    fn parseOptionalThrowsClause(self: *Parser) !?[]const u8 {
        if (self.check(.throws)) {
            _ = self.advance();
            const error_type = try self.expect(.identifier);
            return error_type.text;
        }
        return null;
    }

    const BodyWithEndLine = struct {
        body: []ast.Statement,
        end_line: u32,
    };

    /// Parse statements until `end` keyword, then consume `end 'label'`
    fn parseBodyUntilEnd(self: *Parser, expected_label: []const u8) !BodyWithEndLine {
        var statements: std.ArrayListUnmanaged(ast.Statement) = .empty;
        errdefer statements.deinit(self.allocator);

        while (!self.check(.end) and !self.isAtEnd()) {
            if (self.check(.newline)) {
                _ = self.advance();
                continue;
            }
            try statements.append(self.allocator, try self.parseStatement());
        }

        const end_line = try self.expectEndLabel(expected_label);

        return .{
            .body = try statements.toOwnedSlice(self.allocator),
            .end_line = end_line,
        };
    }

    /// Expect newline after end (or EOF)
    fn expectEndNewline(self: *Parser) !void {
        if (!self.isAtEnd() and !self.check(.newline)) {
            self.reportError(.E004);
            return error.ExpectedNewline;
        }
    }

    fn expectTypeName(self: *Parser) ![]const u8 {
        if (self.check(.int)) {
            return self.advance().text;
        }
        if (self.check(.float)) {
            return self.advance().text;
        }
        if (self.check(.Self)) {
            return self.advance().text;
        }
        if (self.check(.array)) {
            _ = self.advance();
            // Normalize lowercase 'array' keyword to 'Array' type name
            return "Array";
        }
        if (self.check(.identifier)) {
            return self.advance().text;
        }
        self.reportErrorWithDetails(.E002, self.current().text);
        return error.UnexpectedToken;
    }

    fn parseTypeExpr(self: *Parser) ParseError!ast.TypeExpr {
        // Check for function type: (params) returns ReturnType
        if (self.check(.lparen)) {
            return try self.parseFunctionTypeExpr();
        }

        var base_type: ast.TypeExpr = undefined;

        // Parse type name (may include "Array" which is treated as a generic type)
        const type_name = try self.expectTypeName();

        // Check for generic type instantiation: TypeName of arg1 [arg2 ...]
        if (self.check(.of)) {
            _ = self.advance(); // consume 'of'

            // Parse type arguments for generic types
            // Note: "Array of int" and "Array of 5 int" are both stdlib Array types
            var type_args: std.ArrayListUnmanaged([]const u8) = .empty;
            errdefer type_args.deinit(self.allocator);

            // For Array type, skip optional size integer (e.g., "Array of 3 int")
            // The size is only used in expressions, not in type annotations
            if (std.mem.eql(u8, type_name, "Array") and self.check(.integer)) {
                _ = self.advance(); // skip the size - it's ignored for type checking
            }

            // First type argument is required
            try type_args.append(self.allocator, try self.expectTypeName());

            // Additional type arguments are optional (for types like Map of string int)
            while (self.check(.identifier) or self.check(.int) or self.check(.float)) {
                // Only consume if it looks like a type name (starts with lowercase or is a known type)
                const next = self.peek(0);
                if (next == null) break;
                // Check if this could be a type name (heuristic: not 'or', 'and', etc.)
                if (std.mem.eql(u8, next.?.text, "or") or
                    std.mem.eql(u8, next.?.text, "and") or
                    std.mem.eql(u8, next.?.text, "nil"))
                {
                    break;
                }
                try type_args.append(self.allocator, try self.expectTypeName());
            }

            base_type = .{ .generic = .{
                .base_type = type_name,
                .type_args = try type_args.toOwnedSlice(self.allocator),
            } };
        } else if (self.check(.from)) {
            // Check for "TypeName from K to V" syntax (two-argument generics like Map)
            _ = self.advance(); // consume 'from'

            // Parse first type argument (key type)
            const key_type = try self.expectTypeName();

            // Expect 'to' keyword
            _ = try self.expect(.to);

            // Parse second type argument (value type)
            const value_type = try self.expectTypeName();

            var type_args_arr = try self.allocator.alloc([]const u8, 2);
            type_args_arr[0] = key_type;
            type_args_arr[1] = value_type;

            base_type = .{ .generic = .{
                .base_type = type_name,
                .type_args = type_args_arr,
            } };
        } else {
            base_type = .{ .simple = type_name };
        }

        // Check for optional: T or nil
        if (self.check(.@"or")) {
            _ = self.advance(); // consume 'or'
            _ = try self.expect(.nil);

            const wrapped = try self.allocator.create(ast.TypeExpr);
            wrapped.* = base_type;
            return .{ .optional = wrapped };
        }

        return base_type;
    }

    /// Parse function type: (int, string) returns bool or (x int, y int) returns int
    fn parseFunctionTypeExpr(self: *Parser) ParseError!ast.TypeExpr {
        _ = try self.expect(.lparen);

        var param_types: std.ArrayListUnmanaged(ast.TypeExpr) = .empty;
        errdefer param_types.deinit(self.allocator);
        var param_names: std.ArrayListUnmanaged(?[]const u8) = .empty;
        errdefer param_names.deinit(self.allocator);

        // Parse parameter list
        if (!self.check(.rparen)) {
            // Parse first parameter
            const first_param = try self.parseFunctionTypeParam();
            try param_types.append(self.allocator, first_param.type_expr);
            try param_names.append(self.allocator, first_param.name);

            // Parse additional parameters
            while (self.check(.comma)) {
                _ = self.advance(); // consume comma
                const param = try self.parseFunctionTypeParam();
                try param_types.append(self.allocator, param.type_expr);
                try param_names.append(self.allocator, param.name);
            }
        }

        _ = try self.expect(.rparen);

        // Parse optional return type
        var return_type: ?*const ast.TypeExpr = null;
        if (self.check(.returns)) {
            _ = self.advance(); // consume 'returns'
            const ret = try self.parseTypeExpr();
            const ret_ptr = try self.allocator.create(ast.TypeExpr);
            ret_ptr.* = ret;
            return_type = ret_ptr;
        }

        const base_type: ast.TypeExpr = .{ .function_type = .{
            .param_types = try param_types.toOwnedSlice(self.allocator),
            .param_names = try param_names.toOwnedSlice(self.allocator),
            .return_type = return_type,
        } };

        // Check for optional: ((int) returns int) or nil
        if (self.check(.@"or")) {
            _ = self.advance(); // consume 'or'
            _ = try self.expect(.nil);

            const wrapped = try self.allocator.create(ast.TypeExpr);
            wrapped.* = base_type;
            return .{ .optional = wrapped };
        }

        return base_type;
    }

    const FunctionTypeParam = struct {
        name: ?[]const u8,
        type_expr: ast.TypeExpr,
    };

    /// Parse a single parameter in a function type: either "Type" or "name Type"
    fn parseFunctionTypeParam(self: *Parser) ParseError!FunctionTypeParam {
        // Could be: "Type" alone OR "name Type"
        // Look ahead to distinguish: if identifier followed by type-start, first is a name
        if (self.check(.identifier)) {
            const first = self.advance();

            // Check if next token starts a type (identifier, int, float, lparen, Array, Map, etc.)
            const next = self.peek(0);
            if (next != null) {
                const next_type = next.?.type;
                // If followed by another type expression start, first was a parameter name
                if (next_type == .identifier or next_type == .int or next_type == .float or
                    next_type == .lparen)
                {
                    const type_expr = try self.parseTypeExpr();
                    return .{ .name = first.text, .type_expr = type_expr };
                }
            }

            // Otherwise, first token was the type itself (e.g., a struct name)
            return .{ .name = null, .type_expr = .{ .simple = first.text } };
        }

        // Not an identifier, parse as type directly (int, float, etc.)
        const type_expr = try self.parseTypeExpr();
        return .{ .name = null, .type_expr = type_expr };
    }

    /// Infer a TypeExpr from an expression (for field default values)
    fn inferTypeFromExpr(self: *Parser, expr: ast.Expression) ParseError!ast.TypeExpr {
        _ = self;
        return switch (expr) {
            .integer => .{ .simple = "int" },
            .float_lit => .{ .simple = "float" },
            .bool_lit => .{ .simple = "bool" },
            .string_literal, .interpolated_string => .{ .simple = "string" },
            .char_literal => .{ .simple = "character" },
            .nil_lit => {
                // Cannot infer type from nil alone - this is an error case
                // but we'll let later semantic analysis catch it
                return .{ .simple = "nil" };
            },
            else => {
                // For complex expressions, we cannot infer at parse time
                // Return a placeholder and let semantic analysis handle it
                return .{ .simple = "int" }; // fallback
            },
        };
    }

    fn parseFunctionWithExport(self: *Parser, is_export: bool) !ast.FunctionDecl {
        _ = try self.expect(.function);
        const name_token = try self.expect(.identifier);
        _ = try self.expect(.lparen);
        const params = try self.parseParamList();
        const return_type = try self.parseOptionalReturnType();
        const throws_type = try self.parseOptionalThrowsClause();
        _ = try self.expect(.newline);
        const body_result = try self.parseBodyUntilEnd(name_token.text);
        try self.expectEndNewline();

        return .{
            .name = name_token.text,
            .is_export = is_export,
            .params = params,
            .return_type = return_type,
            .throws_type = throws_type,
            .body = body_result.body,
            .line = name_token.line,
            .column = name_token.column,
            .end_line = body_result.end_line,
        };
    }

    fn parseFunction(self: *Parser) !ast.FunctionDecl {
        return self.parseFunctionWithExport(false);
    }

    fn parseStatement(self: *Parser) ParseError!ast.Statement {
        const start_token = self.current();
        const start_line = start_token.line;
        const start_column = start_token.column;
        if (self.check(.@"return")) {
            const ret_stmt = try self.parseReturn();
            _ = try self.expect(.newline);
            return ret_stmt;
        }
        if (self.check(.let)) {
            const decl_stmt = try self.parseVarDecl(false);
            // else-unwrap and guard-let handle their own newlines
            if (decl_stmt.kind != .else_unwrap_decl and decl_stmt.kind != .guard_let_decl) {
                _ = try self.expect(.newline);
            }
            return decl_stmt;
        }
        if (self.check(.@"var")) {
            const decl_stmt = try self.parseVarDecl(true);
            // else-unwrap handles its own newlines
            if (decl_stmt.kind != .else_unwrap_decl) {
                _ = try self.expect(.newline);
            }
            return decl_stmt;
        }
        if (self.check(.@"if")) {
            return try self.parseIfStatement();
        }
        if (self.check(.@"while")) {
            return try self.parseWhileStatement();
        }
        if (self.check(.@"for")) {
            return try self.parseForStatement();
        }
        if (self.check(.@"break")) {
            _ = self.advance();
            // Optional label after break
            const label = if (self.check(.char_literal)) self.advance().text else null;
            _ = try self.expect(.newline);
            return stmtAt(.{ .break_stmt = .{ .label = label } }, start_line, start_column);
        }
        if (self.check(.@"continue")) {
            _ = self.advance();
            // Optional label after continue
            const label = if (self.check(.char_literal)) self.advance().text else null;
            _ = try self.expect(.newline);
            return stmtAt(.{ .continue_stmt = .{ .label = label } }, start_line, start_column);
        }
        // Error handling: throw statement
        if (self.check(.throw)) {
            return try self.parseThrowStatement();
        }
        // Error handling: do-catch block
        if (self.check(.do)) {
            return try self.parseDoCatchStatement();
        }
        // Match statement
        if (self.check(.match)) {
            return try self.parseMatchStatement();
        }
        // Check for assignment, index assignment, or call statement: identifier or self...
        if (self.check(.identifier) or self.check(.self)) {
            const start_pos = self.pos;
            // Try to parse as expression
            const expr = try self.parseExpression() orelse {
                self.reportError(.E003);
                return error.ExpectedExpression;
            };

            // Check if followed by '='
            if (self.check(.equals)) {
                _ = self.advance(); // consume '='
                const value = try self.parseExpression() orelse {
                    self.reportError(.E003);
                    return error.ExpectedExpression;
                };
                _ = try self.expect(.newline);

                // Check if this is an index expression (arr[i] = value)
                if (expr == .index) {
                    return stmtAt(.{ .index_assign = .{
                        .base = expr.index.base,
                        .index = expr.index.index,
                        .value = value,
                    } }, start_line, start_column);
                }

                // Check if this is a simple identifier (x = value)
                if (expr == .identifier) {
                    return stmtAt(.{ .assign = .{
                        .target = expr.identifier,
                        .value = value,
                    } }, start_line, start_column);
                }

                // Check if this is a field access (obj.field = value)
                if (expr == .field_access) {
                    return stmtAt(.{ .field_assign = .{
                        .base = expr.field_access.base,
                        .field_name = expr.field_access.field_name,
                        .value = value,
                    } }, start_line, start_column);
                }

                // Other expressions on LHS not supported
                self.reportErrorWithDetails(.E002, self.current().text);
                return error.UnexpectedToken;
            }

            // Check if this is a call expression (standalone function call)
            if (expr == .call) {
                _ = try self.expect(.newline);
                return stmtAt(.{ .call = expr.call }, start_line, start_column);
            }

            // Check if this is a method call (standalone method call like arr.push(x))
            if (expr == .method_call) {
                _ = try self.expect(.newline);
                return stmtAt(.{ .method_call = expr.method_call }, start_line, start_column);
            }

            // Not an assignment or call, restore position
            self.pos = start_pos;
        }
        self.reportErrorWithDetails(.E002, self.current().text);
        return error.UnexpectedToken;
    }

    fn parseVarDecl(self: *Parser, is_var: bool) !ast.Statement {
        const start_token = self.current();
        const start_line = start_token.line;
        const start_column = start_token.column;
        _ = self.advance(); // consume let/var
        const name_token = try self.expect(.identifier);

        _ = try self.expect(.equals);
        const value = try self.parseExpression();
        if (value) |expr| {
            // Check for else-unwrap: var x = opt else 'label' ... end 'label'
            if (self.check(.@"else")) {
                _ = self.advance(); // consume 'else'

                // Parse label
                const label = try self.expectBlockIdentifier();

                // Require newline
                _ = try self.expect(.newline);

                // Parse default body
                var default_statements: std.ArrayListUnmanaged(ast.Statement) = .empty;
                errdefer default_statements.deinit(self.allocator);

                while (!self.check(.end) and !self.isAtEnd()) {
                    if (self.check(.newline)) {
                        _ = self.advance();
                        continue;
                    }
                    try default_statements.append(self.allocator, try self.parseStatement());
                }

                const end_line = try self.expectEndLabel(label.text);
                try self.expectTrailingNewline();
                if (self.check(.newline)) {
                    _ = self.advance();
                }

                const expr_ptr = try self.createExpr(expr);
                return stmtAt(.{ .else_unwrap_decl = .{
                    .var_name = name_token.text,
                    .optional_expr = expr_ptr,
                    .default_body = try default_statements.toOwnedSlice(self.allocator),
                    .label = label.text,
                    .end_line = end_line,
                } }, start_line, start_column);
            }

            // Check for guard-let: let x = opt or 'label' ... end 'label'
            // Guard-let only works with 'let', not 'var'
            if (!is_var and self.check(.@"or")) {
                const next = self.peek(1);
                if (next != null and next.?.type == .char_literal) {
                    _ = self.advance(); // consume 'or'

                    // Parse label
                    const label = try self.expectBlockIdentifier();

                    // Require newline
                    _ = try self.expect(.newline);

                    // Parse nil body (must contain exit statement)
                    var nil_statements: std.ArrayListUnmanaged(ast.Statement) = .empty;
                    errdefer nil_statements.deinit(self.allocator);

                    while (!self.check(.end) and !self.isAtEnd()) {
                        if (self.check(.newline)) {
                            _ = self.advance();
                            continue;
                        }
                        try nil_statements.append(self.allocator, try self.parseStatement());
                    }

                    const end_line = try self.expectEndLabel(label.text);
                    try self.expectTrailingNewline();
                    if (self.check(.newline)) {
                        _ = self.advance();
                    }

                    const expr_ptr = try self.createExpr(expr);
                    return stmtAt(.{ .guard_let_decl = .{
                        .var_name = name_token.text,
                        .optional_expr = expr_ptr,
                        .nil_body = try nil_statements.toOwnedSlice(self.allocator),
                        .label = label.text,
                        .end_line = end_line,
                    } }, start_line, start_column);
                }
            }

            if (is_var) {
                return stmtAt(.{ .var_decl = .{ .name = name_token.text, .type_annotation = null, .value = expr } }, start_line, start_column);
            } else {
                return stmtAt(.{ .let_decl = .{ .name = name_token.text, .type_annotation = null, .value = expr } }, start_line, start_column);
            }
        }
        self.reportError(.E003);
        return error.ExpectedExpression;
    }

    fn parseReturn(self: *Parser) !ast.Statement {
        const start_token = self.current();
        const start_line = start_token.line;
        const start_column = start_token.column;
        _ = try self.expect(.@"return");
        const expr = try self.parseExpression();
        return stmtAt(.{ .@"return" = .{ .value = expr } }, start_line, start_column);
    }

    // -------------------------------------------------------------------------
    // Block parsing helpers
    // -------------------------------------------------------------------------

    /// Parse statements until 'end' keyword, skipping blank lines
    fn parseBlockBody(self: *Parser) ParseError![]ast.Statement {
        var statements: std.ArrayListUnmanaged(ast.Statement) = .empty;
        errdefer statements.deinit(self.allocator);

        while (!self.check(.end) and !self.isAtEnd()) {
            if (self.check(.newline)) {
                _ = self.advance();
                continue;
            }
            try statements.append(self.allocator, try self.parseStatement());
        }

        return statements.toOwnedSlice(self.allocator);
    }

    /// Parse 'end 'label'' and consume trailing newline (or EOF). Returns the line of the 'end' keyword.
    fn parseEndAndNewline(self: *Parser, expected_label: []const u8) ParseError!u32 {
        const end_line = try self.expectEndLabel(expected_label);
        try self.expectTrailingNewline();
        return end_line;
    }

    /// Require newline after a block end (or allow EOF)
    fn expectTrailingNewline(self: *Parser) ParseError!void {
        if (!self.isAtEnd() and !self.check(.newline)) {
            self.reportError(.E004);
            return error.ExpectedNewline;
        }
        if (self.check(.newline)) {
            _ = self.advance();
        }
    }

    /// Check for 'and fallthrough' suffix in match case, consuming both tokens if present
    fn checkAndConsumeFallthrough(self: *Parser) bool {
        if (self.check(.@"and")) {
            const next = self.peek(1);
            if (next != null and next.?.type == .fallthrough) {
                _ = self.advance(); // consume 'and'
                _ = self.advance(); // consume 'fallthrough'
                return true;
            }
        }
        return false;
    }

    /// Result of parsing an else clause
    const ElseClause = struct {
        body: ?[]ast.Statement,
        label: ?[]const u8,
        else_if: ?*const ast.IfStmt,
    };

    /// Parse optional else clause: 'else 'label' ... end 'label'' or 'else if ...'
    fn parseElseClause(self: *Parser, allow_else_if: bool) ParseError!ElseClause {
        if (!self.check(.@"else")) {
            return .{ .body = null, .label = null, .else_if = null };
        }

        _ = self.advance(); // consume 'else'

        // Check for else-if chain
        if (allow_else_if and self.check(.@"if")) {
            _ = self.advance(); // consume 'if'
            const else_if_stmt = try self.parseIfConditionAndBody();
            const ptr = try self.allocator.create(ast.IfStmt);
            ptr.* = else_if_stmt;
            try self.ifstmt_ptrs.append(self.allocator, ptr);
            return .{ .body = null, .label = null, .else_if = ptr };
        }

        // Parse else block: else 'label' ... end 'label'
        const else_label_tok = try self.expectBlockIdentifier();
        _ = try self.expect(.newline);
        const else_body = try self.parseBlockBody();
        _ = try self.expectEndLabel(else_label_tok.text);

        return .{ .body = else_body, .label = else_label_tok.text, .else_if = null };
    }

    // -------------------------------------------------------------------------
    // Control flow statements
    // -------------------------------------------------------------------------

    fn parseIfStatement(self: *Parser) ParseError!ast.Statement {
        const start_token = self.current();
        const start_line = start_token.line;
        const start_column = start_token.column;
        _ = try self.expect(.@"if");

        // Check for if-let: if let name = expr 'label'
        if (self.check(.let)) {
            _ = self.advance();
            const name_token = try self.expect(.identifier);
            _ = try self.expect(.equals);

            const condition = try self.parseExpression() orelse {
                self.reportError(.E003);
                return error.ExpectedExpression;
            };

            const label = try self.expectBlockIdentifier();
            _ = try self.expect(.newline);

            const body = try self.parseBlockBody();
            const end_line = try self.expectEndLabel(label.text);

            const else_clause = try self.parseElseClause(false);
            try self.expectTrailingNewline();

            return stmtAt(.{ .if_stmt = .{
                .condition = condition,
                .body = body,
                .label = label.text,
                .else_body = else_clause.body,
                .else_label = else_clause.label,
                .else_if = null,
                .binding_name = name_token.text,
                .end_line = end_line,
            } }, start_line, start_column);
        }

        // Regular if statement (if already consumed)
        return stmtAt(.{ .if_stmt = try self.parseIfConditionAndBody() }, start_line, start_column);
    }

    /// Parse if statement after 'if' has been consumed. Used by parseIfStatement
    /// and parseElseClause for else-if chains.
    fn parseIfConditionAndBody(self: *Parser) ParseError!ast.IfStmt {
        const condition = try self.parseExpression() orelse {
            self.reportError(.E003);
            return error.ExpectedExpression;
        };

        const label = try self.expectBlockIdentifier();
        _ = try self.expect(.newline);

        const body = try self.parseBlockBody();
        const end_line = try self.expectEndLabel(label.text);

        const else_clause = try self.parseElseClause(true);

        // Only expect trailing newline if we didn't chain to else-if
        if (else_clause.else_if == null) {
            try self.expectTrailingNewline();
        }

        return .{
            .condition = condition,
            .body = body,
            .label = label.text,
            .else_body = else_clause.body,
            .else_label = else_clause.label,
            .else_if = else_clause.else_if,
            .end_line = end_line,
        };
    }

    fn parseWhileStatement(self: *Parser) ParseError!ast.Statement {
        const start_token = self.current();
        const start_line = start_token.line;
        const start_column = start_token.column;
        _ = try self.expect(.@"while");

        const condition = try self.parseExpression() orelse {
            self.reportError(.E003);
            return error.ExpectedExpression;
        };

        const label = try self.expectBlockIdentifier();
        _ = try self.expect(.newline);

        const body = try self.parseBlockBody();
        const end_line = try self.parseEndAndNewline(label.text);

        return stmtAt(.{ .while_stmt = .{
            .condition = condition,
            .body = body,
            .label = label.text,
            .end_line = end_line,
        } }, start_line, start_column);
    }

    fn parseForStatement(self: *Parser) ParseError!ast.Statement {
        const start_token = self.current();
        const start_line = start_token.line;
        const start_column = start_token.column;
        _ = try self.expect(.@"for");

        // Expect loop variable name
        const var_name = try self.expect(.identifier);

        // Expect 'in' keyword
        _ = try self.expect(.in);

        // Parse iterable expression
        const iterable = try self.parseExpression() orelse {
            self.reportError(.E003);
            return error.ExpectedExpression;
        };

        // Expect label
        const label = try self.expectBlockIdentifier();
        _ = try self.expect(.newline);

        // Parse body
        const body = try self.parseBlockBody();
        const end_line = try self.parseEndAndNewline(label.text);

        return stmtAt(.{ .for_stmt = .{
            .var_name = var_name.text,
            .iterable = iterable,
            .body = body,
            .label = label.text,
            .end_line = end_line,
        } }, start_line, start_column);
    }

    /// Parse throw statement: throw errorExpr
    fn parseThrowStatement(self: *Parser) ParseError!ast.Statement {
        const start_token = self.current();
        const start_line = start_token.line;
        const start_column = start_token.column;

        _ = try self.expect(.throw);
        const error_expr = try self.parseExpression() orelse {
            self.reportError(.E003);
            return error.ExpectedExpression;
        };
        _ = try self.expect(.newline);

        return stmtAt(.{ .throw_stmt = .{
            .error_expr = error_expr,
        } }, start_line, start_column);
    }

    /// Parse do-catch statement:
    /// do 'label'
    ///     body
    /// end 'label' catch (e ErrorType) 'catchLabel'
    ///     catchBody
    /// end 'catchLabel'
    fn parseDoCatchStatement(self: *Parser) ParseError!ast.Statement {
        const start_token = self.current();
        const start_line = start_token.line;
        const start_column = start_token.column;

        _ = try self.expect(.do);
        const label = try self.expectBlockIdentifier();
        _ = try self.expect(.newline);

        const body = try self.parseBlockBody();
        const end_line = try self.expectEndLabel(label.text);

        // Parse catch clauses (at least one required)
        var catches: std.ArrayListUnmanaged(ast.CatchClause) = .empty;
        errdefer catches.deinit(self.allocator);

        while (self.check(.@"catch")) {
            try catches.append(self.allocator, try self.parseCatchClause());
        }

        if (catches.items.len == 0) {
            self.reportError(.E003); // Expected catch clause
            return error.ExpectedExpression;
        }

        return stmtAt(.{ .do_catch_stmt = .{
            .body = body,
            .label = label.text,
            .catches = try catches.toOwnedSlice(self.allocator),
            .end_line = end_line,
        } }, start_line, start_column);
    }

    /// Parse a single catch clause: catch (e ErrorType) 'label' ... end 'label'
    fn parseCatchClause(self: *Parser) ParseError!ast.CatchClause {
        _ = try self.expect(.@"catch");
        _ = try self.expect(.lparen);
        const binding_name = try self.expect(.identifier);

        // Optional error type - if not present, catches any Error
        var error_type: ?[]const u8 = null;
        if (self.check(.identifier)) {
            error_type = self.advance().text;
        }

        _ = try self.expect(.rparen);
        const label = try self.expectBlockIdentifier();
        _ = try self.expect(.newline);

        const catch_body = try self.parseBlockBody();
        _ = try self.expectEndLabel(label.text);

        // Only expect newline if not followed by another catch
        if (!self.check(.@"catch")) {
            try self.expectEndNewline();
        }

        return .{
            .binding_name = binding_name.text,
            .error_type = error_type,
            .body = catch_body,
            .label = label.text,
        };
    }

    /// Parse match statement: match <expr> 'label' ... end 'label'
    fn parseMatchStatement(self: *Parser) ParseError!ast.Statement {
        const start_token = self.current();
        const start_line = start_token.line;
        const start_column = start_token.column;

        _ = try self.expect(.match);

        // Parse scrutinee expression
        const scrutinee = try self.parseExpression() orelse {
            self.reportError(.E003);
            return error.ExpectedExpression;
        };

        // Parse block identifier
        const label = try self.expectBlockIdentifier();
        _ = try self.expect(.newline);

        // Parse cases
        var cases: std.ArrayListUnmanaged(ast.MatchCase) = .empty;
        errdefer cases.deinit(self.allocator);
        var default_case: ?*const ast.Statement = null;
        var saw_default = false;

        while (!self.check(.end) and !self.isAtEnd()) {
            if (self.check(.newline)) {
                _ = self.advance();
                continue;
            }

            // Check for default case
            if (self.check(.default)) {
                _ = self.advance();
                _ = try self.expect(.then);
                const result = try self.parseMatchCaseBody();
                default_case = try self.createStmt(result.stmt);
                saw_default = true;
                continue;
            }

            // Error if we see a pattern after default
            if (saw_default) {
                self.reportErrorWithMessage("'default' case must be the last case in match");
                return error.UnexpectedToken;
            }

            // Parse patterns (may have multiple via 'or')
            var patterns: std.ArrayListUnmanaged(ast.Expression) = .empty;
            errdefer patterns.deinit(self.allocator);
            var pattern_bindings: std.ArrayListUnmanaged(?ast.PatternBinding) = .empty;
            errdefer pattern_bindings.deinit(self.allocator);

            // Parse first pattern (may be identifier with bindings like `value(n)`)
            const first_result = try self.parseMatchPattern();
            try patterns.append(self.allocator, first_result.pattern);
            try pattern_bindings.append(self.allocator, first_result.binding);

            while (self.check(.@"or")) {
                // Check for 'or nil' which is type annotation, not pattern
                const next = self.peek(1);
                if (next != null and next.?.type == .nil) break;

                _ = self.advance(); // consume 'or'
                const pattern_result = try self.parseMatchPattern();
                try patterns.append(self.allocator, pattern_result.pattern);
                try pattern_bindings.append(self.allocator, pattern_result.binding);
            }

            _ = try self.expect(.then);

            // Parse single statement (handles newline internally)
            const result = try self.parseMatchCaseBody();

            try cases.append(self.allocator, .{
                .patterns = try patterns.toOwnedSlice(self.allocator),
                .pattern_bindings = try pattern_bindings.toOwnedSlice(self.allocator),
                .body = try self.createStmt(result.stmt),
                .has_fallthrough = result.has_fallthrough,
            });
        }

        const end_line = try self.expectEndLabel(label.text);
        try self.expectTrailingNewline();

        return stmtAt(.{ .match_stmt = .{
            .scrutinee = scrutinee,
            .cases = try cases.toOwnedSlice(self.allocator),
            .default_case = default_case,
            .label = label.text,
            .end_line = end_line,
        } }, start_line, start_column);
    }

    const MatchCaseResult = struct {
        stmt: ast.Statement,
        has_fallthrough: bool,
    };

    const MatchPatternResult = struct {
        pattern: ast.Expression,
        binding: ?ast.PatternBinding,
    };

    /// Parse a parenthesized list of binding names: (name1, name2, ...)
    /// Expects lparen already consumed, returns the list and consumes rparen
    fn parsePatternBindings(self: *Parser) ParseError![]const []const u8 {
        var bindings: std.ArrayListUnmanaged([]const u8) = .empty;
        errdefer bindings.deinit(self.allocator);

        if (!self.check(.rparen)) {
            const first_binding = try self.expect(.identifier);
            try bindings.append(self.allocator, first_binding.text);

            while (self.check(.comma)) {
                _ = self.advance();
                const next_binding = try self.expect(.identifier);
                try bindings.append(self.allocator, next_binding.text);
            }
        }
        _ = try self.expect(.rparen);

        return bindings.toOwnedSlice(self.allocator);
    }

    /// Parse a match pattern, which may include bindings for associated values
    /// Examples: `empty`, `value(n)`, `pair(a, b)`, `Color.red`, `Color.value(n)`
    fn parseMatchPattern(self: *Parser) ParseError!MatchPatternResult {
        // Check for identifier possibly followed by bindings or field access
        if (self.check(.identifier)) {
            const ident_token = self.advance();

            // Check if followed by '.' for field access (e.g., Color.red)
            if (self.check(.dot)) {
                _ = self.advance(); // consume '.'
                const field_token = try self.expect(.identifier);
                const base_expr = try self.createExpr(.{ .identifier = ident_token.text });

                // Check if field access is followed by '(' for bindings (e.g., Color.value(n))
                if (self.check(.lparen)) {
                    _ = self.advance(); // consume '('
                    const bindings = try self.parsePatternBindings();

                    return .{
                        .pattern = .{ .field_access = .{ .base = base_expr, .field_name = field_token.text } },
                        .binding = .{
                            .case_name = field_token.text,
                            .bindings = bindings,
                        },
                    };
                }

                // Simple field access like Color.red
                return .{
                    .pattern = .{ .field_access = .{ .base = base_expr, .field_name = field_token.text } },
                    .binding = null,
                };
            }

            // Check if followed by '(' for bindings (bare identifier with bindings like `value(n)`)
            if (self.check(.lparen)) {
                _ = self.advance(); // consume '('
                const bindings = try self.parsePatternBindings();

                return .{
                    .pattern = .{ .identifier = ident_token.text },
                    .binding = .{
                        .case_name = ident_token.text,
                        .bindings = bindings,
                    },
                };
            }

            // Plain identifier, no bindings
            return .{
                .pattern = .{ .identifier = ident_token.text },
                .binding = null,
            };
        }

        // Fall back to regular expression parsing for other patterns
        const pattern = try self.parseLogicalAnd() orelse {
            self.reportError(.E003);
            return error.ExpectedExpression;
        };
        return .{
            .pattern = pattern,
            .binding = null,
        };
    }

    /// Parse a single statement in a match case, handling 'and fallthrough'
    fn parseMatchCaseBody(self: *Parser) ParseError!MatchCaseResult {
        const start_token = self.current();
        const start_line = start_token.line;
        const start_column = start_token.column;

        // Handle return statement
        if (self.check(.@"return")) {
            _ = self.advance();
            const value = try self.parseExpression();
            // Check for 'and fallthrough' - not allowed with return, but we parse it anyway
            // Semantic analysis will catch the error
            const has_fallthrough = self.checkAndConsumeFallthrough();
            _ = try self.expect(.newline);
            return .{
                .stmt = stmtAt(.{ .@"return" = .{ .value = value } }, start_line, start_column),
                .has_fallthrough = has_fallthrough,
            };
        }

        // Parse other statements (assignment, call, etc.)
        if (self.check(.identifier) or self.check(.self)) {
            const expr = try self.parseExpression() orelse {
                self.reportError(.E003);
                return error.ExpectedExpression;
            };

            // Check if followed by '='
            if (self.check(.equals)) {
                _ = self.advance(); // consume '='
                const value = try self.parseExpression() orelse {
                    self.reportError(.E003);
                    return error.ExpectedExpression;
                };

                const has_fallthrough = self.checkAndConsumeFallthrough();
                _ = try self.expect(.newline);

                if (expr == .identifier) {
                    return .{
                        .stmt = stmtAt(.{ .assign = .{
                            .target = expr.identifier,
                            .value = value,
                        } }, start_line, start_column),
                        .has_fallthrough = has_fallthrough,
                    };
                }
                if (expr == .field_access) {
                    return .{
                        .stmt = stmtAt(.{ .field_assign = .{
                            .base = expr.field_access.base,
                            .field_name = expr.field_access.field_name,
                            .value = value,
                        } }, start_line, start_column),
                        .has_fallthrough = has_fallthrough,
                    };
                }
                if (expr == .index) {
                    return .{
                        .stmt = stmtAt(.{ .index_assign = .{
                            .base = expr.index.base,
                            .index = expr.index.index,
                            .value = value,
                        } }, start_line, start_column),
                        .has_fallthrough = has_fallthrough,
                    };
                }
                self.reportErrorWithDetails(.E002, self.current().text);
                return error.UnexpectedToken;
            }

            // Function call or method call
            if (expr == .call) {
                const has_fallthrough = self.checkAndConsumeFallthrough();
                _ = try self.expect(.newline);
                return .{
                    .stmt = stmtAt(.{ .call = expr.call }, start_line, start_column),
                    .has_fallthrough = has_fallthrough,
                };
            }
            if (expr == .method_call) {
                const has_fallthrough = self.checkAndConsumeFallthrough();
                _ = try self.expect(.newline);
                return .{
                    .stmt = stmtAt(.{ .method_call = expr.method_call }, start_line, start_column),
                    .has_fallthrough = has_fallthrough,
                };
            }
        }

        self.reportErrorWithDetails(.E002, self.current().text);
        return error.UnexpectedToken;
    }

    /// Parse match expression: match <expr> 'label' ... end 'label'
    fn parseMatchExpression(self: *Parser) ParseError!ast.Expression {
        _ = try self.expect(.match);

        // Parse scrutinee expression
        const scrutinee = try self.parseExpression() orelse {
            self.reportError(.E003);
            return error.ExpectedExpression;
        };

        // Parse block identifier
        const label = try self.expectBlockIdentifier();
        _ = try self.expect(.newline);

        // Parse cases
        var cases: std.ArrayListUnmanaged(ast.MatchExprCase) = .empty;
        errdefer cases.deinit(self.allocator);
        var default_expr: ?ast.Expression = null;

        while (!self.check(.end) and !self.isAtEnd()) {
            if (self.check(.newline)) {
                _ = self.advance();
                continue;
            }

            // Check for default case
            if (self.check(.default)) {
                _ = self.advance();
                _ = try self.expect(.gives);
                default_expr = try self.parseExpression() orelse {
                    self.reportError(.E003);
                    return error.ExpectedExpression;
                };
                _ = try self.expect(.newline);
                continue;
            }

            // Parse patterns (may have multiple via 'or')
            var patterns: std.ArrayListUnmanaged(ast.Expression) = .empty;
            errdefer patterns.deinit(self.allocator);
            var pattern_bindings: std.ArrayListUnmanaged(?ast.PatternBinding) = .empty;
            errdefer pattern_bindings.deinit(self.allocator);

            // Parse first pattern (may be identifier with bindings like `value(n)`)
            const first_result = try self.parseMatchPattern();
            try patterns.append(self.allocator, first_result.pattern);
            try pattern_bindings.append(self.allocator, first_result.binding);

            while (self.check(.@"or")) {
                // Check for 'or nil' which is type annotation, not pattern
                const next = self.peek(1);
                if (next != null and next.?.type == .nil) break;

                _ = self.advance(); // consume 'or'
                const pattern_result = try self.parseMatchPattern();
                try patterns.append(self.allocator, pattern_result.pattern);
                try pattern_bindings.append(self.allocator, pattern_result.binding);
            }

            _ = try self.expect(.gives);

            // Parse result expression
            const result = try self.parseExpression() orelse {
                self.reportError(.E003);
                return error.ExpectedExpression;
            };
            _ = try self.expect(.newline);

            try cases.append(self.allocator, .{
                .patterns = try patterns.toOwnedSlice(self.allocator),
                .pattern_bindings = try pattern_bindings.toOwnedSlice(self.allocator),
                .result = result,
            });
        }

        _ = try self.expectEndLabel(label.text);

        // Create the scrutinee pointer
        const scrutinee_ptr = try self.createExpr(scrutinee);

        // Create the default expression pointer if present
        const default_expr_ptr: ?*const ast.Expression = if (default_expr) |de|
            try self.createExpr(de)
        else
            null;

        return try self.parsePostfix(.{ .match_expr = .{
            .scrutinee = scrutinee_ptr,
            .cases = try cases.toOwnedSlice(self.allocator),
            .default_expr = default_expr_ptr,
            .label = label.text,
        } });
    }

    fn parseExpression(self: *Parser) ParseError!?ast.Expression {
        return try self.parseLogicalOr();
    }

    fn parseLogicalOr(self: *Parser) ParseError!?ast.Expression {
        var left = try self.parseLogicalAnd() orelse return null;

        while (self.check(.@"or")) {
            // Make sure 'or' is not followed by 'nil' (that's a type annotation, not expression)
            const next = self.peek(1);
            if (next != null and next.?.type == .nil) {
                // This is "T or nil" type annotation, not logical or
                break;
            }

            // Check for guard-let: 'or' followed by char_literal is guard-let syntax
            if (next != null and next.?.type == .char_literal) {
                // This is guard-let: `let x = opt or 'label' ... end 'label'`
                break;
            }

            _ = self.advance();
            const right = try self.parseLogicalAnd() orelse {
                self.reportError(.E003);
                return error.ExpectedExpression;
            };

            const left_ptr = try self.createExpr(left);
            const right_ptr = try self.createExpr(right);

            left = .{ .logical = .{
                .left = left_ptr,
                .op = .@"or",
                .right = right_ptr,
            } };
        }
        return left;
    }

    fn parseLogicalAnd(self: *Parser) ParseError!?ast.Expression {
        var left = try self.parseBitwiseOr() orelse return null;

        while (self.check(.@"and")) {
            // Check if 'and' is followed by 'fallthrough' - if so, this is not a logical operator
            // It's the 'and fallthrough' suffix for match case statements
            const next = self.peek(1);
            if (next != null and next.?.type == .fallthrough) {
                break;
            }

            _ = self.advance();
            const right = try self.parseBitwiseOr() orelse {
                self.reportError(.E003);
                return error.ExpectedExpression;
            };

            const left_ptr = try self.createExpr(left);
            const right_ptr = try self.createExpr(right);

            left = .{ .logical = .{
                .left = left_ptr,
                .op = .@"and",
                .right = right_ptr,
            } };
        }
        return left;
    }

    fn parseBitwiseOr(self: *Parser) ParseError!?ast.Expression {
        return self.parseBinaryOp(.pipe, .bitor, parseBitwiseXor);
    }

    fn parseBitwiseXor(self: *Parser) ParseError!?ast.Expression {
        return self.parseBinaryOp(.caret, .bxor, parseBitwiseAnd);
    }

    fn parseBitwiseAnd(self: *Parser) ParseError!?ast.Expression {
        return self.parseBinaryOp(.ampersand, .band, parseComparison);
    }

    fn parseBinaryOp(
        self: *Parser,
        token: TokenType,
        op: ast.BinaryOp,
        comptime nextParser: fn (*Parser) ParseError!?ast.Expression,
    ) ParseError!?ast.Expression {
        var left = try nextParser(self) orelse return null;

        while (self.check(token)) {
            _ = self.advance();
            const right = try nextParser(self) orelse {
                self.reportError(.E003);
                return error.ExpectedExpression;
            };
            left = try self.makeBinary(left, op, right);
        }
        return left;
    }

    fn parseComparison(self: *Parser) ParseError!?ast.Expression {
        var left = try self.parseShift() orelse return null;

        if (self.matchComparison()) |op| {
            _ = self.advance();
            const right = try self.parseShift() orelse {
                self.reportError(.E003);
                return error.ExpectedExpression;
            };
            left = try self.makeCompare(left, op, right);
        }
        return left;
    }

    fn matchComparison(self: *Parser) ?ast.CompareOp {
        if (self.check(.equals_equals)) return .eq;
        if (self.check(.not_equals)) return .ne;
        if (self.check(.less_than)) return .lt;
        if (self.check(.less_equals)) return .le;
        if (self.check(.greater_than)) return .gt;
        if (self.check(.greater_equals)) return .ge;
        return null;
    }

    fn makeCompare(self: *Parser, left: ast.Expression, op: ast.CompareOp, right: ast.Expression) !ast.Expression {
        return .{ .compare = .{
            .left = try self.createExpr(left),
            .op = op,
            .right = try self.createExpr(right),
        } };
    }

    fn parseShift(self: *Parser) ParseError!?ast.Expression {
        var left = try self.parseAdditive() orelse return null;

        while (self.matchShift()) |op| {
            _ = self.advance();
            const right = try self.parseAdditive() orelse {
                self.reportError(.E003);
                return error.ExpectedExpression;
            };
            left = try self.makeBinary(left, op, right);
        }
        return left;
    }

    fn matchShift(self: *Parser) ?ast.BinaryOp {
        if (self.check(.left_shift)) return .shl;
        if (self.check(.right_shift)) return .shr;
        return null;
    }

    fn parseAdditive(self: *Parser) ParseError!?ast.Expression {
        var left = try self.parseMultiplicative() orelse return null;

        while (self.matchAdditive()) |op| {
            _ = self.advance();
            const right = try self.parseMultiplicative() orelse {
                self.reportError(.E003);
                return error.ExpectedExpression;
            };
            left = try self.makeBinary(left, op, right);
        }
        return left;
    }

    fn parseMultiplicative(self: *Parser) ParseError!?ast.Expression {
        var left = try self.parseUnary() orelse return null;

        while (self.matchMultiplicative()) |op| {
            _ = self.advance();
            const right = try self.parseUnary() orelse {
                self.reportError(.E003);
                return error.ExpectedExpression;
            };
            left = try self.makeBinary(left, op, right);
        }
        return left;
    }

    fn parseUnary(self: *Parser) ParseError!?ast.Expression {
        // Check for unary minus
        if (self.check(.minus)) {
            _ = self.advance();
            const operand = try self.parseUnary() orelse {
                self.reportError(.E003);
                return error.ExpectedExpression;
            };
            return .{ .unary = .{
                .op = .negate,
                .operand = try self.createExpr(operand),
            } };
        }
        // Check for logical not
        if (self.check(.not)) {
            _ = self.advance();
            const operand = try self.parseUnary() orelse {
                self.reportError(.E003);
                return error.ExpectedExpression;
            };
            return .{ .unary = .{
                .op = .not,
                .operand = try self.createExpr(operand),
            } };
        }
        // Check for try expression (error propagation)
        if (self.check(.@"try")) {
            _ = self.advance();
            const mode: ast.TryMode = .propagate;
            const operand = try self.parseUnary() orelse {
                self.reportError(.E003);
                return error.ExpectedExpression;
            };
            return .{ .try_expr = .{
                .expr = try self.createExpr(operand),
                .mode = mode,
            } };
        }
        return self.parsePrimary();
    }

    fn matchAdditive(self: *Parser) ?ast.BinaryOp {
        if (self.check(.plus)) return .add;
        if (self.check(.minus)) return .sub;
        return null;
    }

    fn matchMultiplicative(self: *Parser) ?ast.BinaryOp {
        if (self.check(.star)) return .mul;
        if (self.check(.slash)) return .div;
        if (self.check(.mod)) return .mod;
        return null;
    }

    fn makeBinary(self: *Parser, left: ast.Expression, op: ast.BinaryOp, right: ast.Expression) !ast.Expression {
        return .{ .binary = .{
            .left = try self.createExpr(left),
            .op = op,
            .right = try self.createExpr(right),
        } };
    }

    fn createExpr(self: *Parser, expr: ast.Expression) !*ast.Expression {
        const ptr = try self.allocator.create(ast.Expression);
        ptr.* = expr;
        try self.expr_ptrs.append(self.allocator, ptr);
        return ptr;
    }

    fn createStmt(self: *Parser, stmt: ast.Statement) !*ast.Statement {
        const ptr = try self.allocator.create(ast.Statement);
        ptr.* = stmt;
        try self.stmt_ptrs.append(self.allocator, ptr);
        return ptr;
    }

    fn parsePrimary(self: *Parser) ParseError!?ast.Expression {
        // Match expression
        if (self.check(.match)) {
            return try self.parseMatchExpression();
        }
        if (self.check(.integer)) {
            const token = self.advance();
            const value = std.fmt.parseInt(i64, token.text, 0) catch {
                self.reportError(.E001);
                return error.InvalidNumber;
            };
            return try self.parsePostfix(.{ .integer = value });
        }
        if (self.check(.float_literal)) {
            const token = self.advance();
            const value = std.fmt.parseFloat(f64, token.text) catch {
                self.reportError(.E001);
                return error.InvalidNumber;
            };
            return try self.parsePostfix(.{ .float_lit = value });
        }
        if (self.check(.true)) {
            _ = self.advance();
            return try self.parsePostfix(.{ .bool_lit = true });
        }
        if (self.check(.false)) {
            _ = self.advance();
            return try self.parsePostfix(.{ .bool_lit = false });
        }
        // Nil literal
        if (self.check(.nil)) {
            _ = self.advance();
            return try self.parsePostfix(.nil_lit);
        }
        // String literal
        if (self.check(.string_literal)) {
            const token = self.advance();
            return try self.parsePostfix(.{ .string_literal = token.text });
        }
        // String with interpolation
        if (self.check(.string_interp)) {
            const token = self.advance();
            return try self.parseInterpolatedString(token.text);
        }
        // Character literal (also used for end labels, but here as an expression)
        if (self.check(.char_literal)) {
            const token = self.advance();
            return try self.parsePostfix(.{ .char_literal = token.text });
        }
        // Self expression (instance reference in methods)
        if (self.check(.self)) {
            _ = self.advance();
            return try self.parsePostfix(.self_expr);
        }
        // Array literal: [expr, expr, ...]
        if (self.check(.lbracket)) {
            return try self.parseArrayLiteral();
        }
        // Sized array: Array of N type
        if (self.check(.array)) {
            return try self.parseArrayType();
        }
        // Check for Self keyword (for Self{...} struct initialization in methods)
        if (self.check(.Self)) {
            const token = self.advance();
            if (self.check(.lbrace)) {
                return try self.parseStructInit(token.text, &.{});
            }
            return try self.parsePostfix(.{ .identifier = token.text });
        }
        if (self.check(.identifier)) {
            const token = self.advance();

            // Check for generic type instantiation: TypeName of T{...}
            // or sized array: Array of N type
            if (self.check(.of)) {
                _ = self.advance(); // consume 'of'

                // Check for sized array: Array of <expr> <type>
                // Sized arrays are detected by the token after 'of':
                // - integer literal: Array of 5 int
                // - lparen: Array of (n+1) int
                // - minus (negative): Array of -3 int
                // - lowercase identifier (variable): Array of n int
                // Generic types have a type name (int/float/bool/byte or capitalized identifier)
                const is_array_type = blk: {
                    if (!std.mem.eql(u8, token.text, "Array")) break :blk false;
                    if (self.check(.integer) or self.check(.lparen) or self.check(.minus)) break :blk true;
                    if (self.check(.identifier)) {
                        const next = self.peek(0);
                        if (next) |tok| {
                            // Lowercase identifier that's not a builtin type is a variable (size expr)
                            // Type names are: int, float, bool, byte, string, character, or start uppercase
                            const first_char = tok.text[0];
                            const is_builtin_type = std.mem.eql(u8, tok.text, "int") or
                                std.mem.eql(u8, tok.text, "float") or
                                std.mem.eql(u8, tok.text, "bool") or
                                std.mem.eql(u8, tok.text, "byte") or
                                std.mem.eql(u8, tok.text, "string") or
                                std.mem.eql(u8, tok.text, "character");
                            const starts_uppercase = first_char >= 'A' and first_char <= 'Z';
                            // If it's not a builtin type and doesn't start uppercase, it's a variable
                            break :blk !is_builtin_type and !starts_uppercase;
                        }
                    }
                    break :blk false;
                };

                if (is_array_type) {
                    // Parse size expression
                    const size_expr = try self.parseExpression() orelse {
                        self.reportError(.E003);
                        return error.ExpectedExpression;
                    };
                    const size_ptr = try self.createExpr(size_expr);

                    // Parse element type
                    const elem_type = try self.expectTypeName();

                    return try self.parsePostfix(.{ .array_type = .{
                        .size = size_ptr,
                        .element_type = elem_type,
                    } });
                }

                // Parse type arguments
                var type_args: std.ArrayListUnmanaged([]const u8) = .empty;
                errdefer type_args.deinit(self.allocator);

                // First type argument is required
                try type_args.append(self.allocator, try self.expectTypeName());

                // Additional type arguments (for types like Map of string int)
                while (self.check(.identifier) or self.check(.int) or self.check(.float)) {
                    const next = self.peek(0);
                    if (next == null) break;
                    // Stop if next token is a keyword or could be part of expression
                    if (std.mem.eql(u8, next.?.text, "or") or
                        std.mem.eql(u8, next.?.text, "and") or
                        std.mem.eql(u8, next.?.text, "nil"))
                    {
                        break;
                    }
                    // Only continue if followed by identifier (another type arg)
                    // or lbrace (struct init)
                    const peek1 = self.peek(1);
                    if (peek1 != null and peek1.?.type != .identifier and peek1.?.type != .int and peek1.?.type != .float and peek1.?.type != .lbrace) {
                        break;
                    }
                    try type_args.append(self.allocator, try self.expectTypeName());
                }

                // Must be followed by struct init
                if (self.check(.lbrace)) {
                    return try self.parseStructInit(token.text, try type_args.toOwnedSlice(self.allocator));
                }

                // Otherwise this is just a generic type used as expression (error case)
                self.reportErrorWithDetails(.E002, self.current().text);
                return error.UnexpectedToken;
            }

            // Check for "TypeName from ..." syntax:
            // - "TypeName from [...]" for InitableFromArrayLiteral types like Set
            // - "TypeName from K to V{}" for two-argument generics like Map
            if (self.check(.from)) {
                _ = self.advance(); // consume 'from'

                // Check for array literal: Set from [1, 2, 3]
                if (self.check(.lbracket)) {
                    const arr_lit = try self.parseArrayLiteral();
                    const arr_lit_ptr = try self.createExpr(arr_lit);
                    return try self.parsePostfix(.{
                        .set_from = .{
                            .type_name = token.text,
                            .type_args = &.{}, // Element type inferred from array
                            .elements = arr_lit_ptr,
                        },
                    });
                }

                // Otherwise, parse "TypeName from K to V{}" syntax for Map
                const key_type = try self.expectTypeName();

                // Expect 'to' keyword
                _ = try self.expect(.to);

                // Parse second type argument (value type)
                const value_type = try self.expectTypeName();

                // Must be followed by struct init {}
                if (self.check(.lbrace)) {
                    const type_args_slice = try self.allocator.alloc([]const u8, 2);
                    type_args_slice[0] = key_type;
                    type_args_slice[1] = value_type;
                    return try self.parseStructInit(token.text, type_args_slice);
                }

                // Error: Map from K to V requires {}
                self.reportErrorWithDetails(.E002, self.current().text);
                return error.UnexpectedToken;
            }

            // Check for struct initialization: TypeName{...}
            if (self.check(.lbrace)) {
                return try self.parseStructInit(token.text, &.{});
            }
            // Check for function call
            if (self.check(.lparen)) {
                return try self.parseCall(token.text);
            }
            return try self.parsePostfix(.{ .identifier = token.text });
        }
        if (self.check(.lparen)) {
            // Check if this is a closure: (name type, ...) gives expr
            if (self.isClosureStart()) {
                return try self.parseClosure();
            }
            _ = self.advance(); // consume '('
            const expr = try self.parseExpression() orelse {
                self.reportError(.E003);
                return error.ExpectedExpression;
            };
            _ = try self.expect(.rparen);
            return try self.parsePostfix(expr);
        }
        return null;
    }

    /// Check if the current position is the start of a closure: (name type, ...) gives
    fn isClosureStart(self: *Parser) bool {
        // We're at '('
        // Closure format: (identifier type [, identifier type ...]) gives
        // Look for pattern: ( identifier type [,|)] ) gives
        var lookahead: usize = 1;

        // Skip the lparen
        const after_lparen = self.peek(lookahead) orelse return false;
        if (after_lparen.type != .identifier) return false;
        lookahead += 1;

        // After identifier, expect type name (identifier or int/float/bool)
        const type_tok = self.peek(lookahead) orelse return false;
        if (type_tok.type != .identifier and type_tok.type != .int and type_tok.type != .float) return false;
        lookahead += 1;

        // Now we can have: ) gives  OR  , identifier type ...
        // Skip params until we hit )
        while (true) {
            const tok = self.peek(lookahead) orelse return false;
            if (tok.type == .rparen) {
                lookahead += 1;
                break;
            }
            if (tok.type == .comma) {
                lookahead += 1;
                // Expect identifier type
                const id_tok = self.peek(lookahead) orelse return false;
                if (id_tok.type != .identifier) return false;
                lookahead += 1;
                const t_tok = self.peek(lookahead) orelse return false;
                if (t_tok.type != .identifier and t_tok.type != .int and t_tok.type != .float) return false;
                lookahead += 1;
            } else {
                return false;
            }
        }

        // Check for 'gives' keyword
        const gives_tok = self.peek(lookahead) orelse return false;
        return gives_tok.type == .gives;
    }

    /// Parse a closure: (name type, ...) gives expr
    fn parseClosure(self: *Parser) ParseError!ast.Expression {
        _ = self.advance(); // consume '('

        var params: std.ArrayListUnmanaged(ast.ClosureParam) = .empty;
        errdefer params.deinit(self.allocator);

        // Parse params: identifier type, identifier type, ...
        while (!self.check(.rparen)) {
            const name_tok = try self.expect(.identifier);
            // Parse type (can be identifier like "int" or type keyword)
            const type_name = if (self.check(.int)) blk: {
                _ = self.advance();
                break :blk "int";
            } else if (self.check(.float)) blk: {
                _ = self.advance();
                break :blk "float";
            } else blk: {
                const t = try self.expect(.identifier);
                break :blk t.text;
            };

            try params.append(self.allocator, .{
                .name = name_tok.text,
                .type_name = type_name,
            });

            if (self.check(.comma)) {
                _ = self.advance();
            }
        }

        _ = try self.expect(.rparen);
        _ = try self.expect(.gives);

        // Parse the body expression
        const body_expr = try self.parseExpression() orelse {
            self.reportError(.E003);
            return error.ExpectedExpression;
        };
        const body_ptr = try self.createExpr(body_expr);

        const owned_params = try params.toOwnedSlice(self.allocator);
        return try self.parsePostfix(.{ .closure = .{
            .params = owned_params,
            .body = body_ptr,
        } });
    }

    /// Parse an interpolated string like "Hello {name}!"
    /// The raw_text is the content between quotes (without the quotes)
    fn parseInterpolatedString(self: *Parser, raw_text: []const u8) ParseError!ast.Expression {
        var parts: std.ArrayListUnmanaged(ast.InterpolatedStringPart) = .empty;
        errdefer parts.deinit(self.allocator);

        var pos: usize = 0;
        var literal_start: usize = 0;

        while (pos < raw_text.len) {
            if (raw_text[pos] == '\\' and pos + 1 < raw_text.len) {
                // Escape sequence - keep it in the literal (will be processed in IR gen)
                pos += 2;
            } else if (raw_text[pos] == '{') {
                // End current literal part (if any)
                if (pos > literal_start) {
                    try parts.append(self.allocator, .{
                        .is_expression = false,
                        .literal_value = raw_text[literal_start..pos],
                        .expr = null,
                        .format_spec = null,
                    });
                }

                // Find matching } tracking depth for nested brackets/parens/braces
                const expr_start = pos + 1;
                var expr_end = expr_start;
                var depth: usize = 1;
                var format_spec_start: ?usize = null;

                while (expr_end < raw_text.len and depth > 0) {
                    const ch = raw_text[expr_end];
                    if (ch == '\\' and expr_end + 1 < raw_text.len) {
                        // Skip escape sequence in expression (e.g., string literals)
                        expr_end += 2;
                        continue;
                    }
                    if (ch == '{' or ch == '(' or ch == '[') {
                        depth += 1;
                    } else if (ch == '}') {
                        depth -= 1;
                        if (depth == 0) break;
                    } else if (ch == ')' or ch == ']') {
                        if (depth > 1) depth -= 1;
                    } else if (ch == ':' and depth == 1 and format_spec_start == null) {
                        // Format specifier starts here (only at outermost level)
                        format_spec_start = expr_end + 1;
                    }
                    expr_end += 1;
                }

                // Extract expression text (excluding format spec if present)
                const expr_text_end = if (format_spec_start) |fs| fs - 1 else expr_end;
                const expr_text = raw_text[expr_start..expr_text_end];

                // Parse the expression using a sub-lexer and sub-parser
                var sub_lexer = Lexer.init(expr_text);
                const expr_tokens = sub_lexer.tokenize(self.allocator) catch {
                    return error.OutOfMemory;
                };
                defer self.allocator.free(expr_tokens);

                var sub_parser = Parser.init(expr_tokens, self.allocator);
                defer sub_parser.deinit();

                const expr = sub_parser.parseExpression() catch {
                    self.reportError(.E003);
                    return error.ExpectedExpression;
                } orelse {
                    self.reportError(.E003);
                    return error.ExpectedExpression;
                };

                // Extract format spec if present
                const format_spec: ?[]const u8 = if (format_spec_start) |fs|
                    raw_text[fs..expr_end]
                else
                    null;

                // Move the expression pointer to our arena
                const expr_ptr = try self.createExpr(expr);

                try parts.append(self.allocator, .{
                    .is_expression = true,
                    .literal_value = null,
                    .expr = expr_ptr,
                    .format_spec = format_spec,
                });

                pos = expr_end + 1; // Skip past }
                literal_start = pos;
            } else {
                pos += 1;
            }
        }

        // Add final literal part if any
        if (pos > literal_start) {
            try parts.append(self.allocator, .{
                .is_expression = false,
                .literal_value = raw_text[literal_start..pos],
                .expr = null,
                .format_spec = null,
            });
        }

        return try self.parsePostfix(.{ .interpolated_string = .{
            .parts = try parts.toOwnedSlice(self.allocator),
        } });
    }

    fn parsePostfix(self: *Parser, base_expr: ast.Expression) ParseError!ast.Expression {
        var expr = base_expr;
        while (true) {
            if (self.check(.dot)) {
                _ = self.advance(); // consume '.'
                const field_token = try self.expect(.identifier);
                // Check if this is a method call: .identifier(args)
                if (self.check(.lparen)) {
                    _ = self.advance(); // consume '('
                    var args: std.ArrayListUnmanaged(ast.Expression) = .empty;
                    errdefer args.deinit(self.allocator);
                    var named_args: std.ArrayListUnmanaged(ast.NamedArg) = .empty;
                    errdefer named_args.deinit(self.allocator);
                    var seen_named = false;

                    if (!self.check(.rparen)) {
                        const arg_result = try self.parseCallArgument();
                        switch (arg_result) {
                            .positional => |pos_expr| try args.append(self.allocator, pos_expr),
                            .named => |named| {
                                seen_named = true;
                                try named_args.append(self.allocator, named);
                            },
                        }
                        while (self.check(.comma)) {
                            _ = self.advance();
                            const next_arg = try self.parseCallArgument();
                            switch (next_arg) {
                                .positional => |pos_expr| {
                                    if (seen_named) {
                                        self.reportError(.E048);
                                        return error.PositionalAfterNamed;
                                    }
                                    try args.append(self.allocator, pos_expr);
                                },
                                .named => |named| {
                                    seen_named = true;
                                    try named_args.append(self.allocator, named);
                                },
                            }
                        }
                    }
                    _ = try self.expect(.rparen);
                    const base_ptr = try self.createExpr(expr);
                    expr = .{ .method_call = .{
                        .base = base_ptr,
                        .method_name = field_token.text,
                        .args = try args.toOwnedSlice(self.allocator),
                        .named_args = try named_args.toOwnedSlice(self.allocator),
                    } };
                } else {
                    const base_ptr = try self.createExpr(expr);
                    expr = .{ .field_access = .{
                        .base = base_ptr,
                        .field_name = field_token.text,
                    } };
                }
            } else if (self.check(.lbracket)) {
                _ = self.advance(); // consume '['
                const index_expr = try self.parseExpression() orelse {
                    self.reportError(.E003);
                    return error.ExpectedExpression;
                };
                _ = try self.expect(.rbracket);
                const base_ptr = try self.createExpr(expr);
                expr = .{ .index = .{
                    .base = base_ptr,
                    .index = try self.createExpr(index_expr),
                } };
            } else if (self.check(.as)) {
                // Type cast: expr as Type
                _ = self.advance(); // consume 'as'
                const target_type = try self.expectTypeName();
                const base_ptr = try self.createExpr(expr);
                expr = .{ .cast = .{
                    .expr = base_ptr,
                    .target_type = target_type,
                } };
            } else {
                break;
            }
        }
        return expr;
    }

    fn parseArrayLiteral(self: *Parser) ParseError!ast.Expression {
        _ = try self.expect(.lbracket);

        if (!self.check(.rbracket)) {
            // Parse first expression (could be array element or map key)
            const first = try self.parseExpression() orelse {
                self.reportError(.E003);
                return error.ExpectedExpression;
            };

            // Check if this is a map literal (key: value)
            if (self.check(.colon)) {
                return try self.parseMapLiteralRest(first);
            }

            // Otherwise, continue parsing as array literal
            var elements: std.ArrayListUnmanaged(ast.Expression) = .empty;
            errdefer elements.deinit(self.allocator);

            try elements.append(self.allocator, first);

            // Parse remaining elements
            while (self.check(.comma)) {
                _ = self.advance();
                const elem = try self.parseExpression() orelse {
                    self.reportError(.E003);
                    return error.ExpectedExpression;
                };
                try elements.append(self.allocator, elem);
            }

            _ = try self.expect(.rbracket);
            return try self.parsePostfix(.{ .array_literal = .{
                .elements = try elements.toOwnedSlice(self.allocator),
            } });
        }

        // Empty array literal: []
        _ = try self.expect(.rbracket);
        return try self.parsePostfix(.{ .array_literal = .{
            .elements = &.{},
        } });
    }

    /// Parse the rest of a map literal after the first key has been parsed.
    /// Called when we've seen `[key` and the next token is `:`.
    fn parseMapLiteralRest(self: *Parser, first_key: ast.Expression) ParseError!ast.Expression {
        var entries: std.ArrayListUnmanaged(ast.MapEntry) = .empty;
        errdefer entries.deinit(self.allocator);

        // Parse first entry's value (we already have the key)
        _ = try self.expect(.colon);
        const first_value = try self.parseExpression() orelse {
            self.reportError(.E003);
            return error.ExpectedExpression;
        };
        try entries.append(self.allocator, .{
            .key = try self.createExpr(first_key),
            .value = try self.createExpr(first_value),
        });

        // Parse remaining entries
        while (self.check(.comma)) {
            _ = self.advance(); // consume ','

            const key = try self.parseExpression() orelse {
                self.reportError(.E003);
                return error.ExpectedExpression;
            };
            _ = try self.expect(.colon);
            const value = try self.parseExpression() orelse {
                self.reportError(.E003);
                return error.ExpectedExpression;
            };
            try entries.append(self.allocator, .{
                .key = try self.createExpr(key),
                .value = try self.createExpr(value),
            });
        }

        _ = try self.expect(.rbracket);
        return try self.parsePostfix(.{ .map_literal = .{
            .entries = try entries.toOwnedSlice(self.allocator),
        } });
    }

    fn parseArrayType(self: *Parser) ParseError!ast.Expression {
        _ = try self.expect(.array);
        _ = try self.expect(.of);

        // Parse size expression
        const size_expr = try self.parseExpression() orelse {
            self.reportError(.E003);
            return error.ExpectedExpression;
        };
        const size_ptr = try self.createExpr(size_expr);

        // Parse element type
        const elem_type = try self.expectTypeName();

        return try self.parsePostfix(.{ .array_type = .{
            .size = size_ptr,
            .element_type = elem_type,
        } });
    }

    fn parseFieldInit(self: *Parser) ParseError!ast.FieldInit {
        const name = try self.expect(.identifier);
        _ = try self.expect(.colon);
        const value = try self.parseExpression() orelse {
            self.reportError(.E003);
            return error.ExpectedExpression;
        };
        return .{
            .name = name.text,
            .value = try self.createExpr(value),
        };
    }

    fn parseStructInit(self: *Parser, type_name: []const u8, type_args: []const []const u8) ParseError!ast.Expression {
        _ = try self.expect(.lbrace);

        var fields: std.ArrayListUnmanaged(ast.FieldInit) = .empty;
        errdefer fields.deinit(self.allocator);

        if (!self.check(.rbrace)) {
            try fields.append(self.allocator, try self.parseFieldInit());

            while (self.check(.comma)) {
                _ = self.advance();
                try fields.append(self.allocator, try self.parseFieldInit());
            }
        }

        _ = try self.expect(.rbrace);
        return try self.parsePostfix(.{ .struct_init = .{
            .type_name = type_name,
            .type_args = type_args,
            .fields = try fields.toOwnedSlice(self.allocator),
        } });
    }

    fn parseCall(self: *Parser, func_name: []const u8) ParseError!ast.Expression {
        _ = try self.expect(.lparen);
        var args: std.ArrayListUnmanaged(ast.Expression) = .empty;
        errdefer args.deinit(self.allocator);
        var named_args: std.ArrayListUnmanaged(ast.NamedArg) = .empty;
        errdefer named_args.deinit(self.allocator);
        var seen_named = false;

        // Parse argument list (positional args first, then named args)
        if (!self.check(.rparen)) {
            const arg_result = try self.parseCallArgument();
            switch (arg_result) {
                .positional => |expr| try args.append(self.allocator, expr),
                .named => |named| {
                    seen_named = true;
                    try named_args.append(self.allocator, named);
                },
            }

            while (self.check(.comma)) {
                _ = self.advance(); // consume ','
                const next_arg = try self.parseCallArgument();
                switch (next_arg) {
                    .positional => |expr| {
                        if (seen_named) {
                            self.reportError(.E048);
                            return error.PositionalAfterNamed;
                        }
                        try args.append(self.allocator, expr);
                    },
                    .named => |named| {
                        seen_named = true;
                        try named_args.append(self.allocator, named);
                    },
                }
            }
        }

        _ = try self.expect(.rparen);
        const args_slice = try args.toOwnedSlice(self.allocator);
        const named_args_slice = try named_args.toOwnedSlice(self.allocator);
        const call_expr: ast.Expression = .{ .call = .{
            .func_name = func_name,
            .args = args_slice,
            .named_args = named_args_slice,
        } };
        // Allow postfix operators (field access, indexing, cast) on call results
        return try self.parsePostfix(call_expr);
    }

    /// Result of parsing a call argument: either positional or named
    const CallArgResult = union(enum) {
        positional: ast.Expression,
        named: ast.NamedArg,
    };

    /// Parse a single call argument, detecting if it's named (identifier = expr) or positional
    fn parseCallArgument(self: *Parser) ParseError!CallArgResult {
        const start_token = self.current();

        // Check if this is a named argument: identifier = expr
        if (self.check(.identifier)) {
            if (self.peek(1)) |next_tok| {
                if (next_tok.type == .equals) {
                    // This is a named argument
                    const name_token = self.advance(); // consume identifier
                    _ = self.advance(); // consume '='
                    const value = try self.parseExpression() orelse {
                        self.reportError(.E003);
                        return error.ExpectedExpression;
                    };
                    const value_ptr = try self.createExpr(value);
                    return .{ .named = .{
                        .name = name_token.text,
                        .value = value_ptr,
                        .line = start_token.line,
                        .column = start_token.column,
                    } };
                }
            }
        }

        // Regular positional argument
        const expr = try self.parseExpression() orelse {
            self.reportError(.E003);
            return error.ExpectedExpression;
        };
        return .{ .positional = expr };
    }

    fn expect(self: *Parser, token_type: TokenType) !Token {
        if (self.check(token_type)) {
            return self.advance();
        }
        const tok = self.current();
        const expected_name = @tagName(token_type);
        const got_name = @tagName(tok.type);
        debug.parser("Parse error at {d}:{d}: expected '{s}', got '{s}'\n", .{
            tok.line,
            tok.column,
            expected_name,
            got_name,
        });
        self.reportErrorWithDetails(.E002, tok.text);
        return error.UnexpectedToken;
    }

    fn expectBlockIdentifier(self: *Parser) !Token {
        if (self.check(.char_literal)) {
            return self.advance();
        }
        self.reportError(.E042);
        return error.UnexpectedToken;
    }

    /// Parse 'end 'label'' and verify the label matches expected. Returns the line of the 'end' keyword.
    fn expectEndLabel(self: *Parser, expected: []const u8) !u32 {
        const end_token = try self.expect(.end);
        const end_label = try self.expectBlockIdentifier();
        if (!std.mem.eql(u8, expected, end_label.text)) {
            self.reportBlockIdMismatch(expected, end_label.text);
            return error.UnexpectedToken;
        }
        return end_token.line;
    }

    fn check(self: *Parser, token_type: TokenType) bool {
        if (self.isAtEnd()) return false;
        return self.tokens[self.pos].type == token_type;
    }

    fn peek(self: *Parser, offset: usize) ?Token {
        const target = self.pos + offset;
        if (target < self.tokens.len) {
            return self.tokens[target];
        }
        return null;
    }

    fn advance(self: *Parser) Token {
        if (!self.isAtEnd()) {
            const token = self.tokens[self.pos];
            self.pos += 1;
            return token;
        }
        return self.tokens[self.pos - 1];
    }

    fn isAtEnd(self: *Parser) bool {
        return self.pos >= self.tokens.len or self.tokens[self.pos].type == .eof;
    }

    fn skipNewlines(self: *Parser) void {
        while (self.check(.newline)) {
            _ = self.advance();
        }
    }

    fn parseEnumDecl(self: *Parser) ParseError!ast.EnumDecl {
        _ = try self.expect(.@"enum");
        const name_token = try self.expect(.identifier);

        // Parse optional backing type (e.g., "enum Color int" or "enum Status string")
        // Must check that the identifier is not "is" (start of conformance clause)
        // Backing type can be identifier (like "string") or keywords like "int" or "float"
        var backing_type: ?[]const u8 = null;
        if (self.check(.int) or self.check(.float)) {
            // int/float keywords are valid backing types
            backing_type = self.advance().text;
        } else if (self.check(.identifier)) {
            const next_text = self.peek(0).?.text;
            if (!std.mem.eql(u8, next_text, "is")) {
                backing_type = self.advance().text;
            }
        }

        // Parse optional interface conformances: is InterfaceName, ...
        var conformances: std.ArrayListUnmanaged(ast.InterfaceConformance) = .empty;
        errdefer conformances.deinit(self.allocator);

        if (self.check(.is)) {
            _ = self.advance();
            try conformances.append(self.allocator, try self.parseInterfaceConformance());

            while (self.check(.comma)) {
                _ = self.advance();
                try conformances.append(self.allocator, try self.parseInterfaceConformance());
            }
        }

        // Require newline after enum declaration
        _ = try self.expect(.newline);

        // Parse members and methods
        var members: std.ArrayListUnmanaged(ast.EnumMember) = .empty;
        errdefer members.deinit(self.allocator);
        var methods: std.ArrayListUnmanaged(ast.MethodDecl) = .empty;
        errdefer methods.deinit(self.allocator);

        while (!self.check(.end) and !self.isAtEnd()) {
            // Skip blank lines
            if (self.check(.newline)) {
                _ = self.advance();
                continue;
            }

            // Check if this is a method (function keyword)
            if (self.check(.function)) {
                const method = try self.parseMethodDecl();
                try methods.append(self.allocator, method);
                continue;
            }

            // Parse member name
            const member_name = try self.expect(.identifier);

            // Parse optional associated values (e.g., "value(n int)" or "pair(a int, b int)")
            var associated_values: std.ArrayListUnmanaged(ast.ParamDecl) = .empty;
            errdefer associated_values.deinit(self.allocator);

            if (self.check(.lparen)) {
                _ = self.advance(); // consume '('
                // Parse first associated value (name Type)
                if (!self.check(.rparen)) {
                    const param_name = try self.expect(.identifier);
                    const param_type = try self.parseTypeExpr();
                    try associated_values.append(self.allocator, .{
                        .name = param_name.text,
                        .type_expr = param_type,
                    });

                    // Parse additional associated values
                    while (self.check(.comma)) {
                        _ = self.advance(); // consume ','
                        const next_name = try self.expect(.identifier);
                        const next_type = try self.parseTypeExpr();
                        try associated_values.append(self.allocator, .{
                            .name = next_name.text,
                            .type_expr = next_type,
                        });
                    }
                }
                _ = try self.expect(.rparen);
            }

            // Parse optional value assignment (e.g., "red = 1" or "active = \"Active\"")
            // This is for raw value enums, not associated values
            var value: ?*const ast.Expression = null;
            if (self.check(.equals)) {
                _ = self.advance(); // consume '='
                const expr = try self.parseExpression() orelse {
                    self.reportError(.E003);
                    return error.ExpectedExpression;
                };
                value = try self.createExpr(expr);
            }

            try members.append(self.allocator, .{
                .name = member_name.text,
                .value = value,
                .associated_values = try associated_values.toOwnedSlice(self.allocator),
                .line = member_name.line,
                .column = member_name.column,
            });

            _ = try self.expect(.newline);
        }

        _ = try self.expectEndLabel(name_token.text);
        try self.expectTrailingNewline();

        return .{
            .name = name_token.text,
            .backing_type = backing_type,
            .conformances = try conformances.toOwnedSlice(self.allocator),
            .members = try members.toOwnedSlice(self.allocator),
            .methods = try methods.toOwnedSlice(self.allocator),
            .line = name_token.line,
            .column = name_token.column,
        };
    }

    fn parseInterfaceDecl(self: *Parser) ParseError!ast.InterfaceDecl {
        // Check for optional 'export' before 'interface'
        const is_export = self.check(.@"export");
        if (is_export) _ = self.advance();

        _ = try self.expect(.interface);
        const name_token = try self.expect(.identifier);

        // Parse optional generic params: uses TypeParam, ...
        const generic_params = if (self.check(.uses)) blk: {
            _ = self.advance();
            break :blk try self.parseIdentifierList();
        } else &[_][]const u8{};

        // Parse optional extends: extends Interface1, Interface2, ...
        const extends_list = if (self.check(.extends)) blk: {
            _ = self.advance();
            break :blk try self.parseIdentifierList();
        } else &[_][]const u8{};

        _ = try self.expect(.newline);

        // Parse interface methods
        var methods: std.ArrayListUnmanaged(ast.InterfaceMethod) = .empty;
        errdefer methods.deinit(self.allocator);

        while (!self.check(.end) and !self.isAtEnd()) {
            if (self.check(.newline)) {
                _ = self.advance();
                continue;
            }
            try methods.append(self.allocator, try self.parseInterfaceMethod());
        }

        _ = try self.expectEndLabel(name_token.text);
        try self.expectEndNewline();

        return .{
            .name = name_token.text,
            .is_export = is_export,
            .generic_params = generic_params,
            .extends = extends_list,
            .methods = try methods.toOwnedSlice(self.allocator),
            .line = name_token.line,
            .column = name_token.column,
        };
    }

    fn parseInterfaceMethod(self: *Parser) ParseError!ast.InterfaceMethod {
        // Check for optional 'static' before 'function'
        const is_static = self.check(.static);
        if (is_static) _ = self.advance();

        _ = try self.expect(.function);
        const name_token = try self.expect(.identifier);
        _ = try self.expect(.lparen);
        const params = try self.parseParamList();
        const return_type = try self.parseOptionalReturnType();
        const throws_type = try self.parseOptionalThrowsClause();
        _ = try self.expect(.newline);

        // Check if this has a default implementation (body before end/function)
        self.skipNewlines();
        const has_default_impl = !self.check(.end) and !self.check(.function) and !self.check(.static);
        const default_body: ?[]ast.Statement = if (has_default_impl) blk: {
            const body_result = try self.parseBodyUntilEnd(name_token.text);
            try self.expectEndNewline();
            if (self.check(.newline)) _ = self.advance();
            break :blk body_result.body;
        } else null;

        return .{
            .name = name_token.text,
            .is_static = is_static,
            .params = params,
            .return_type = return_type,
            .throws_type = throws_type,
            .has_default_impl = has_default_impl,
            .default_body = default_body,
        };
    }
};
