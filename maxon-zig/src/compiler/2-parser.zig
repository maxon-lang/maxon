const std = @import("std");
const Token = @import("1-lexer.zig").Token;
const TokenType = @import("1-lexer.zig").TokenType;
const ast = @import("ast.zig");
const debug = @import("debug.zig");
const err = @import("error.zig");

pub const ParseError = error{
    OutOfMemory,
    ExpectedExpression,
    UnexpectedToken,
    InvalidNumber,
    ExpectedNewline,
};

pub const Parser = struct {
    tokens: []const Token,
    pos: usize,
    allocator: std.mem.Allocator,
    expr_ptrs: std.ArrayListUnmanaged(*ast.Expression),
    ifstmt_ptrs: std.ArrayListUnmanaged(*ast.IfStmt),
    /// Source file path for error messages
    source_file: ?[]const u8,
    /// Last error with location info
    last_error: ?err.CompileError,

    pub fn init(tokens: []const Token, allocator: std.mem.Allocator) Parser {
        return .{
            .tokens = tokens,
            .pos = 0,
            .allocator = allocator,
            .expr_ptrs = .empty,
            .ifstmt_ptrs = .empty,
            .source_file = null,
            .last_error = null,
        };
    }

    pub fn initWithFile(tokens: []const Token, allocator: std.mem.Allocator, source_file: []const u8) Parser {
        return .{
            .tokens = tokens,
            .pos = 0,
            .allocator = allocator,
            .expr_ptrs = .empty,
            .ifstmt_ptrs = .empty,
            .source_file = source_file,
            .last_error = null,
        };
    }

    pub fn deinit(self: *Parser) void {
        for (self.expr_ptrs.items) |ptr| {
            self.allocator.destroy(ptr);
        }
        self.expr_ptrs.deinit(self.allocator);
        // Note: ifstmt_ptrs are freed by freeProgram in 0-compiler.zig
        self.ifstmt_ptrs.deinit(self.allocator);
    }

    pub fn parse(self: *Parser) ParseError!ast.Program {
        var types: std.ArrayListUnmanaged(ast.TypeDecl) = .empty;
        errdefer types.deinit(self.allocator);
        var enums: std.ArrayListUnmanaged(ast.EnumDecl) = .empty;
        errdefer enums.deinit(self.allocator);
        var functions: std.ArrayListUnmanaged(ast.FunctionDecl) = .empty;
        errdefer functions.deinit(self.allocator);

        // Skip leading newlines
        self.skipNewlines();

        while (!self.isAtEnd()) {
            // Check for type declaration (with optional 'export' prefix)
            if (self.check(.type)) {
                try types.append(self.allocator, try self.parseTypeDecl());
            } else if (self.check(.@"export")) {
                // Peek ahead to see if this is 'export type' or 'export function'
                if (self.peek(1)) |next| {
                    if (next.type == .type) {
                        try types.append(self.allocator, try self.parseTypeDecl());
                    } else if (next.type == .function) {
                        // For now, treat export function as regular function
                        _ = self.advance(); // skip 'export'
                        try functions.append(self.allocator, try self.parseFunction());
                    } else {
                        self.reportError(.E002);
                        return error.UnexpectedToken;
                    }
                } else {
                    self.reportError(.E002);
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
                debug.parser("  Expected: 'type', 'enum', or 'function' declaration\n", .{});
                self.reportError(.E002);
                return error.UnexpectedToken;
            }
            self.skipNewlines();
        }

        return .{
            .types = try types.toOwnedSlice(self.allocator),
            .enums = try enums.toOwnedSlice(self.allocator),
            .functions = try functions.toOwnedSlice(self.allocator),
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

    fn parseTypeDecl(self: *Parser) !ast.TypeDecl {
        // Check for optional 'export' before 'type'
        var is_export = false;
        if (self.check(.@"export")) {
            is_export = true;
            _ = self.advance();
        }

        _ = try self.expect(.type);
        const name_token = try self.expect(.identifier);

        // Parse optional generic params: uses TypeParam, ...
        var generic_params: std.ArrayListUnmanaged([]const u8) = .empty;
        errdefer generic_params.deinit(self.allocator);

        if (self.check(.uses)) {
            _ = self.advance();
            const first_param = try self.expect(.identifier);
            try generic_params.append(self.allocator, first_param.text);

            while (self.check(.comma)) {
                _ = self.advance();
                const param = try self.expect(.identifier);
                try generic_params.append(self.allocator, param.text);
            }
        }

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

        // Require newline after type header
        _ = try self.expect(.newline);

        // Parse fields and methods
        var fields: std.ArrayListUnmanaged(ast.FieldDecl) = .empty;
        errdefer fields.deinit(self.allocator);
        var methods: std.ArrayListUnmanaged(ast.MethodDecl) = .empty;
        errdefer methods.deinit(self.allocator);

        while (!self.check(.end) and !self.isAtEnd()) {
            // Skip blank lines
            if (self.check(.newline)) {
                _ = self.advance();
                continue;
            }

            // Check for method: export? static? function ...
            if (self.check(.@"export") or self.check(.static) or self.check(.function)) {
                try methods.append(self.allocator, try self.parseMethodDecl());
                continue;
            }

            // Parse field: let/var name type
            const is_mutable = self.check(.@"var");
            if (!is_mutable and !self.check(.let)) {
                self.reportError(.E002);
                return error.UnexpectedToken;
            }
            _ = self.advance(); // consume let/var

            const field_name = try self.expect(.identifier);
            const field_type = try self.parseTypeExpr();

            try fields.append(self.allocator, .{
                .name = field_name.text,
                .type_expr = field_type,
                .is_mutable = is_mutable,
            });

            _ = try self.expect(.newline);
        }

        // Parse end 'label'
        _ = try self.expect(.end);
        _ = try self.expect(.string); // label

        // Require newline after end (or EOF)
        if (!self.isAtEnd() and !self.check(.newline)) {
            self.reportError(.E004);
            return error.ExpectedNewline;
        }

        return .{
            .name = name_token.text,
            .is_export = is_export,
            .generic_params = try generic_params.toOwnedSlice(self.allocator),
            .conformances = try conformances.toOwnedSlice(self.allocator),
            .fields = try fields.toOwnedSlice(self.allocator),
            .methods = try methods.toOwnedSlice(self.allocator),
        };
    }

    fn parseInterfaceConformance(self: *Parser) !ast.InterfaceConformance {
        const interface_name = try self.expect(.identifier);

        var type_args: std.ArrayListUnmanaged([]const u8) = .empty;
        errdefer type_args.deinit(self.allocator);

        // Parse optional: with TypeArg, TypeArg2, ...
        if (self.check(.with)) {
            _ = self.advance();
            const first_arg = try self.expect(.identifier);
            try type_args.append(self.allocator, first_arg.text);

            // Additional type args separated by commas (stop at newline or next keyword)
            while (self.check(.comma)) {
                // Peek to check if this comma separates type args or conformances
                if (self.peek(1)) |next| {
                    // If next is an identifier, check if after that is 'with' (new conformance)
                    if (next.type == .identifier) {
                        if (self.peek(2)) |after_ident| {
                            if (after_ident.type == .with or after_ident.type == .newline) {
                                // This comma separates conformances, not type args
                                break;
                            }
                        }
                    }
                }
                _ = self.advance(); // consume comma
                const arg = try self.expect(.identifier);
                try type_args.append(self.allocator, arg.text);
            }
        }

        return .{
            .interface_name = interface_name.text,
            .type_args = try type_args.toOwnedSlice(self.allocator),
        };
    }

    fn parseMethodDecl(self: *Parser) !ast.MethodDecl {
        var method_is_export = false;
        var is_static = false;

        // Parse optional modifiers
        if (self.check(.@"export")) {
            method_is_export = true;
            _ = self.advance();
        }
        if (self.check(.static)) {
            is_static = true;
            _ = self.advance();
        }

        _ = try self.expect(.function);

        // Parse method name - can be qualified like "Collection.count"
        const first_name = try self.expect(.identifier);
        var name: []const u8 = first_name.text;
        var qualified_name: ?[]const u8 = null;

        if (self.check(.dot)) {
            _ = self.advance();
            const second_name = try self.expect(.identifier);
            // Store "Interface.method" as qualified_name, use "method" as name
            qualified_name = try std.fmt.allocPrint(self.allocator, "{s}.{s}", .{ first_name.text, second_name.text });
            name = second_name.text;
        }

        _ = try self.expect(.lparen);

        // Parse parameter list
        var params: std.ArrayListUnmanaged(ast.ParamDecl) = .empty;
        errdefer params.deinit(self.allocator);

        if (!self.check(.rparen)) {
            // Parse first parameter
            const first_param_name = try self.expect(.identifier);
            const first_type = try self.parseTypeExpr();
            try params.append(self.allocator, .{
                .name = first_param_name.text,
                .type_expr = first_type,
            });

            // Parse remaining parameters
            while (self.check(.comma)) {
                _ = self.advance();
                const param_name = try self.expect(.identifier);
                const param_type = try self.parseTypeExpr();
                try params.append(self.allocator, .{
                    .name = param_name.text,
                    .type_expr = param_type,
                });
            }
        }

        _ = try self.expect(.rparen);

        // Parse optional return type
        var return_type: ?ast.TypeExpr = null;
        if (self.check(.returns)) {
            _ = self.advance();
            return_type = try self.parseTypeExpr();
        }

        // Require newline after method signature
        _ = try self.expect(.newline);

        // Parse body
        var statements: std.ArrayListUnmanaged(ast.Statement) = .empty;
        errdefer statements.deinit(self.allocator);

        while (!self.check(.end) and !self.isAtEnd()) {
            // Skip blank lines
            if (self.check(.newline)) {
                _ = self.advance();
                continue;
            }
            try statements.append(self.allocator, try self.parseStatement());
        }

        // Parse end 'label'
        _ = try self.expect(.end);
        _ = try self.expect(.string); // label

        // Require newline after end (or EOF/next method)
        if (!self.isAtEnd() and !self.check(.newline)) {
            self.reportError(.E004);
            return error.ExpectedNewline;
        }
        if (self.check(.newline)) {
            _ = self.advance();
        }

        return .{
            .name = name,
            .qualified_name = qualified_name,
            .is_static = is_static,
            .is_export = method_is_export,
            .params = try params.toOwnedSlice(self.allocator),
            .return_type = return_type,
            .body = try statements.toOwnedSlice(self.allocator),
        };
    }

    fn expectTypeName(self: *Parser) ![]const u8 {
        if (self.check(.int)) {
            return self.advance().text;
        }
        if (self.check(.float)) {
            return self.advance().text;
        }
        if (self.check(.identifier)) {
            return self.advance().text;
        }
        self.reportError(.E002);
        return error.UnexpectedToken;
    }

    fn parseTypeExpr(self: *Parser) !ast.TypeExpr {
        // Check for array type: array of [size] element_type
        var base_type: ast.TypeExpr = undefined;
        if (self.check(.array)) {
            _ = self.advance(); // consume 'array'
            _ = try self.expect(.of);

            // Check for optional size (integer literal)
            var size: ?i64 = null;
            if (self.check(.integer)) {
                const size_token = self.advance();
                size = std.fmt.parseInt(i64, size_token.text, 10) catch {
                    self.reportError(.E001);
                    return error.InvalidNumber;
                };
            }

            // Parse element type
            const elem_type = try self.expectTypeName();

            base_type = .{ .array = .{
                .size = size,
                .element_type = elem_type,
            } };
        } else {
            // Simple type name
            const type_name = try self.expectTypeName();
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

    fn parseFunction(self: *Parser) !ast.FunctionDecl {
        _ = try self.expect(.function);
        const name_token = try self.expect(.identifier);
        _ = try self.expect(.lparen);

        // Parse parameter list
        var params: std.ArrayListUnmanaged(ast.ParamDecl) = .empty;
        errdefer params.deinit(self.allocator);

        if (!self.check(.rparen)) {
            // Parse first parameter
            const first_name = try self.expect(.identifier);
            const first_type = try self.parseTypeExpr();
            try params.append(self.allocator, .{
                .name = first_name.text,
                .type_expr = first_type,
            });

            // Parse remaining parameters
            while (self.check(.comma)) {
                _ = self.advance();
                const param_name = try self.expect(.identifier);
                const param_type = try self.parseTypeExpr();
                try params.append(self.allocator, .{
                    .name = param_name.text,
                    .type_expr = param_type,
                });
            }
        }

        _ = try self.expect(.rparen);

        // Parse optional return type
        var return_type: ?ast.TypeExpr = null;
        if (self.check(.returns)) {
            _ = self.advance();
            return_type = try self.parseTypeExpr();
        }

        // Require newline after function signature
        _ = try self.expect(.newline);

        // Parse body
        var statements: std.ArrayListUnmanaged(ast.Statement) = .empty;
        errdefer statements.deinit(self.allocator);

        while (!self.check(.end) and !self.isAtEnd()) {
            // Skip blank lines
            if (self.check(.newline)) {
                _ = self.advance();
                continue;
            }
            try statements.append(self.allocator, try self.parseStatement());
        }

        // Parse end 'label'
        _ = try self.expect(.end);
        _ = try self.expect(.string); // label

        // Require newline after end (or EOF)
        if (!self.isAtEnd() and !self.check(.newline)) {
            self.reportError(.E004);
            return error.ExpectedNewline;
        }

        return .{
            .name = name_token.text,
            .params = try params.toOwnedSlice(self.allocator),
            .return_type = return_type,
            .body = try statements.toOwnedSlice(self.allocator),
        };
    }

    fn parseStatement(self: *Parser) ParseError!ast.Statement {
        if (self.check(.@"return")) {
            const stmt = try self.parseReturn();
            _ = try self.expect(.newline);
            return stmt;
        }
        if (self.check(.let)) {
            const stmt = try self.parseVarDecl(false);
            // else-unwrap handles its own newlines
            if (stmt != .else_unwrap_decl) {
                _ = try self.expect(.newline);
            }
            return stmt;
        }
        if (self.check(.@"var")) {
            const stmt = try self.parseVarDecl(true);
            // else-unwrap handles its own newlines
            if (stmt != .else_unwrap_decl) {
                _ = try self.expect(.newline);
            }
            return stmt;
        }
        if (self.check(.@"if")) {
            return try self.parseIfStatement();
        }
        if (self.check(.@"while")) {
            return try self.parseWhileStatement();
        }
        if (self.check(.@"break")) {
            _ = self.advance();
            _ = try self.expect(.newline);
            return .{ .break_stmt = .{} };
        }
        if (self.check(.@"continue")) {
            _ = self.advance();
            _ = try self.expect(.newline);
            return .{ .continue_stmt = .{} };
        }
        // Check for assignment, index assignment, or call statement: identifier...
        if (self.check(.identifier)) {
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
                    return .{ .index_assign = .{
                        .base = expr.index.base,
                        .index = expr.index.index,
                        .value = value,
                    } };
                }

                // Check if this is a simple identifier (x = value)
                if (expr == .identifier) {
                    return .{ .assign = .{
                        .target = expr.identifier,
                        .value = value,
                    } };
                }

                // Check if this is a field access (obj.field = value)
                if (expr == .field_access) {
                    return .{ .field_assign = .{
                        .base = expr.field_access.base,
                        .field_name = expr.field_access.field_name,
                        .value = value,
                    } };
                }

                // Other expressions on LHS not supported
                self.reportError(.E002);
                return error.UnexpectedToken;
            }

            // Check if this is a call expression (standalone function call)
            if (expr == .call) {
                _ = try self.expect(.newline);
                return .{ .call = expr.call };
            }

            // Check if this is a method call (standalone method call like arr.push(x))
            if (expr == .method_call) {
                _ = try self.expect(.newline);
                return .{ .method_call = expr.method_call };
            }

            // Not an assignment or call, restore position
            self.pos = start_pos;
        }
        self.reportError(.E002);
        return error.UnexpectedToken;
    }

    fn parseVarDecl(self: *Parser, is_var: bool) !ast.Statement {
        _ = self.advance(); // consume let/var
        const name_token = try self.expect(.identifier);
        _ = try self.expect(.equals);
        const value = try self.parseExpression();
        if (value) |expr| {
            // Check for else-unwrap: var x = opt else 'label' ... end 'label'
            if (self.check(.@"else")) {
                _ = self.advance(); // consume 'else'

                // Parse label
                const label = try self.expect(.string);

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

                // Parse end 'label'
                _ = try self.expect(.end);
                _ = try self.expect(.string);

                // Require newline after end (or allow EOF)
                if (!self.isAtEnd() and !self.check(.newline)) {
                    self.reportError(.E004);
                    return error.ExpectedNewline;
                }
                if (self.check(.newline)) {
                    _ = self.advance();
                }

                const expr_ptr = try self.createExpr(expr);
                return .{ .else_unwrap_decl = .{
                    .var_name = name_token.text,
                    .optional_expr = expr_ptr,
                    .default_body = try default_statements.toOwnedSlice(self.allocator),
                    .label = label.text,
                } };
            }

            if (is_var) {
                return .{ .var_decl = .{ .name = name_token.text, .value = expr } };
            } else {
                return .{ .let_decl = .{ .name = name_token.text, .value = expr } };
            }
        }
        self.reportError(.E003);
        return error.ExpectedExpression;
    }

    fn parseReturn(self: *Parser) !ast.Statement {
        _ = try self.expect(.@"return");
        const expr = try self.parseExpression();
        return .{ .@"return" = .{ .value = expr } };
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

    /// Parse 'end 'label'' and consume trailing newline (or EOF)
    fn parseEndAndNewline(self: *Parser) ParseError!void {
        _ = try self.expect(.end);
        _ = try self.expect(.string);
        try self.expectTrailingNewline();
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
        const else_label_tok = try self.expect(.string);
        _ = try self.expect(.newline);
        const else_body = try self.parseBlockBody();
        _ = try self.expect(.end);
        _ = try self.expect(.string);

        return .{ .body = else_body, .label = else_label_tok.text, .else_if = null };
    }

    // -------------------------------------------------------------------------
    // Control flow statements
    // -------------------------------------------------------------------------

    fn parseIfStatement(self: *Parser) ParseError!ast.Statement {
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

            const label = try self.expect(.string);
            _ = try self.expect(.newline);

            const body = try self.parseBlockBody();
            _ = try self.expect(.end);
            _ = try self.expect(.string);

            const else_clause = try self.parseElseClause(false);
            try self.expectTrailingNewline();

            return .{ .if_stmt = .{
                .condition = condition,
                .body = body,
                .label = label.text,
                .else_body = else_clause.body,
                .else_label = else_clause.label,
                .else_if = null,
                .binding_name = name_token.text,
            } };
        }

        // Regular if statement (if already consumed)
        return .{ .if_stmt = try self.parseIfConditionAndBody() };
    }

    /// Parse if statement after 'if' has been consumed. Used by parseIfStatement
    /// and parseElseClause for else-if chains.
    fn parseIfConditionAndBody(self: *Parser) ParseError!ast.IfStmt {
        const condition = try self.parseExpression() orelse {
            self.reportError(.E003);
            return error.ExpectedExpression;
        };

        const label = try self.expect(.string);
        _ = try self.expect(.newline);

        const body = try self.parseBlockBody();
        _ = try self.expect(.end);
        _ = try self.expect(.string);

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
        };
    }

    fn parseWhileStatement(self: *Parser) ParseError!ast.Statement {
        _ = try self.expect(.@"while");

        const condition = try self.parseExpression() orelse {
            self.reportError(.E003);
            return error.ExpectedExpression;
        };

        const label = try self.expect(.string);
        _ = try self.expect(.newline);

        const body = try self.parseBlockBody();
        try self.parseEndAndNewline();

        return .{ .while_stmt = .{
            .condition = condition,
            .body = body,
            .label = label.text,
        } };
    }

    fn parseExpression(self: *Parser) ParseError!?ast.Expression {
        return try self.parseComparison();
    }

    fn parseComparison(self: *Parser) ParseError!?ast.Expression {
        var left = try self.parseAdditive() orelse return null;

        if (self.matchComparison()) |op| {
            _ = self.advance();
            const right = try self.parseAdditive() orelse {
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
        var left = try self.parsePrimary() orelse return null;

        while (self.matchMultiplicative()) |op| {
            _ = self.advance();
            const right = try self.parsePrimary() orelse {
                self.reportError(.E003);
                return error.ExpectedExpression;
            };
            left = try self.makeBinary(left, op, right);
        }
        return left;
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

    fn parsePrimary(self: *Parser) ParseError!?ast.Expression {
        if (self.check(.integer)) {
            const token = self.advance();
            const value = std.fmt.parseInt(i64, token.text, 10) catch {
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
        if (self.check(.@"true")) {
            _ = self.advance();
            return try self.parsePostfix(.{ .bool_lit = true });
        }
        if (self.check(.@"false")) {
            _ = self.advance();
            return try self.parsePostfix(.{ .bool_lit = false });
        }
        // Nil literal
        if (self.check(.nil)) {
            _ = self.advance();
            return try self.parsePostfix(.nil_lit);
        }
        // Array literal: [expr, expr, ...]
        if (self.check(.lbracket)) {
            return try self.parseArrayLiteral();
        }
        // Sized array: array of N type
        if (self.check(.array)) {
            return try self.parseSizedArray();
        }
        if (self.check(.identifier)) {
            const token = self.advance();
            // Check for struct initialization: TypeName{...}
            if (self.check(.lbrace)) {
                return try self.parseStructInit(token.text);
            }
            // Check for function call
            if (self.check(.lparen)) {
                return try self.parseCall(token.text);
            }
            return try self.parsePostfix(.{ .identifier = token.text });
        }
        if (self.check(.lparen)) {
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

                    if (!self.check(.rparen)) {
                        const first_arg = try self.parseExpression() orelse {
                            self.reportError(.E003);
                            return error.ExpectedExpression;
                        };
                        try args.append(self.allocator, first_arg);
                        while (self.check(.comma)) {
                            _ = self.advance();
                            const arg = try self.parseExpression() orelse {
                                self.reportError(.E003);
                                return error.ExpectedExpression;
                            };
                            try args.append(self.allocator, arg);
                        }
                    }
                    _ = try self.expect(.rparen);
                    const base_ptr = try self.createExpr(expr);
                    expr = .{ .method_call = .{
                        .base = base_ptr,
                        .method_name = field_token.text,
                        .args = try args.toOwnedSlice(self.allocator),
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
            } else {
                break;
            }
        }
        return expr;
    }

    fn parseArrayLiteral(self: *Parser) ParseError!ast.Expression {
        _ = try self.expect(.lbracket);

        var elements: std.ArrayListUnmanaged(ast.Expression) = .empty;
        errdefer elements.deinit(self.allocator);

        if (!self.check(.rbracket)) {
            // Parse first element
            const first = try self.parseExpression() orelse {
                self.reportError(.E003);
                return error.ExpectedExpression;
            };
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
        }

        _ = try self.expect(.rbracket);
        return try self.parsePostfix(.{ .array_literal = .{
            .elements = try elements.toOwnedSlice(self.allocator),
        } });
    }

    fn parseSizedArray(self: *Parser) ParseError!ast.Expression {
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

        return try self.parsePostfix(.{ .sized_array = .{
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

    fn parseStructInit(self: *Parser, type_name: []const u8) ParseError!ast.Expression {
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
            .fields = try fields.toOwnedSlice(self.allocator),
        } });
    }

    fn parseCall(self: *Parser, func_name: []const u8) ParseError!ast.Expression {
        _ = try self.expect(.lparen);
        var args: std.ArrayListUnmanaged(ast.Expression) = .empty;
        errdefer args.deinit(self.allocator);

        // Parse argument list
        if (!self.check(.rparen)) {
            const first_arg = try self.parseExpression() orelse {
                self.reportError(.E003);
                return error.ExpectedExpression;
            };
            try args.append(self.allocator, first_arg);

            while (self.check(.comma)) {
                _ = self.advance(); // consume ','
                const arg = try self.parseExpression() orelse {
                    self.reportError(.E003);
                    return error.ExpectedExpression;
                };
                try args.append(self.allocator, arg);
            }
        }

        _ = try self.expect(.rparen);
        const args_slice = try args.toOwnedSlice(self.allocator);
        return .{ .call = .{ .func_name = func_name, .args = args_slice } };
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
        self.reportError(.E002);
        return error.UnexpectedToken;
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

        // Require newline after enum name
        _ = try self.expect(.newline);

        // Parse members
        var members: std.ArrayListUnmanaged([]const u8) = .empty;
        errdefer members.deinit(self.allocator);

        while (!self.check(.end) and !self.isAtEnd()) {
            // Skip blank lines
            if (self.check(.newline)) {
                _ = self.advance();
                continue;
            }

            // Parse member name (just an identifier)
            const member_name = try self.expect(.identifier);
            try members.append(self.allocator, member_name.text);

            _ = try self.expect(.newline);
        }

        // Parse end 'label'
        _ = try self.expect(.end);
        _ = try self.expect(.string); // label

        // Require newline after end (or EOF)
        if (!self.isAtEnd() and !self.check(.newline)) {
            self.reportError(.E004);
            return error.ExpectedNewline;
        }

        return .{
            .name = name_token.text,
            .members = try members.toOwnedSlice(self.allocator),
        };
    }
};
