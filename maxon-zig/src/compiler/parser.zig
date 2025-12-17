const std = @import("std");
const Token = @import("lexer.zig").Token;
const TokenType = @import("lexer.zig").TokenType;
const ast = @import("ast.zig");

pub const Parser = struct {
    tokens: []const Token,
    pos: usize,
    allocator: std.mem.Allocator,
    expr_ptrs: std.ArrayListUnmanaged(*ast.Expression),

    pub fn init(tokens: []const Token, allocator: std.mem.Allocator) Parser {
        return .{
            .tokens = tokens,
            .pos = 0,
            .allocator = allocator,
            .expr_ptrs = .empty,
        };
    }

    pub fn deinit(self: *Parser) void {
        for (self.expr_ptrs.items) |ptr| {
            self.allocator.destroy(ptr);
        }
        self.expr_ptrs.deinit(self.allocator);
    }

    pub fn parse(self: *Parser) !ast.Program {
        var types: std.ArrayListUnmanaged(ast.TypeDecl) = .empty;
        errdefer types.deinit(self.allocator);
        var functions: std.ArrayListUnmanaged(ast.FunctionDecl) = .empty;
        errdefer functions.deinit(self.allocator);

        // Skip leading newlines
        self.skipNewlines();

        while (!self.isAtEnd()) {
            if (self.check(.@"type")) {
                try types.append(self.allocator, try self.parseTypeDecl());
            } else if (self.check(.function)) {
                try functions.append(self.allocator, try self.parseFunction());
            } else {
                break;
            }
            self.skipNewlines();
        }

        return .{
            .types = try types.toOwnedSlice(self.allocator),
            .functions = try functions.toOwnedSlice(self.allocator),
        };
    }

    fn parseTypeDecl(self: *Parser) !ast.TypeDecl {
        _ = try self.expect(.@"type");
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
                return error.UnexpectedToken;
            }
            _ = self.advance(); // consume let/var

            const field_name = try self.expect(.identifier);
            const field_type = try self.expectTypeName();

            try fields.append(self.allocator, .{
                .name = field_name.text,
                .type_name = field_type,
                .is_mutable = is_mutable,
            });

            _ = try self.expect(.newline);
        }

        // Parse end 'label'
        _ = try self.expect(.end);
        _ = try self.expect(.string); // label

        // Require newline after end (or EOF)
        if (!self.isAtEnd() and !self.check(.newline)) {
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
        return error.UnexpectedToken;
    }

    fn parseFunction(self: *Parser) !ast.FunctionDecl {
        _ = try self.expect(.function);
        const name_token = try self.expect(.identifier);
        _ = try self.expect(.lparen);
        _ = try self.expect(.rparen);

        // Parse optional return type
        var return_type: ?[]const u8 = null;
        if (self.check(.returns)) {
            _ = self.advance();
            const type_token = try self.expect(.int);
            return_type = type_token.text;
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
            return error.ExpectedNewline;
        }

        return .{
            .name = name_token.text,
            .return_type = return_type,
            .body = try statements.toOwnedSlice(self.allocator),
        };
    }

    fn parseStatement(self: *Parser) !ast.Statement {
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
        return error.ExpectedExpression;
    }

    fn parseReturn(self: *Parser) !ast.Statement {
        _ = try self.expect(.@"return");
        const expr = try self.parseExpression();
        return .{ .@"return" = .{ .value = expr } };
    }

    const ParseError = error{ OutOfMemory, ExpectedExpression, UnexpectedToken, InvalidNumber };

    fn parseExpression(self: *Parser) ParseError!?ast.Expression {
        return try self.parseAdditive();
    }

    fn parseAdditive(self: *Parser) ParseError!?ast.Expression {
        var left = try self.parseMultiplicative() orelse return null;

        while (self.matchAdditive()) |op| {
            _ = self.advance();
            const right = try self.parseMultiplicative() orelse return error.ExpectedExpression;
            left = try self.makeBinary(left, op, right);
        }
        return left;
    }

    fn parseMultiplicative(self: *Parser) ParseError!?ast.Expression {
        var left = try self.parsePrimary() orelse return null;

        while (self.matchMultiplicative()) |op| {
            _ = self.advance();
            const right = try self.parsePrimary() orelse return error.ExpectedExpression;
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
            const value = std.fmt.parseInt(i64, token.text, 10) catch return error.InvalidNumber;
            return try self.parsePostfix(.{ .integer = value });
        }
        if (self.check(.float_literal)) {
            const token = self.advance();
            const value = std.fmt.parseFloat(f64, token.text) catch return error.InvalidNumber;
            return try self.parsePostfix(.{ .float_lit = value });
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
            const expr = try self.parseExpression() orelse return error.ExpectedExpression;
            _ = try self.expect(.rparen);
            return try self.parsePostfix(expr);
        }
        return null;
    }

    fn parsePostfix(self: *Parser, base_expr: ast.Expression) ParseError!ast.Expression {
        var expr = base_expr;
        while (self.check(.dot)) {
            _ = self.advance(); // consume '.'
            const field_token = try self.expect(.identifier);
            const base_ptr = try self.createExpr(expr);
            expr = .{ .field_access = .{
                .base = base_ptr,
                .field_name = field_token.text,
            } };
        }
        return expr;
    }

    fn parseFieldInit(self: *Parser) ParseError!ast.FieldInit {
        const name = try self.expect(.identifier);
        _ = try self.expect(.colon);
        const value = try self.parseExpression() orelse return error.ExpectedExpression;
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
            const first_arg = try self.parseExpression() orelse return error.ExpectedExpression;
            try args.append(self.allocator, first_arg);

            while (self.check(.comma)) {
                _ = self.advance(); // consume ','
                const arg = try self.parseExpression() orelse return error.ExpectedExpression;
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
        return error.UnexpectedToken;
    }

    fn check(self: *Parser, token_type: TokenType) bool {
        if (self.isAtEnd()) return false;
        return self.tokens[self.pos].type == token_type;
    }

    fn advance(self: *Parser) Token {
        if (!self.isAtEnd()) {
            self.pos += 1;
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
};
