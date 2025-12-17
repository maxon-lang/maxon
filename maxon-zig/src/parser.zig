const std = @import("std");
const Token = @import("lexer.zig").Token;
const TokenType = @import("lexer.zig").TokenType;
const ast = @import("ast.zig");

pub const Parser = struct {
    tokens: []const Token,
    pos: usize,
    allocator: std.mem.Allocator,

    pub fn init(tokens: []const Token, allocator: std.mem.Allocator) Parser {
        return .{
            .tokens = tokens,
            .pos = 0,
            .allocator = allocator,
        };
    }

    pub fn parse(self: *Parser) !ast.Program {
        var functions: std.ArrayListUnmanaged(ast.FunctionDecl) = .empty;
        errdefer functions.deinit(self.allocator);

        while (!self.isAtEnd()) {
            if (self.check(.function)) {
                try functions.append(self.allocator, try self.parseFunction());
            } else {
                break;
            }
        }

        return .{
            .functions = try functions.toOwnedSlice(self.allocator),
        };
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

        // Parse body
        var statements: std.ArrayListUnmanaged(ast.Statement) = .empty;
        errdefer statements.deinit(self.allocator);

        while (!self.check(.end) and !self.isAtEnd()) {
            try statements.append(self.allocator, try self.parseStatement());
        }

        // Parse end 'label'
        _ = try self.expect(.end);
        _ = try self.expect(.string); // label

        return .{
            .name = name_token.text,
            .return_type = return_type,
            .body = try statements.toOwnedSlice(self.allocator),
        };
    }

    fn parseStatement(self: *Parser) !ast.Statement {
        if (self.check(.@"return")) {
            return try self.parseReturn();
        }
        return error.UnexpectedToken;
    }

    fn parseReturn(self: *Parser) !ast.Statement {
        _ = try self.expect(.@"return");
        const expr = try self.parseExpression();
        return .{ .@"return" = .{ .value = expr } };
    }

    fn parseExpression(self: *Parser) !?ast.Expression {
        if (self.check(.integer)) {
            const token = self.advance();
            const value = std.fmt.parseInt(i64, token.text, 10) catch return error.InvalidNumber;
            return .{ .integer = value };
        }
        return null;
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
};
