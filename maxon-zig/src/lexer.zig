const std = @import("std");

pub const TokenType = enum {
    // Keywords
    function,
    returns,
    @"return",
    end,

    // Types
    int,

    // Literals
    identifier,
    integer,
    string, // for 'label' in end statements

    // Punctuation
    lparen,
    rparen,

    // Special
    eof,
};

pub const Token = struct {
    type: TokenType,
    text: []const u8,
};

pub const Lexer = struct {
    source: []const u8,
    pos: usize,

    pub fn init(source: []const u8) Lexer {
        return .{
            .source = source,
            .pos = 0,
        };
    }

    pub fn tokenize(self: *Lexer, allocator: std.mem.Allocator) ![]Token {
        var tokens: std.ArrayListUnmanaged(Token) = .empty;
        errdefer tokens.deinit(allocator);

        while (self.pos < self.source.len) {
            self.skipWhitespace();
            if (self.pos >= self.source.len) break;

            const c = self.source[self.pos];

            // Skip comments
            if (c == '/' and self.pos + 1 < self.source.len and self.source[self.pos + 1] == '/') {
                while (self.pos < self.source.len and self.source[self.pos] != '\n') {
                    self.pos += 1;
                }
                continue;
            }

            // Single character tokens
            if (c == '(') {
                try tokens.append(allocator, .{ .type = .lparen, .text = "(" });
                self.pos += 1;
                continue;
            }
            if (c == ')') {
                try tokens.append(allocator, .{ .type = .rparen, .text = ")" });
                self.pos += 1;
                continue;
            }

            // String literal (single quotes for end labels)
            if (c == '\'') {
                const start = self.pos + 1;
                self.pos += 1;
                while (self.pos < self.source.len and self.source[self.pos] != '\'') {
                    self.pos += 1;
                }
                const text = self.source[start..self.pos];
                self.pos += 1; // skip closing quote
                try tokens.append(allocator, .{ .type = .string, .text = text });
                continue;
            }

            // Integer literal
            if (std.ascii.isDigit(c)) {
                const start = self.pos;
                while (self.pos < self.source.len and std.ascii.isDigit(self.source[self.pos])) {
                    self.pos += 1;
                }
                try tokens.append(allocator, .{ .type = .integer, .text = self.source[start..self.pos] });
                continue;
            }

            // Identifier or keyword
            if (std.ascii.isAlphabetic(c) or c == '_') {
                const start = self.pos;
                while (self.pos < self.source.len and (std.ascii.isAlphanumeric(self.source[self.pos]) or self.source[self.pos] == '_')) {
                    self.pos += 1;
                }
                const text = self.source[start..self.pos];
                const token_type = getKeyword(text) orelse .identifier;
                try tokens.append(allocator, .{ .type = token_type, .text = text });
                continue;
            }

            // Unknown character - skip
            self.pos += 1;
        }

        try tokens.append(allocator, .{ .type = .eof, .text = "" });
        return tokens.toOwnedSlice(allocator);
    }

    fn skipWhitespace(self: *Lexer) void {
        while (self.pos < self.source.len) {
            const c = self.source[self.pos];
            if (c == ' ' or c == '\t' or c == '\n' or c == '\r') {
                self.pos += 1;
            } else {
                break;
            }
        }
    }

    fn getKeyword(text: []const u8) ?TokenType {
        const keywords = .{
            .{ "function", TokenType.function },
            .{ "returns", TokenType.returns },
            .{ "return", TokenType.@"return" },
            .{ "end", TokenType.end },
            .{ "int", TokenType.int },
        };
        inline for (keywords) |kw| {
            if (std.mem.eql(u8, text, kw[0])) {
                return kw[1];
            }
        }
        return null;
    }
};
