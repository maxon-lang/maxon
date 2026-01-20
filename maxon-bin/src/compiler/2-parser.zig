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
    /// Pending doc comment to attach to next declaration
    pending_doc_comment: ?[]const u8 = null,

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
            .pending_doc_comment = null,
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

    /// Transfer ownership of expr_ptrs to caller, preventing deinit from freeing them
    pub fn takeExprPtrs(self: *Parser) std.ArrayListUnmanaged(*ast.Expression) {
        const ptrs = self.expr_ptrs;
        self.expr_ptrs = .empty;
        return ptrs;
    }

    pub fn parse(self: *Parser) ParseError!ast.Program {
        var types: std.ArrayListUnmanaged(ast.TypeDecl) = .empty;
        errdefer types.deinit(self.allocator);
        var enums: std.ArrayListUnmanaged(ast.EnumDecl) = .empty;
        errdefer enums.deinit(self.allocator);
        var interfaces: std.ArrayListUnmanaged(ast.InterfaceDecl) = .empty;
        errdefer interfaces.deinit(self.allocator);
        var extensions: std.ArrayListUnmanaged(ast.ExtensionDecl) = .empty;
        errdefer extensions.deinit(self.allocator);
        var functions: std.ArrayListUnmanaged(ast.FunctionDecl) = .empty;
        errdefer functions.deinit(self.allocator);
        var global_constants: std.ArrayListUnmanaged(ast.GlobalConstant) = .empty;
        errdefer global_constants.deinit(self.allocator);
        var global_variables: std.ArrayListUnmanaged(ast.GlobalVariable) = .empty;
        errdefer global_variables.deinit(self.allocator);
        var type_aliases: std.ArrayListUnmanaged(ast.TypeAliasDecl) = .empty;
        errdefer type_aliases.deinit(self.allocator);

        // Skip leading newlines
        self.skipNewlines();

        while (!self.isAtEnd()) {
            // Check for type declaration (with optional 'export' prefix)
            if (self.check(.type)) {
                try types.append(self.allocator, try self.parseTypeDecl());
            } else if (self.check(.typealias)) {
                try type_aliases.append(self.allocator, try self.parseTypeAliasDecl(false));
            } else if (self.check(.interface)) {
                try interfaces.append(self.allocator, try self.parseInterfaceDecl());
            } else if (self.check(.extension)) {
                try extensions.append(self.allocator, try self.parseExtensionDecl(false));
            } else if (self.check(.let)) {
                // Top-level let declaration (non-exported)
                try global_constants.append(self.allocator, try self.parseGlobalConstant(false));
            } else if (self.check(.@"var")) {
                // Top-level var declaration (non-exported)
                try global_variables.append(self.allocator, try self.parseGlobalVariable(false));
            } else if (self.check(.@"export")) {
                // Peek ahead to see if this is 'export type', 'export interface', 'export function', 'export let', or 'export var'
                if (self.peek(1)) |next| {
                    if (next.type == .type) {
                        try types.append(self.allocator, try self.parseTypeDecl());
                    } else if (next.type == .typealias) {
                        _ = self.advance(); // skip 'export'
                        try type_aliases.append(self.allocator, try self.parseTypeAliasDecl(true));
                    } else if (next.type == .interface) {
                        try interfaces.append(self.allocator, try self.parseInterfaceDecl());
                    } else if (next.type == .extension) {
                        _ = self.advance(); // skip 'export'
                        try extensions.append(self.allocator, try self.parseExtensionDecl(true));
                    } else if (next.type == .function) {
                        _ = self.advance(); // skip 'export'
                        try functions.append(self.allocator, try self.parseFunctionWithExport(true));
                    } else if (next.type == .let) {
                        _ = self.advance(); // skip 'export'
                        try global_constants.append(self.allocator, try self.parseGlobalConstant(true));
                    } else if (next.type == .@"var") {
                        _ = self.advance(); // skip 'export'
                        try global_variables.append(self.allocator, try self.parseGlobalVariable(true));
                    } else if (next.type == .@"enum") {
                        _ = self.advance(); // skip 'export'
                        var enum_decl = try self.parseEnumDecl();
                        enum_decl.is_export = true;
                        try enums.append(self.allocator, enum_decl);
                    } else {
                        self.reportErrorWithDetails(.E002, next.text, @src());
                        return error.UnexpectedToken;
                    }
                } else {
                    self.reportErrorWithDetails(.E002, self.current().text, @src());
                    return error.UnexpectedToken;
                }
            } else if (self.check(.@"enum")) {
                const enum_decl = try self.parseEnumDecl();
                try enums.append(self.allocator, enum_decl);
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
                debug.parser("  Expected: 'type', 'enum', 'interface', 'function', 'let', or 'var' declaration\n", .{});
                self.reportErrorWithDetails(.E002, token.text, @src());
                return error.UnexpectedToken;
            }
            self.skipNewlines();
        }

        return .{
            .types = try types.toOwnedSlice(self.allocator),
            .enums = try enums.toOwnedSlice(self.allocator),
            .interfaces = try interfaces.toOwnedSlice(self.allocator),
            .extensions = try extensions.toOwnedSlice(self.allocator),
            .functions = try functions.toOwnedSlice(self.allocator),
            .global_constants = try global_constants.toOwnedSlice(self.allocator),
            .global_variables = try global_variables.toOwnedSlice(self.allocator),
            .type_aliases = try type_aliases.toOwnedSlice(self.allocator),
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

    fn reportError(self: *Parser, code: err.ErrorCode, src: std.builtin.SourceLocation) void {
        const tok = self.current();
        self.last_error = .{
            .code = code,
            .message = code.message(),
            .location = .{
                .file = self.source_file,
                .line = tok.line,
                .column = tok.column,
            },
            .caller_location = src,
        };
    }

    fn reportErrorWithMessage(self: *Parser, message: []const u8, src: std.builtin.SourceLocation) void {
        const tok = self.current();
        self.last_error = .{
            .code = .E029, // default case must be last
            .message = message,
            .location = .{
                .file = self.source_file,
                .line = tok.line,
                .column = tok.column,
            },
            .caller_location = src,
        };
    }

    fn reportErrorWithDetails(self: *Parser, code: err.ErrorCode, details: []const u8, src: std.builtin.SourceLocation) void {
        const tok = self.current();
        const formatted = std.fmt.allocPrint(self.allocator, "{s}: '{s}'", .{ code.message(), details }) catch {
            self.reportError(code, src);
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
            .caller_location = src,
        };
    }

    fn reportBlockIdMismatch(self: *Parser, expected: []const u8, got: []const u8, src: std.builtin.SourceLocation) void {
        const tok = self.current();
        const formatted = std.fmt.allocPrint(self.allocator, "{s}: expected '{s}', got '{s}'", .{ err.ErrorCode.E043.message(), expected, got }) catch {
            self.reportError(.E043, src);
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
            .caller_location = src,
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

        // Parse associated types, fields and methods
        var associated_types: std.ArrayListUnmanaged(ast.TypeAliasDecl) = .empty;
        errdefer associated_types.deinit(self.allocator);
        var fields: std.ArrayListUnmanaged(ast.FieldDecl) = .empty;
        errdefer fields.deinit(self.allocator);
        var methods: std.ArrayListUnmanaged(ast.MethodDecl) = .empty;
        errdefer methods.deinit(self.allocator);

        while (!self.check(.end) and !self.isAtEnd()) {
            if (self.check(.newline)) {
                _ = self.advance();
                continue;
            }

            // Collect doc comments for the next declaration
            if (self.check(.doc_comment)) {
                const doc_text = self.advance().text;
                self.pending_doc_comment = doc_text;
                continue;
            }

            // Check for typealias declaration
            if (self.check(.typealias)) {
                try associated_types.append(self.allocator, try self.parseTypeAliasDecl(false));
                continue;
            }

            // Check for method: export? static? function ...
            // export followed by static function is a method
            // export followed by var or let is an exported field
            // static followed by var or let is a static field
            // static followed by function is a static method
            if (self.check(.@"export")) {
                if (self.peek(1)) |next| {
                    if (next.type == .static) {
                        // export static ... - check if function or field
                        if (self.peek(2)) |after_static| {
                            if (after_static.type == .function) {
                                try methods.append(self.allocator, try self.parseMethodDecl());
                                continue;
                            }
                            // export static var/let - static field
                        }
                    } else if (next.type == .function) {
                        try methods.append(self.allocator, try self.parseMethodDecl());
                        continue;
                    }
                    // Otherwise fall through to field parsing (export var/let or export static var/let)
                }
            } else if (self.check(.static)) {
                // static followed by function is a static method
                if (self.peek(1)) |next| {
                    if (next.type == .function) {
                        try methods.append(self.allocator, try self.parseMethodDecl());
                        continue;
                    }
                    // static var/let - static field, fall through to field parsing
                }
            } else if (self.check(.function)) {
                try methods.append(self.allocator, try self.parseMethodDecl());
                continue;
            }

            // Parse field: export? static? let/var name [type] [= default_value]
            const field_is_export = self.check(.@"export");
            if (field_is_export) _ = self.advance();

            const field_is_static = self.check(.static);
            if (field_is_static) _ = self.advance();

            const is_mutable = self.check(.@"var");
            if (!is_mutable and !self.check(.let)) {
                self.reportErrorWithDetails(.E002, self.current().text, @src());
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
                    self.reportError(.E003, @src());
                    return error.ExpectedExpression;
                };
                default_value = try self.createExpr(default_expr);
                // Infer type from the default expression
                field_type = try self.inferTypeFromExpr(default_expr);
            } else {
                // Explicit type specified (no default value allowed with explicit type)
                field_type = try self.parseTypeExpr();
            }

            try fields.append(self.allocator, .{
                .name = field_name.text,
                .type_expr = field_type,
                .is_mutable = is_mutable,
                .is_export = field_is_export,
                .is_static = field_is_static,
                .default_value = default_value,
            });

            _ = try self.expect(.newline);
        }

        const end_line = try self.expectEndLabel(name_token.text);
        try self.expectEndNewline();

        return .{
            .name = name_token.text,
            .is_export = is_export,
            .generic_params = generic_params,
            .conformances = try conformances.toOwnedSlice(self.allocator),
            .associated_types = try associated_types.toOwnedSlice(self.allocator),
            .fields = try fields.toOwnedSlice(self.allocator),
            .methods = try methods.toOwnedSlice(self.allocator),
            .block = .{
                .start_line = name_token.line,
                .start_column = name_token.column,
                .end_line = end_line,
                .identifier = name_token.text,
            },
        };
    }

    /// Parse a top-level constant declaration: let NAME = expression
    fn parseGlobalConstant(self: *Parser, is_export: bool) !ast.GlobalConstant {
        const let_token = try self.expect(.let);
        const name_token = try self.expect(.identifier);
        _ = try self.expect(.equals);

        const value_expr = try self.parseExpression() orelse {
            self.reportError(.E003, @src());
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

    /// Parse a top-level mutable variable declaration: var NAME = expression
    fn parseGlobalVariable(self: *Parser, is_export: bool) !ast.GlobalVariable {
        const var_token = try self.expect(.@"var");
        const name_token = try self.expect(.identifier);
        _ = try self.expect(.equals);

        const value_expr = try self.parseExpression() orelse {
            self.reportError(.E003, @src());
            return error.ExpectedExpression;
        };

        _ = try self.expect(.newline);

        return .{
            .name = name_token.text,
            .is_export = is_export,
            .value = value_expr,
            .line = var_token.line,
            .column = var_token.column,
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
        // Capture and clear pending doc comment
        const doc_comment = self.pending_doc_comment;
        self.pending_doc_comment = null;

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
            .block = .{
                .start_line = first_name.line,
                .start_column = first_name.column,
                .end_line = body_result.end_line,
                .identifier = name,
            },
            .doc_comment = doc_comment,
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
                .line = first_name.line,
                .column = first_name.column,
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
                    .line = param_name.line,
                    .column = param_name.column,
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
                self.reportError(.E003, @src());
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
            self.reportError(.E004, @src());
            return error.ExpectedNewline;
        }
    }

    fn expectTypeName(self: *Parser) ![]const u8 {
        // Check all type keywords from keyword_map dynamically
        inline for (Lexer.keyword_map) |kw| {
            if (kw[2] == .type_keyword) {
                const token_type = kw[1];
                if (self.check(token_type)) {
                    const token = self.advance();
                    return token.text;
                }
            }
        }
        if (self.check(.Self)) {
            return self.advance().text;
        }
        if (self.check(.identifier)) {
            return self.advance().text;
        }
        self.reportErrorWithDetails(.E002, self.current().text, @src());
        return error.UnexpectedToken;
    }

    fn parseTypeExpr(self: *Parser) ParseError!ast.TypeExpr {
        // Check for function type: (params) returns ReturnType
        if (self.check(.lparen)) {
            return try self.parseFunctionTypeExpr();
        }

        // Capture location of the type expression (the first token)
        const start_token = self.current();
        const type_line = start_token.line;
        const type_column = start_token.column;

        // Parse simple type name - generic types must be instantiated via type aliases
        const type_name = try self.expectTypeName();

        return .{
            .expr = .{ .simple = type_name },
            .line = type_line,
            .column = type_column,
        };
    }

    /// Convert a TypeExpr to a monomorphized string name
    /// Simple types: "int", "String"
    /// Generic types: "Pair$String$int", "Array$int"
    fn typeExprToMonoName(self: *Parser, type_expr: ast.TypeExpr) ![]const u8 {
        switch (type_expr.expr) {
            .simple => |name| return name,
            .generic => |gen| {
                // Build monomorphized name: "Pair$String$int"
                var parts: std.ArrayListUnmanaged([]const u8) = .empty;
                defer parts.deinit(self.allocator);
                try parts.append(self.allocator, gen.base_type);
                for (gen.type_args) |arg| {
                    try parts.append(self.allocator, arg);
                }
                // Join with $
                var total_len: usize = 0;
                for (parts.items) |part| {
                    total_len += part.len;
                }
                total_len += parts.items.len - 1; // for $ separators

                const result = try self.allocator.alloc(u8, total_len);
                var pos: usize = 0;
                for (parts.items, 0..) |part, i| {
                    if (i > 0) {
                        result[pos] = '$';
                        pos += 1;
                    }
                    @memcpy(result[pos .. pos + part.len], part);
                    pos += part.len;
                }
                return result;
            },
            .function_type => {
                // Function types in closures - not typically used as parameter types
                return "(fn)";
            },
            .error_union => {
                // Error union types - not typically used as closure parameter types
                return "(error_union)";
            },
        }
    }

    /// Parse function type: (int, string) returns bool or (x int, y int) returns int
    fn parseFunctionTypeExpr(self: *Parser) ParseError!ast.TypeExpr {
        // Capture location of the function type (the opening paren)
        const start_token = self.current();
        const type_line = start_token.line;
        const type_column = start_token.column;

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

        return .{
            .expr = .{ .function_type = .{
                .param_types = try param_types.toOwnedSlice(self.allocator),
                .param_names = try param_names.toOwnedSlice(self.allocator),
                .return_type = return_type,
            } },
            .line = type_line,
            .column = type_column,
        };
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
            return .{ .name = null, .type_expr = .{
                .expr = .{ .simple = first.text },
                .line = first.line,
                .column = first.column,
            } };
        }

        // Not an identifier, parse as type directly (int, float, etc.)
        const type_expr = try self.parseTypeExpr();
        return .{ .name = null, .type_expr = type_expr };
    }

    /// Infer a TypeExpr from an expression (for field default values)
    fn inferTypeFromExpr(self: *Parser, expr: ast.Expression) ParseError!ast.TypeExpr {
        _ = self;
        // No specific location for inferred types (they're synthetic)
        return switch (expr) {
            .integer => .{ .expr = .{ .simple = "int" } },
            .float_lit => .{ .expr = .{ .simple = "float" } },
            .bool_lit => .{ .expr = .{ .simple = "bool" } },
            .string_literal, .interpolated_string => .{ .expr = .{ .simple = "String" } },
            .char_literal => .{ .expr = .{ .simple = "Character" } },
            else => {
                // For complex expressions, we cannot infer at parse time
                // Return a placeholder and let semantic analysis handle it
                return .{ .expr = .{ .simple = "int" } }; // fallback
            },
        };
    }

    fn parseFunctionWithExport(self: *Parser, is_export: bool) !ast.FunctionDecl {
        // Capture and clear pending doc comment
        const doc_comment = self.pending_doc_comment;
        self.pending_doc_comment = null;

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
            .block = .{
                .start_line = name_token.line,
                .start_column = name_token.column,
                .end_line = body_result.end_line,
                .identifier = name_token.text,
            },
            .doc_comment = doc_comment,
            .line = name_token.line,
            .column = name_token.column,
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
            _ = try self.expect(.newline);
            return decl_stmt;
        }
        if (self.check(.@"var")) {
            const decl_stmt = try self.parseVarDecl(true);
            _ = try self.expect(.newline);
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
        // Error handling: try statement (for void-returning throwing functions)
        if (self.check(.@"try")) {
            _ = self.advance(); // consume 'try'
            const operand = try self.parseUnary() orelse {
                self.reportError(.E003, @src());
                return error.ExpectedExpression;
            };
            // Check for otherwise clause
            const otherwise_clause = try self.parseOtherwiseClause();
            _ = try self.expect(.newline);
            return stmtAt(.{ .try_stmt = .{
                .expr = try self.createExpr(operand),
                .otherwise = otherwise_clause,
            } }, start_line, start_column);
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
                self.reportError(.E003, @src());
                return error.ExpectedExpression;
            };

            // Check if followed by '='
            if (self.check(.equals)) {
                _ = self.advance(); // consume '='
                const value = try self.parseExpression() orelse {
                    self.reportError(.E003, @src());
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
                self.reportErrorWithDetails(.E002, self.current().text, @src());
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
        self.reportErrorWithDetails(.E002, self.current().text, @src());
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
            // Check for common mistake: otherwise without try
            if (self.check(.otherwise)) {
                self.reportError(.E058, @src());
                return error.UnexpectedToken;
            }
            if (is_var) {
                return stmtAt(.{ .var_decl = .{ .name = name_token.text, .type_annotation = null, .value = expr } }, start_line, start_column);
            } else {
                return stmtAt(.{ .let_decl = .{ .name = name_token.text, .type_annotation = null, .value = expr } }, start_line, start_column);
            }
        }
        self.reportError(.E003, @src());
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
            self.reportError(.E004, @src());
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

    /// Parse optional else clause as ChildBlock: 'else 'label' ... end 'label'' or 'else if ...'
    /// Returns null if no else clause, otherwise returns the ChildBlock and whether trailing newline was consumed
    fn parseElseBlock(self: *Parser, allow_else_if: bool) ParseError!?ast.ChildBlock {
        if (!self.check(.@"else")) {
            return null;
        }

        const else_token = self.advance(); // consume 'else'

        // Check for else-if chain
        if (allow_else_if and self.check(.@"if")) {
            _ = self.advance(); // consume 'if'
            // Parse the nested if statement
            const else_if_stmt = try self.parseIfConditionAndBody(else_token.line, else_token.column);
            // Wrap the IfStmt in a Statement and store in ChildBlock
            const stmt_slice = try self.allocator.alloc(ast.Statement, 1);
            stmt_slice[0] = .{
                .kind = .{ .if_stmt = else_if_stmt },
                .line = else_token.line,
                .column = else_token.column,
            };
            return .{
                .role = .else_if,
                .statements = stmt_slice,
                .info = else_if_stmt.block,
            };
        }

        // Parse else block: else 'label' ... end 'label'
        const else_label_tok = try self.expectBlockIdentifier();
        _ = try self.expect(.newline);
        const else_body = try self.parseBlockBody();
        const else_end_line = try self.expectEndLabel(else_label_tok.text);

        return .{
            .role = .else_clause,
            .statements = else_body,
            .info = .{
                .start_line = else_token.line,
                .end_line = else_end_line,
                .identifier = else_label_tok.text,
            },
        };
    }

    // -------------------------------------------------------------------------
    // Control flow statements
    // -------------------------------------------------------------------------

    fn parseIfStatement(self: *Parser) ParseError!ast.Statement {
        const start_token = self.current();
        const start_line = start_token.line;
        const start_column = start_token.column;
        _ = try self.expect(.@"if");

        // Check for if-try forms: "if try" or "if let x = try"
        if (self.check(.@"try")) {
            // Boolean form: if try expr
            return stmtAt(.{ .if_stmt = try self.parseIfTryConditionAndBody(start_line, start_column, null) }, start_line, start_column);
        }

        if (self.check(.let)) {
            // Peek ahead to check for binding form: if let x = try
            if (self.peek(1)) |name_tok| {
                if (name_tok.type == .identifier) {
                    if (self.peek(2)) |eq_tok| {
                        if (eq_tok.type == .equals) {
                            if (self.peek(3)) |try_tok| {
                                if (try_tok.type == .@"try") {
                                    // Binding form: if let x = try expr
                                    _ = self.advance(); // consume 'let'
                                    const binding_name = self.advance().text; // consume identifier
                                    _ = self.advance(); // consume '='
                                    return stmtAt(.{ .if_stmt = try self.parseIfTryConditionAndBody(start_line, start_column, binding_name) }, start_line, start_column);
                                }
                            }
                        }
                    }
                }
            }
        }

        // Regular if statement
        return stmtAt(.{ .if_stmt = try self.parseIfConditionAndBody(start_line, start_column) }, start_line, start_column);
    }

    /// Parse if statement after 'if' has been consumed. Used by parseIfStatement
    /// and parseElseBlock for else-if chains.
    fn parseIfConditionAndBody(self: *Parser, start_line: u32, start_column: u32) ParseError!ast.IfStmt {
        const condition = try self.parseExpression() orelse {
            self.reportError(.E003, @src());
            return error.ExpectedExpression;
        };

        const label = try self.expectBlockIdentifier();
        _ = try self.expect(.newline);

        const body = try self.parseBlockBody();
        const end_line = try self.expectEndLabel(label.text);

        const else_block = try self.parseElseBlock(true);

        // Only expect trailing newline if we didn't chain to else-if
        if (else_block == null or else_block.?.role != .else_if) {
            try self.expectTrailingNewline();
        }

        // Build children array
        const num_children: usize = if (else_block != null) 2 else 1;
        const children = try self.allocator.alloc(ast.ChildBlock, num_children);
        children[0] = .{
            .role = .primary,
            .statements = body,
            .info = .{
                .start_line = start_line,
                .start_column = start_column,
                .end_line = end_line,
                .identifier = label.text,
            },
        };
        if (else_block) |eb| {
            children[1] = eb;
        }

        return .{
            .condition = condition,
            .block = .{
                .start_line = start_line,
                .start_column = start_column,
                .end_line = end_line,
                .identifier = label.text,
            },
            .children = children,
        };
    }

    /// Parse if-try statement after 'if try' or 'if let x = try' has been detected
    /// binding_name is non-null for the binding form (if let x = try)
    fn parseIfTryConditionAndBody(self: *Parser, start_line: u32, start_column: u32, binding_name: ?[]const u8) ParseError!ast.IfStmt {
        // Consume 'try' keyword
        _ = try self.expect(.@"try");

        // Parse the expression being tried (the function call)
        const try_expr = try self.parseUnary() orelse {
            self.reportError(.E003, @src());
            return error.ExpectedExpression;
        };

        const label = try self.expectBlockIdentifier();
        _ = try self.expect(.newline);

        const body = try self.parseBlockBody();
        const end_line = try self.expectEndLabel(label.text);

        // Parse else block with optional error binding
        const else_block = try self.parseIfTryElseBlock();

        if (else_block == null or else_block.?.role != .else_if) {
            try self.expectTrailingNewline();
        }

        // Build children array
        const num_children: usize = if (else_block != null) 2 else 1;
        const children = try self.allocator.alloc(ast.ChildBlock, num_children);
        children[0] = .{
            .role = .primary,
            .statements = body,
            .info = .{
                .start_line = start_line,
                .start_column = start_column,
                .end_line = end_line,
                .identifier = label.text,
            },
        };
        if (else_block) |eb| {
            children[1] = eb;
        }

        // Create the IfTryCondition
        const if_try_ptr = try self.allocator.create(ast.IfTryCondition);
        if_try_ptr.* = .{
            .try_expr = try self.createExpr(try_expr),
            .binding_name = binding_name,
        };

        return .{
            .condition = .{ .bool_lit = true }, // Placeholder, not used when if_try is set
            .if_try = if_try_ptr,
            .block = .{
                .start_line = start_line,
                .start_column = start_column,
                .end_line = end_line,
                .identifier = label.text,
            },
            .children = children,
        };
    }

    /// Parse optional else clause for if-try: 'else 'label' ... end 'label'' or 'else (e) 'label' ... end 'label''
    /// Returns null if no else clause
    fn parseIfTryElseBlock(self: *Parser) ParseError!?ast.ChildBlock {
        if (!self.check(.@"else")) {
            return null;
        }

        const else_token = self.advance(); // consume 'else'

        // Check for error binding: else (e) 'label'
        var error_binding: ?[]const u8 = null;
        if (self.check(.lparen)) {
            _ = self.advance(); // consume '('
            const err_name = try self.expect(.identifier);
            error_binding = err_name.text;
            _ = try self.expect(.rparen);
        }

        // Parse else block: 'label' ... end 'label'
        const else_label_tok = try self.expectBlockIdentifier();
        _ = try self.expect(.newline);
        const else_body = try self.parseBlockBody();
        const else_end_line = try self.expectEndLabel(else_label_tok.text);

        return .{
            .role = .else_clause,
            .statements = else_body,
            .info = .{
                .start_line = else_token.line,
                .end_line = else_end_line,
                .identifier = else_label_tok.text,
            },
            .error_binding = error_binding,
        };
    }

    fn parseWhileStatement(self: *Parser) ParseError!ast.Statement {
        const start_token = self.current();
        const start_line = start_token.line;
        const start_column = start_token.column;
        _ = try self.expect(.@"while");

        const condition = try self.parseExpression() orelse {
            self.reportError(.E003, @src());
            return error.ExpectedExpression;
        };

        const label = try self.expectBlockIdentifier();
        _ = try self.expect(.newline);

        const body = try self.parseBlockBody();
        const end_line = try self.parseEndAndNewline(label.text);

        const children = try self.allocator.alloc(ast.ChildBlock, 1);
        children[0] = .{
            .role = .primary,
            .statements = body,
            .info = .{
                .start_line = start_line,
                .start_column = start_column,
                .end_line = end_line,
                .identifier = label.text,
            },
        };

        return stmtAt(.{ .while_stmt = .{
            .condition = condition,
            .block = .{
                .start_line = start_line,
                .start_column = start_column,
                .end_line = end_line,
                .identifier = label.text,
            },
            .children = children,
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
            self.reportError(.E003, @src());
            return error.ExpectedExpression;
        };

        // Expect label
        const label = try self.expectBlockIdentifier();
        _ = try self.expect(.newline);

        // Parse body
        const body = try self.parseBlockBody();
        const end_line = try self.parseEndAndNewline(label.text);

        const children = try self.allocator.alloc(ast.ChildBlock, 1);
        children[0] = .{
            .role = .primary,
            .statements = body,
            .info = .{
                .start_line = start_line,
                .start_column = start_column,
                .end_line = end_line,
                .identifier = label.text,
            },
        };

        return stmtAt(.{ .for_stmt = .{
            .var_name = var_name.text,
            .iterable = iterable,
            .block = .{
                .start_line = start_line,
                .start_column = start_column,
                .end_line = end_line,
                .identifier = label.text,
            },
            .children = children,
        } }, start_line, start_column);
    }

    /// Parse throw statement: throw errorExpr
    fn parseThrowStatement(self: *Parser) ParseError!ast.Statement {
        const start_token = self.current();
        const start_line = start_token.line;
        const start_column = start_token.column;

        _ = try self.expect(.throw);
        const error_expr = try self.parseExpression() orelse {
            self.reportError(.E003, @src());
            return error.ExpectedExpression;
        };
        _ = try self.expect(.newline);

        return stmtAt(.{ .throw_stmt = .{
            .error_expr = error_expr,
        } }, start_line, start_column);
    }

    /// Parse match statement: match <expr> 'label' ... end 'label'
    fn parseMatchStatement(self: *Parser) ParseError!ast.Statement {
        const start_token = self.current();
        const start_line = start_token.line;
        const start_column = start_token.column;

        _ = try self.expect(.match);

        // Parse scrutinee expression
        const scrutinee = try self.parseExpression() orelse {
            self.reportError(.E003, @src());
            return error.ExpectedExpression;
        };

        // Parse block identifier
        const label = try self.expectBlockIdentifier();
        _ = try self.expect(.newline);

        // Parse cases as ChildBlocks
        var children: std.ArrayListUnmanaged(ast.ChildBlock) = .empty;
        errdefer children.deinit(self.allocator);
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
                const stmt_slice = try self.allocator.alloc(ast.Statement, 1);
                stmt_slice[0] = result.stmt;
                try children.append(self.allocator, .{
                    .role = .default_case,
                    .statements = stmt_slice,
                });
                saw_default = true;
                continue;
            }

            // Error if we see a pattern after default
            if (saw_default) {
                self.reportErrorWithMessage("'default' case must be the last case in match", @src());
                return error.UnexpectedToken;
            }

            // Parse patterns (may have multiple via 'or')
            var patterns: std.ArrayListUnmanaged(ast.Expression) = .empty;
            errdefer patterns.deinit(self.allocator);
            var pattern_bindings: std.ArrayListUnmanaged(?ast.PatternBinding) = .empty;
            errdefer pattern_bindings.deinit(self.allocator);

            // Capture location of first pattern for error reporting
            const pattern_token = self.current();
            const pattern_line = pattern_token.line;
            const pattern_column = pattern_token.column;

            // Parse first pattern (may be identifier with bindings like `value(n)`)
            const first_result = try self.parseMatchPattern();
            try patterns.append(self.allocator, first_result.pattern);
            try pattern_bindings.append(self.allocator, first_result.binding);

            while (self.check(.@"or")) {
                _ = self.advance(); // consume 'or'
                const pattern_result = try self.parseMatchPattern();
                try patterns.append(self.allocator, pattern_result.pattern);
                try pattern_bindings.append(self.allocator, pattern_result.binding);
            }

            _ = try self.expect(.then);

            // Parse single statement (handles newline internally)
            const result = try self.parseMatchCaseBody();
            const stmt_slice = try self.allocator.alloc(ast.Statement, 1);
            stmt_slice[0] = result.stmt;

            try children.append(self.allocator, .{
                .role = .match_case,
                .statements = stmt_slice,
                .match_patterns = try patterns.toOwnedSlice(self.allocator),
                .pattern_bindings = try pattern_bindings.toOwnedSlice(self.allocator),
                .has_fallthrough = result.has_fallthrough,
                .pattern_line = pattern_line,
                .pattern_column = pattern_column,
            });
        }

        const end_line = try self.expectEndLabel(label.text);
        try self.expectTrailingNewline();

        return stmtAt(.{ .match_stmt = .{
            .scrutinee = scrutinee,
            .block = .{
                .start_line = start_line,
                .start_column = start_column,
                .end_line = end_line,
                .identifier = label.text,
            },
            .children = try children.toOwnedSlice(self.allocator),
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
    /// Also handles range patterns: `1..=5`, `1..<5`, `1..`, `..=5`, `..<5`, `..`
    fn parseMatchPattern(self: *Parser) ParseError!MatchPatternResult {
        // Check for prefix range patterns: ..=X, ..<X, ..
        if (self.check(.dot_dot_equals)) {
            _ = self.advance(); // consume '..='
            // Parse upper bound
            const upper = try self.parseRangePatternBound() orelse {
                self.reportError(.E003, @src());
                return error.ExpectedExpression;
            };
            return .{
                .pattern = .{ .range_pattern = .{
                    .lower = null,
                    .upper = try self.createExpr(upper),
                    .inclusive = true,
                } },
                .binding = null,
            };
        }
        if (self.check(.dot_dot_less)) {
            _ = self.advance(); // consume '..<'
            // Parse upper bound
            const upper = try self.parseRangePatternBound() orelse {
                self.reportError(.E003, @src());
                return error.ExpectedExpression;
            };
            return .{
                .pattern = .{ .range_pattern = .{
                    .lower = null,
                    .upper = try self.createExpr(upper),
                    .inclusive = false,
                } },
                .binding = null,
            };
        }
        if (self.check(.dot_dot)) {
            _ = self.advance(); // consume '..'
            // This is the wildcard range pattern '..' (matches everything)
            return .{
                .pattern = .{
                    .range_pattern = .{
                        .lower = null,
                        .upper = null,
                        .inclusive = true, // doesn't matter for wildcard
                    },
                },
                .binding = null,
            };
        }

        // Check for identifier possibly followed by bindings or field access
        if (self.check(.identifier)) {
            const ident_token = self.advance();

            // Check if followed by '.' for field access (e.g., Color.red) OR range operator
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

            // Check if followed by range operator
            if (self.check(.dot_dot_equals) or self.check(.dot_dot_less) or self.check(.dot_dot)) {
                return self.parseRangeSuffix(.{ .identifier = ident_token.text });
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

        // Fall back to regular expression parsing for other patterns (integers, chars, etc.)
        const pattern = try self.parseRangePatternBound() orelse {
            self.reportError(.E003, @src());
            return error.ExpectedExpression;
        };

        // Check if followed by range operator
        if (self.check(.dot_dot_equals) or self.check(.dot_dot_less) or self.check(.dot_dot)) {
            return self.parseRangeSuffix(pattern);
        }

        return .{
            .pattern = pattern,
            .binding = null,
        };
    }

    /// Parse a simple expression that can be a range bound (integer, char, float, negation, identifier)
    fn parseRangePatternBound(self: *Parser) ParseError!?ast.Expression {
        // Handle negation for negative numbers
        if (self.check(.minus)) {
            _ = self.advance();
            const operand = try self.parseRangePatternBound() orelse {
                return null;
            };
            return .{ .unary = .{
                .op = .negate,
                .operand = try self.createExpr(operand),
            } };
        }
        if (self.check(.integer)) {
            const token = self.advance();
            const value = std.fmt.parseInt(i64, token.text, 0) catch {
                self.reportError(.E001, @src());
                return error.InvalidNumber;
            };
            return .{ .integer = value };
        }
        if (self.check(.float_literal)) {
            const token = self.advance();
            const value = std.fmt.parseFloat(f64, token.text) catch {
                self.reportError(.E001, @src());
                return error.InvalidNumber;
            };
            return .{ .float_lit = value };
        }
        if (self.check(.char_literal)) {
            const token = self.advance();
            return .{ .char_literal = token.text };
        }
        // Fallback to parseLogicalAnd for more complex expressions
        return try self.parseLogicalAnd();
    }

    /// Parse the suffix part of a range pattern after the lower bound has been parsed
    /// Handles: X..=Y, X..<Y, X..
    fn parseRangeSuffix(self: *Parser, lower: ast.Expression) ParseError!MatchPatternResult {
        const lower_ptr = try self.createExpr(lower);

        if (self.check(.dot_dot_equals)) {
            _ = self.advance(); // consume '..='
            // Parse upper bound
            const upper = try self.parseRangePatternBound() orelse {
                self.reportError(.E003, @src());
                return error.ExpectedExpression;
            };
            return .{
                .pattern = .{ .range_pattern = .{
                    .lower = lower_ptr,
                    .upper = try self.createExpr(upper),
                    .inclusive = true,
                } },
                .binding = null,
            };
        }
        if (self.check(.dot_dot_less)) {
            _ = self.advance(); // consume '..<'
            // Parse upper bound
            const upper = try self.parseRangePatternBound() orelse {
                self.reportError(.E003, @src());
                return error.ExpectedExpression;
            };
            return .{
                .pattern = .{ .range_pattern = .{
                    .lower = lower_ptr,
                    .upper = try self.createExpr(upper),
                    .inclusive = false,
                } },
                .binding = null,
            };
        }
        if (self.check(.dot_dot)) {
            _ = self.advance(); // consume '..'
            // Open-ended upper bound: X..
            return .{
                .pattern = .{
                    .range_pattern = .{
                        .lower = lower_ptr,
                        .upper = null,
                        .inclusive = true, // doesn't matter for open-ended
                    },
                },
                .binding = null,
            };
        }

        // This shouldn't happen if called correctly
        self.reportError(.E003, @src());
        return error.ExpectedExpression;
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
                self.reportError(.E003, @src());
                return error.ExpectedExpression;
            };

            // Check if followed by '='
            if (self.check(.equals)) {
                _ = self.advance(); // consume '='
                const value = try self.parseExpression() orelse {
                    self.reportError(.E003, @src());
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
                self.reportErrorWithDetails(.E002, self.current().text, @src());
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

        self.reportErrorWithDetails(.E002, self.current().text, @src());
        return error.UnexpectedToken;
    }

    /// Parse match expression: match <expr> 'label' ... end 'label'
    fn parseMatchExpression(self: *Parser) ParseError!ast.Expression {
        const start_line = self.current().line;
        _ = try self.expect(.match);

        // Parse scrutinee expression
        const scrutinee = try self.parseExpression() orelse {
            self.reportError(.E003, @src());
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
                    self.reportError(.E003, @src());
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
                _ = self.advance(); // consume 'or'
                const pattern_result = try self.parseMatchPattern();
                try patterns.append(self.allocator, pattern_result.pattern);
                try pattern_bindings.append(self.allocator, pattern_result.binding);
            }

            _ = try self.expect(.gives);

            // Parse result expression
            const result = try self.parseExpression() orelse {
                self.reportError(.E003, @src());
                return error.ExpectedExpression;
            };
            _ = try self.expect(.newline);

            try cases.append(self.allocator, .{
                .patterns = try patterns.toOwnedSlice(self.allocator),
                .pattern_bindings = try pattern_bindings.toOwnedSlice(self.allocator),
                .result = result,
            });
        }

        const end_line = self.current().line;
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
            .start_line = start_line,
            .end_line = end_line,
        } });
    }

    fn parseExpression(self: *Parser) ParseError!?ast.Expression {
        return try self.parseLogicalOr();
    }

    fn parseLogicalOr(self: *Parser) ParseError!?ast.Expression {
        var left = try self.parseLogicalAnd() orelse return null;

        while (self.check(.@"or")) {
            _ = self.advance();
            const right = try self.parseLogicalAnd() orelse {
                self.reportError(.E003, @src());
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
                self.reportError(.E003, @src());
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
                self.reportError(.E003, @src());
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
                self.reportError(.E003, @src());
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
                self.reportError(.E003, @src());
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
                self.reportError(.E003, @src());
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
                self.reportError(.E003, @src());
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
            // Fold negative numeric literals directly
            if (self.check(.integer)) {
                const token = self.advance();
                const value = std.fmt.parseInt(i64, token.text, 0) catch {
                    self.reportError(.E001, @src());
                    return error.InvalidNumber;
                };
                return try self.parsePostfix(.{ .integer = -value });
            }
            if (self.check(.float_literal)) {
                const token = self.advance();
                const value = std.fmt.parseFloat(f64, token.text) catch {
                    self.reportError(.E001, @src());
                    return error.InvalidNumber;
                };
                return try self.parsePostfix(.{ .float_lit = -value });
            }
            const operand = try self.parseUnary() orelse {
                self.reportError(.E003, @src());
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
                self.reportError(.E003, @src());
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
            const operand = try self.parseUnary() orelse {
                self.reportError(.E003, @src());
                return error.ExpectedExpression;
            };

            // Check for otherwise clause
            const otherwise_clause = try self.parseOtherwiseClause();

            return .{ .try_expr = .{
                .expr = try self.createExpr(operand),
                .otherwise = otherwise_clause,
            } };
        }
        return self.parsePrimary();
    }

    /// Parse optional otherwise clause for try expressions:
    /// - `try expr otherwise ignore` - discard mode
    /// - `try expr otherwise defaultExpr` - single expression default
    /// - `try expr otherwise 'label' body end 'label'` - block handler
    /// - `try expr otherwise (err) 'label' body end 'label'` - block with error binding
    fn parseOtherwiseClause(self: *Parser) ParseError!?*const ast.OtherwiseClause {
        if (!self.check(.otherwise)) {
            return null;
        }
        _ = self.advance(); // consume 'otherwise'

        const clause = try self.allocator.create(ast.OtherwiseClause);

        // Check for 'ignore' mode
        if (self.check(.ignore)) {
            _ = self.advance();
            clause.* = .{
                .mode = .ignore,
            };
            return clause;
        }

        // Check for block with optional error binding: (err) 'label' or just 'label'
        if (self.check(.lparen)) {
            // Block with error binding: (err) 'label' ... end 'label'
            _ = self.advance(); // consume '('
            const err_name = try self.expect(.identifier);
            _ = try self.expect(.rparen);

            const label = try self.expectBlockIdentifier();
            _ = try self.expect(.newline);

            const body = try self.parseBlockBody();
            const end_line = try self.expectEndLabel(label.text);

            clause.* = .{
                .mode = .block_with_err,
                .error_binding = err_name.text,
                .block = .{
                    .start_line = label.line,
                    .start_column = label.column,
                    .end_line = end_line,
                    .identifier = label.text,
                },
                .body = body,
            };
            return clause;
        }

        if (self.check(.char_literal)) {
            // Block without error binding: 'label' ... end 'label'
            const label = try self.expectBlockIdentifier();
            _ = try self.expect(.newline);

            const body = try self.parseBlockBody();
            const end_line = try self.expectEndLabel(label.text);

            clause.* = .{
                .mode = .block,
                .block = .{
                    .start_line = label.line,
                    .start_column = label.column,
                    .end_line = end_line,
                    .identifier = label.text,
                },
                .body = body,
            };
            return clause;
        }

        // Default expression mode: otherwise defaultExpr
        const default_expr = try self.parseExpression() orelse {
            self.reportError(.E003, @src());
            return error.ExpectedExpression;
        };
        const expr_ptr = try self.createExpr(default_expr);

        clause.* = .{
            .mode = .default_expr,
            .default_expr = expr_ptr,
        };
        return clause;
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
                self.reportError(.E001, @src());
                return error.InvalidNumber;
            };
            return try self.parsePostfix(.{ .integer = value });
        }
        if (self.check(.float_literal)) {
            const token = self.advance();
            const value = std.fmt.parseFloat(f64, token.text) catch {
                self.reportError(.E001, @src());
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
        // Anonymous struct literal: {field: value, ...}
        // Pattern: { identifier : ... - type inferred from context
        if (self.check(.lbrace)) {
            if (self.isAnonymousStructLiteral()) {
                return try self.parseAnonymousStructInit();
            }
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

            // Inline generic syntax "TypeName of T{...}" is no longer allowed
            // Use type aliases instead: "type MyArray is Array with T"
            if (self.check(.of)) {
                self.reportErrorWithDetails(.E002, "of", @src());
                return error.UnexpectedToken;
            }

            // Check for "TypeName from ..." syntax:
            // - "TypeName from [...]" for InitableFromArrayLiteral types
            // - "TypeName from K to V{}" for two-argument generics like Map
            if (self.check(.from)) {
                _ = self.advance(); // consume 'from'

                // Check for array literal: TypeName from [1, 2, 3]
                if (self.check(.lbracket)) {
                    const arr_lit = try self.parseArrayLiteral();
                    const arr_lit_ptr = try self.createExpr(arr_lit);
                    return try self.parsePostfix(.{
                        .init_from_array = .{
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
                self.reportErrorWithDetails(.E002, self.current().text, @src());
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
                self.reportError(.E003, @src());
                return error.ExpectedExpression;
            };
            _ = try self.expect(.rparen);
            return try self.parsePostfix(expr);
        }
        return null;
    }

    /// Check if the current position is the start of a closure: (name [type], ...) gives
    /// Type is optional - can be `(x, y) gives` or `(x int, y int) gives`
    fn isClosureStart(self: *Parser) bool {
        // We're at '('
        // Closure format: (identifier [type] [, identifier [type] ...]) gives
        var lookahead: usize = 1;

        // Skip the lparen
        const after_lparen = self.peek(lookahead) orelse return false;
        if (after_lparen.type != .identifier) return false;
        lookahead += 1;

        // After identifier, we can have:
        // - type expression (identifier, int, float, or complex type like "Pair of String int")
        // - comma (no type, move to next param)
        // - rparen (no type, end of params)
        const after_id = self.peek(lookahead) orelse return false;
        if (after_id.type == .identifier or after_id.type == .int or after_id.type == .float) {
            // Type present - skip the entire type expression
            lookahead = self.skipTypeExprLookahead(lookahead);
        }

        // Now we can have: ) gives  OR  , identifier [type] ...
        // Skip params until we hit )
        while (true) {
            const tok = self.peek(lookahead) orelse return false;
            if (tok.type == .rparen) {
                lookahead += 1;
                break;
            }
            if (tok.type == .comma) {
                lookahead += 1;
                // Expect identifier [type]
                const id_tok = self.peek(lookahead) orelse return false;
                if (id_tok.type != .identifier) return false;
                lookahead += 1;
                // Check for optional type
                const after_param = self.peek(lookahead) orelse return false;
                if (after_param.type == .identifier or after_param.type == .int or after_param.type == .float) {
                    // Type present - skip the entire type expression
                    lookahead = self.skipTypeExprLookahead(lookahead);
                }
                // Otherwise, no type - continue
            } else {
                return false;
            }
        }

        // Check for 'gives' keyword
        const gives_tok = self.peek(lookahead) orelse return false;
        return gives_tok.type == .gives;
    }

    /// Skip a type expression during lookahead for closure detection
    /// Returns the new lookahead position after the type expression
    /// Handles: simple types (int, float, bool), identifiers, generic types (Pair of A B)
    fn skipTypeExprLookahead(self: *Parser, start: usize) usize {
        var pos = start;

        // Skip the base type (int, float, identifier)
        const base = self.peek(pos) orelse return pos;
        if (base.type != .identifier and base.type != .int and base.type != .float) {
            return pos;
        }
        pos += 1;

        // Check for generic type: "of" followed by type arguments
        const maybe_of = self.peek(pos) orelse return pos;
        if (maybe_of.type == .of) {
            pos += 1;
            // Skip type arguments until we hit comma or rparen
            while (true) {
                const tok = self.peek(pos) orelse return pos;
                if (tok.type == .comma or tok.type == .rparen) {
                    break;
                }
                // Skip this type argument token
                pos += 1;
            }
        }

        return pos;
    }

    /// Parse a closure: (name [type], ...) gives expr
    /// Type is optional - if omitted, will be inferred from context
    fn parseClosure(self: *Parser) ParseError!ast.Expression {
        _ = self.advance(); // consume '('

        var params: std.ArrayListUnmanaged(ast.ClosureParam) = .empty;
        errdefer params.deinit(self.allocator);

        // Parse params: identifier [type], identifier [type], ...
        while (!self.check(.rparen)) {
            const name_tok = try self.expect(.identifier);
            // Type is optional - if next is comma or rparen, no type specified
            var type_line: u32 = 0;
            var type_column: u32 = 0;
            const type_name: ?[]const u8 = if (self.check(.comma) or self.check(.rparen)) blk: {
                break :blk null; // Type will be inferred from context
            } else blk: {
                // Capture location before parsing type for error reporting
                type_line = self.current().line;
                type_column = self.current().column;
                // Parse full type expression (handles "int", "Pair of String int", etc.)
                const type_expr = try self.parseTypeExpr();
                break :blk try self.typeExprToMonoName(type_expr);
            };

            try params.append(self.allocator, .{
                .name = name_tok.text,
                .type_name = type_name,
                .type_line = type_line,
                .type_column = type_column,
            });

            if (self.check(.comma)) {
                _ = self.advance();
            }
        }

        _ = try self.expect(.rparen);
        _ = try self.expect(.gives);

        // Parse the body expression
        const body_expr = try self.parseExpression() orelse {
            self.reportError(.E003, @src());
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
                    self.reportError(.E003, @src());
                    return error.ExpectedExpression;
                } orelse {
                    self.reportError(.E003, @src());
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
                const field_token = self.advance();
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
                                        self.reportError(.E048, @src());
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
                        .method_line = field_token.line,
                        .method_column = field_token.column,
                    } };
                } else {
                    const base_ptr = try self.createExpr(expr);
                    expr = .{ .field_access = .{
                        .base = base_ptr,
                        .field_name = field_token.text,
                    } };
                }
            } else if (self.check(.lbracket)) {
                // Bracket indexing is not supported - use .get(i) and .set(i, value: v)
                self.reportErrorWithDetails(.E053, "Use .get(i) for reading and .set(i, value: v) for writing", @src());
                return error.UnexpectedToken;
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
        const start_line = self.current().line;
        _ = try self.expect(.lbracket);
        self.skipNewlines();

        if (!self.check(.rbracket)) {
            // Parse first expression (could be array element or map key)
            const first = try self.parseExpression() orelse {
                self.reportError(.E003, @src());
                return error.ExpectedExpression;
            };

            // Check if this is a map literal (key: value)
            if (self.check(.colon)) {
                return try self.parseMapLiteralRest(first, start_line);
            }

            // Otherwise, continue parsing as array literal
            var elements: std.ArrayListUnmanaged(ast.Expression) = .empty;
            errdefer elements.deinit(self.allocator);

            try elements.append(self.allocator, first);

            // Parse remaining elements
            while (self.check(.comma)) {
                _ = self.advance();
                const elem = try self.parseExpression() orelse {
                    self.reportError(.E003, @src());
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
    fn parseMapLiteralRest(self: *Parser, first_key: ast.Expression, start_line: u32) ParseError!ast.Expression {
        var entries: std.ArrayListUnmanaged(ast.MapEntry) = .empty;
        errdefer entries.deinit(self.allocator);

        // Parse first entry's value (we already have the key)
        _ = try self.expect(.colon);
        const first_value = try self.parseExpression() orelse {
            self.reportError(.E003, @src());
            return error.ExpectedExpression;
        };
        try entries.append(self.allocator, .{
            .key = try self.createExpr(first_key),
            .value = try self.createExpr(first_value),
        });
        self.skipNewlines();

        // Parse remaining entries
        while (self.check(.comma)) {
            _ = self.advance(); // consume ','
            self.skipNewlines();

            const key = try self.parseExpression() orelse {
                self.reportError(.E003, @src());
                return error.ExpectedExpression;
            };
            _ = try self.expect(.colon);
            const value = try self.parseExpression() orelse {
                self.reportError(.E003, @src());
                return error.ExpectedExpression;
            };
            try entries.append(self.allocator, .{
                .key = try self.createExpr(key),
                .value = try self.createExpr(value),
            });
            self.skipNewlines();
        }

        const end_line = self.current().line;
        _ = try self.expect(.rbracket);
        return try self.parsePostfix(.{ .map_literal = .{
            .entries = try entries.toOwnedSlice(self.allocator),
            .start_line = start_line,
            .end_line = end_line,
        } });
    }

    fn parseFieldInit(self: *Parser) ParseError!ast.FieldInit {
        const name = try self.expect(.identifier);
        _ = try self.expect(.colon);
        const value = try self.parseExpression() orelse {
            self.reportError(.E003, @src());
            return error.ExpectedExpression;
        };
        return .{
            .name = name.text,
            .value = try self.createExpr(value),
        };
    }

    fn parseStructInit(self: *Parser, type_name: []const u8, type_args: []const []const u8) ParseError!ast.Expression {
        _ = try self.expect(.lbrace);
        self.skipNewlines();

        var fields: std.ArrayListUnmanaged(ast.FieldInit) = .empty;
        errdefer fields.deinit(self.allocator);

        if (!self.check(.rbrace)) {
            try fields.append(self.allocator, try self.parseFieldInit());
            self.skipNewlines();

            while (self.check(.comma)) {
                _ = self.advance();
                self.skipNewlines();
                try fields.append(self.allocator, try self.parseFieldInit());
                self.skipNewlines();
            }
        }

        _ = try self.expect(.rbrace);
        return try self.parsePostfix(.{ .struct_init = .{
            .type_name = type_name,
            .type_args = type_args,
            .fields = try fields.toOwnedSlice(self.allocator),
        } });
    }

    /// Check if the current position starts an anonymous struct literal: { identifier : ...
    /// This is used to disambiguate between blocks and anonymous struct literals
    fn isAnonymousStructLiteral(self: *Parser) bool {
        // Pattern: { identifier :
        // We're at '{'
        const peek1 = self.peek(1) orelse return false;
        if (peek1.type != .identifier) return false;
        const peek2 = self.peek(2) orelse return false;
        return peek2.type == .colon;
    }

    /// Parse an anonymous struct literal: {field: value, ...}
    /// Type will be inferred from context
    fn parseAnonymousStructInit(self: *Parser) ParseError!ast.Expression {
        _ = try self.expect(.lbrace);
        self.skipNewlines();

        var fields: std.ArrayListUnmanaged(ast.FieldInit) = .empty;
        errdefer fields.deinit(self.allocator);

        if (!self.check(.rbrace)) {
            try fields.append(self.allocator, try self.parseFieldInit());
            self.skipNewlines();

            while (self.check(.comma)) {
                _ = self.advance();
                self.skipNewlines();
                try fields.append(self.allocator, try self.parseFieldInit());
                self.skipNewlines();
            }
        }

        _ = try self.expect(.rbrace);
        return try self.parsePostfix(.{
            .struct_init = .{
                .type_name = null, // Anonymous - type inferred from context
                .type_args = &.{},
                .fields = try fields.toOwnedSlice(self.allocator),
            },
        });
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
                            self.reportError(.E048, @src());
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

    /// Parse a single call argument, detecting if it's named (identifier: expr) or positional
    fn parseCallArgument(self: *Parser) ParseError!CallArgResult {
        const start_token = self.current();

        // Check if this is a named argument: identifier: expr
        if (self.check(.identifier)) {
            if (self.peek(1)) |next_tok| {
                if (next_tok.type == .colon) {
                    // This is a named argument
                    const name_token = self.advance(); // consume identifier
                    _ = self.advance(); // consume ':'
                    const value = try self.parseExpression() orelse {
                        self.reportError(.E003, @src());
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
            self.reportError(.E003, @src());
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
        self.reportErrorWithDetails(.E002, tok.text, @src());
        return error.UnexpectedToken;
    }

    fn expectBlockIdentifier(self: *Parser) !Token {
        if (self.check(.char_literal)) {
            return self.advance();
        }
        self.reportError(.E042, @src());
        return error.UnexpectedToken;
    }

    /// Parse 'end 'label'' and verify the label matches expected. Returns the line of the 'end' keyword.
    fn expectEndLabel(self: *Parser, expected: []const u8) !u32 {
        const end_token = try self.expect(.end);
        const end_label = try self.expectBlockIdentifier();
        if (!std.mem.eql(u8, expected, end_label.text)) {
            self.reportBlockIdMismatch(expected, end_label.text, @src());
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

    /// Check if we're at the end of a block: "end 'label'"
    fn isAtEndOfBlock(self: *Parser) bool {
        if (!self.check(.end)) return false;
        // "end" followed by a block label (char_literal) is end of block
        if (self.peek(1)) |next_token| {
            return next_token.type == .char_literal;
        }
        return false;
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
        while (self.check(.newline) or self.check(.doc_comment)) {
            if (self.check(.doc_comment)) {
                // Store doc comment for the next declaration
                const doc_text = self.advance().text;
                self.pending_doc_comment = doc_text;
            } else {
                _ = self.advance();
            }
        }
    }

    fn parseEnumDecl(self: *Parser) ParseError!ast.EnumDecl {
        // The caller already handled 'export' token if present
        const is_export = false; // Enums default to private

        _ = try self.expect(.@"enum");
        const name_token = try self.expect(.identifier);

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

        // Check if we're at the end of a block: "end 'label'"
        // This is needed because 'end' can also be an enum member name
        while (!self.isAtEndOfBlock() and !self.isAtEnd()) {
            // Skip blank lines
            if (self.check(.newline)) {
                _ = self.advance();
                continue;
            }

            // Collect doc comments for the next declaration
            if (self.check(.doc_comment)) {
                self.pending_doc_comment = self.advance().text;
                continue;
            }

            // Check if this is a method (function keyword followed by identifier)
            // "function foo()" is a method, "function" or "function(...)" is an enum member
            if (self.check(.function)) {
                if (self.peek(1)) |next_token| {
                    if (next_token.type == .identifier) {
                        const method = try self.parseMethodDecl();
                        try methods.append(self.allocator, method);
                        continue;
                    }
                }
                // Fall through to parse "function" as an enum member name
            }

            // Parse member name - can be identifier, string literal, char literal, or keyword
            // Inside an enum block, keywords are valid member names
            var member_name: []const u8 = undefined;
            var member_line: u32 = 0;
            var member_column: u32 = 0;
            var name_is_string_literal = false;
            var name_is_char_literal = false;

            if (self.check(.string_literal)) {
                const token = self.advance();
                member_name = token.text;
                member_line = token.line;
                member_column = token.column;
                name_is_string_literal = true;
            } else if (self.check(.char_literal)) {
                const token = self.advance();
                member_name = token.text;
                member_line = token.line;
                member_column = token.column;
                name_is_char_literal = true;
            } else {
                // Accept any token with text as enum member name (identifier or keyword)
                const token = self.current();
                if (token.text.len > 0 and token.type != .newline and token.type != .eof) {
                    _ = self.advance();
                    member_name = token.text;
                    member_line = token.line;
                    member_column = token.column;
                } else {
                    const expected_token = try self.expect(.identifier);
                    member_name = expected_token.text;
                    member_line = expected_token.line;
                    member_column = expected_token.column;
                }
            }

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
                    self.reportError(.E003, @src());
                    return error.ExpectedExpression;
                };
                value = try self.createExpr(expr);
            }

            try members.append(self.allocator, .{
                .name = member_name,
                .value = value,
                .associated_values = try associated_values.toOwnedSlice(self.allocator),
                .line = member_line,
                .column = member_column,
                .name_is_string_literal = name_is_string_literal,
                .name_is_char_literal = name_is_char_literal,
            });

            _ = try self.expect(.newline);
        }

        const end_line = try self.expectEndLabel(name_token.text);
        try self.expectTrailingNewline();

        return .{
            .name = name_token.text,
            .is_export = is_export,
            .conformances = try conformances.toOwnedSlice(self.allocator),
            .members = try members.toOwnedSlice(self.allocator),
            .methods = try methods.toOwnedSlice(self.allocator),
            .block = .{
                .start_line = name_token.line,
                .start_column = name_token.column,
                .end_line = end_line,
                .identifier = name_token.text,
            },
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

        // Parse interface body: associated types and methods
        var associated_types: std.ArrayListUnmanaged(ast.TypeAliasDecl) = .empty;
        errdefer associated_types.deinit(self.allocator);
        var methods: std.ArrayListUnmanaged(ast.InterfaceMethod) = .empty;
        errdefer methods.deinit(self.allocator);

        while (!self.check(.end) and !self.isAtEnd()) {
            if (self.check(.newline)) {
                _ = self.advance();
                continue;
            }

            // Check for typealias declaration
            if (self.check(.typealias)) {
                try associated_types.append(self.allocator, try self.parseTypeAliasDecl(false));
            } else {
                try methods.append(self.allocator, try self.parseInterfaceMethod());
            }
        }

        const end_line = try self.expectEndLabel(name_token.text);
        try self.expectEndNewline();

        return .{
            .name = name_token.text,
            .is_export = is_export,
            .generic_params = generic_params,
            .extends = extends_list,
            .associated_types = try associated_types.toOwnedSlice(self.allocator),
            .methods = try methods.toOwnedSlice(self.allocator),
            .block = .{
                .start_line = name_token.line,
                .start_column = name_token.column,
                .end_line = end_line,
                .identifier = name_token.text,
            },
        };
    }

    /// Parse a type alias declaration: typealias Name is GenericType with (TypeArg1, TypeArg2)
    fn parseTypeAliasDecl(self: *Parser, is_export: bool) ParseError!ast.TypeAliasDecl {
        const alias_token = try self.expect(.typealias);
        const name_token = try self.expect(.identifier);
        _ = try self.expect(.is);
        const base_type_token = try self.expect(.identifier);

        // Parse: with TypeArg or with (TypeArg1, TypeArg2)
        _ = try self.expect(.with);

        var type_args: std.ArrayListUnmanaged([]const u8) = .empty;
        errdefer type_args.deinit(self.allocator);

        // Check for parenthesized type args: with (X, Y)
        if (self.check(.lparen)) {
            _ = self.advance(); // consume '('
            const first_arg = try self.expectTypeName();
            try type_args.append(self.allocator, first_arg);

            while (self.check(.comma)) {
                _ = self.advance(); // consume comma
                const arg = try self.expectTypeName();
                try type_args.append(self.allocator, arg);
            }

            _ = try self.expect(.rparen); // consume ')'
        } else {
            // Single type arg: with TypeArg
            const arg = try self.expectTypeName();
            try type_args.append(self.allocator, arg);
        }

        _ = try self.expect(.newline);

        return .{
            .name = name_token.text,
            .base_type = base_type_token.text,
            .type_args = try type_args.toOwnedSlice(self.allocator),
            .is_export = is_export,
            .line = alias_token.line,
            .column = alias_token.column,
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

        return .{
            .name = name_token.text,
            .is_static = is_static,
            .params = params,
            .return_type = return_type,
            .throws_type = throws_type,
        };
    }

    fn parseExtensionDecl(self: *Parser, is_export: bool) ParseError!ast.ExtensionDecl {
        _ = try self.expect(.extension);
        const name_token = try self.expect(.identifier);
        _ = try self.expect(.newline);

        // Parse associated types and extension methods
        var associated_types: std.ArrayListUnmanaged(ast.TypeAliasDecl) = .empty;
        errdefer associated_types.deinit(self.allocator);
        var methods: std.ArrayListUnmanaged(ast.ExtensionMethod) = .empty;
        errdefer methods.deinit(self.allocator);

        while (!self.check(.end) and !self.isAtEnd()) {
            if (self.check(.newline)) {
                _ = self.advance();
                continue;
            }
            // Check for typealias declaration
            if (self.check(.typealias)) {
                try associated_types.append(self.allocator, try self.parseTypeAliasDecl(false));
            } else {
                try methods.append(self.allocator, try self.parseExtensionMethod());
            }
        }

        const end_line = try self.expectEndLabel(name_token.text);
        try self.expectEndNewline();

        return .{
            .interface_name = name_token.text,
            .is_export = is_export,
            .associated_types = try associated_types.toOwnedSlice(self.allocator),
            .methods = try methods.toOwnedSlice(self.allocator),
            .block = .{
                .start_line = name_token.line,
                .start_column = name_token.column,
                .end_line = end_line,
                .identifier = name_token.text,
            },
        };
    }

    fn parseExtensionMethod(self: *Parser) ParseError!ast.ExtensionMethod {
        _ = try self.expect(.function);
        const name_token = try self.expect(.identifier);
        _ = try self.expect(.lparen);
        const params = try self.parseParamList();
        const return_type = try self.parseOptionalReturnType();
        const throws_type = try self.parseOptionalThrowsClause();
        _ = try self.expect(.newline);

        // Extension methods MUST have a body
        const body_result = try self.parseBodyUntilEnd(name_token.text);
        try self.expectEndNewline();

        return .{
            .name = name_token.text,
            .params = params,
            .return_type = return_type,
            .throws_type = throws_type,
            .body = body_result.body,
            .block = .{
                .start_line = name_token.line,
                .start_column = name_token.column,
                .end_line = body_result.end_line,
                .identifier = name_token.text,
            },
        };
    }
};
