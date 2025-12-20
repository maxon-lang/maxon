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
            if (self.check(.type)) {
                try types.append(self.allocator, try self.parseTypeDecl());
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
        _ = try self.expect(.type);
        const name_token = try self.expect(.identifier);

        // Require newline after type name
        _ = try self.expect(.newline);

        // Parse fields
        var fields: std.ArrayListUnmanaged(ast.FieldDecl) = .empty;
        errdefer fields.deinit(self.allocator);

        while (!self.check(.end) and !self.isAtEnd()) {
            // Skip blank lines
            if (self.check(.newline)) {
                _ = self.advance();
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
            .fields = try fields.toOwnedSlice(self.allocator),
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

            return .{ .array = .{
                .size = size,
                .element_type = elem_type,
            } };
        }

        // Simple type name
        const type_name = try self.expectTypeName();
        return .{ .simple = type_name };
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
            _ = try self.expect(.newline);
            return stmt;
        }
        if (self.check(.@"var")) {
            const stmt = try self.parseVarDecl(true);
            _ = try self.expect(.newline);
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

    fn parseIfStatement(self: *Parser) ParseError!ast.Statement {
        const if_stmt = try self.parseIfStmtInternal();
        return .{ .if_stmt = if_stmt };
    }

    fn parseIfStmtInternal(self: *Parser) ParseError!ast.IfStmt {
        _ = try self.expect(.@"if");

        // Parse condition
        const condition = try self.parseExpression() orelse {
            self.reportError(.E003);
            return error.ExpectedExpression;
        };

        // Parse label 'name'
        const label = try self.expect(.string);

        // Require newline after 'if condition 'label''
        _ = try self.expect(.newline);

        // Parse body statements
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
        _ = try self.expect(.string); // closing label

        // Check for else clause: end 'label' else ...
        var else_body: ?[]ast.Statement = null;
        var else_label: ?[]const u8 = null;
        var else_if: ?*const ast.IfStmt = null;

        if (self.check(.@"else")) {
            _ = self.advance(); // consume 'else'

            // Check for else-if: else if ...
            if (self.check(.@"if")) {
                // Parse the else-if as a recursive IfStmt
                const else_if_stmt = try self.parseIfStmtInternal();
                const ptr = try self.allocator.create(ast.IfStmt);
                ptr.* = else_if_stmt;
                try self.ifstmt_ptrs.append(self.allocator, ptr);
                else_if = ptr;
            } else {
                // Parse else block: else 'else_label' ... end 'else_label'
                const else_label_tok = try self.expect(.string);
                else_label = else_label_tok.text;

                // Require newline after 'else 'label''
                _ = try self.expect(.newline);

                // Parse else body statements
                var else_statements: std.ArrayListUnmanaged(ast.Statement) = .empty;
                errdefer else_statements.deinit(self.allocator);

                while (!self.check(.end) and !self.isAtEnd()) {
                    // Skip blank lines
                    if (self.check(.newline)) {
                        _ = self.advance();
                        continue;
                    }
                    try else_statements.append(self.allocator, try self.parseStatement());
                }

                // Parse end 'else_label'
                _ = try self.expect(.end);
                _ = try self.expect(.string); // closing else label

                else_body = try else_statements.toOwnedSlice(self.allocator);
            }
        }

        // Require newline after end (or allow EOF) - only if we didn't parse else-if
        if (else_if == null) {
            if (!self.isAtEnd() and !self.check(.newline)) {
                self.reportError(.E004);
                return error.ExpectedNewline;
            }
            if (self.check(.newline)) {
                _ = self.advance();
            }
        }

        return .{
            .condition = condition,
            .body = try statements.toOwnedSlice(self.allocator),
            .label = label.text,
            .else_body = else_body,
            .else_label = else_label,
            .else_if = else_if,
        };
    }

    fn parseWhileStatement(self: *Parser) ParseError!ast.Statement {
        _ = try self.expect(.@"while");

        // Parse condition
        const condition = try self.parseExpression() orelse {
            self.reportError(.E003);
            return error.ExpectedExpression;
        };

        // Parse label 'name'
        const label = try self.expect(.string);

        // Require newline after 'while condition 'label''
        _ = try self.expect(.newline);

        // Parse body statements
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
        _ = try self.expect(.string); // closing label

        // Require newline after end (or allow EOF)
        if (!self.isAtEnd() and !self.check(.newline)) {
            self.reportError(.E004);
            return error.ExpectedNewline;
        }
        if (self.check(.newline)) {
            _ = self.advance();
        }

        return .{ .while_stmt = .{
            .condition = condition,
            .body = try statements.toOwnedSlice(self.allocator),
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
