const std = @import("std");

pub const TokenType = enum {
    // Keywords
    function,
    returns,
    @"return",
    end,
    let,
    @"var",

    // Types
    int,

    // Literals
    identifier,
    integer,
    string, // for 'label' in end statements

    // Punctuation
    lparen,
    rparen,
    equals,
    plus,
    minus,
    star,
    slash,

    // Formatting
    newline,

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
            self.skipWhitespaceExceptNewline();
            if (self.pos >= self.source.len) break;

            const c = self.source[self.pos];

            // Newline token
            if (c == '\n') {
                try tokens.append(allocator, .{ .type = .newline, .text = "\n" });
                self.pos += 1;
                continue;
            }

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
            if (c == '=') {
                try tokens.append(allocator, .{ .type = .equals, .text = "=" });
                self.pos += 1;
                continue;
            }
            if (c == '+') {
                try tokens.append(allocator, .{ .type = .plus, .text = "+" });
                self.pos += 1;
                continue;
            }
            if (c == '-') {
                try tokens.append(allocator, .{ .type = .minus, .text = "-" });
                self.pos += 1;
                continue;
            }
            if (c == '*') {
                try tokens.append(allocator, .{ .type = .star, .text = "*" });
                self.pos += 1;
                continue;
            }
            if (c == '/') {
                // Check if it's a comment (already handled above, but double-check)
                if (self.pos + 1 < self.source.len and self.source[self.pos + 1] == '/') {
                    while (self.pos < self.source.len and self.source[self.pos] != '\n') {
                        self.pos += 1;
                    }
                    continue;
                }
                try tokens.append(allocator, .{ .type = .slash, .text = "/" });
                self.pos += 1;
                continue;
            }

            // String literal (single quotes for end labels)
            if (c == '\'') {
                const start = self.pos + 1;
                self.pos += 1;
                // UTF-8: scan bytes until closing quote
                while (self.pos < self.source.len and self.source[self.pos] != '\'') {
                    self.pos += 1;
                }
                const text = self.source[start..self.pos];
                self.pos += 1; // skip closing quote
                try tokens.append(allocator, .{ .type = .string, .text = text });
                continue;
            }

            // Integer literal (ASCII digits only)
            if (c >= '0' and c <= '9') {
                const start = self.pos;
                while (self.pos < self.source.len and self.source[self.pos] >= '0' and self.source[self.pos] <= '9') {
                    self.pos += 1;
                }
                try tokens.append(allocator, .{ .type = .integer, .text = self.source[start..self.pos] });
                continue;
            }

            // Identifier or keyword
            // Start: ASCII letter, underscore, or UTF-8 continuation byte (for unicode identifiers)
            if (isIdentifierStart(c)) {
                const start = self.pos;
                self.pos += 1;
                while (self.pos < self.source.len and isIdentifierContinue(self.source[self.pos])) {
                    self.pos += 1;
                }
                const text = self.source[start..self.pos];
                const token_type = getKeyword(text) orelse .identifier;
                try tokens.append(allocator, .{ .type = token_type, .text = text });
                continue;
            }

            // UTF-8 multi-byte sequence (non-ASCII) - could be identifier
            if (c >= 0x80) {
                const start = self.pos;
                // Skip UTF-8 sequence
                self.pos += utf8ByteLen(c);
                // Continue while identifier characters
                while (self.pos < self.source.len and isIdentifierContinue(self.source[self.pos])) {
                    if (self.source[self.pos] >= 0x80) {
                        self.pos += utf8ByteLen(self.source[self.pos]);
                    } else {
                        self.pos += 1;
                    }
                }
                const text = self.source[start..self.pos];
                try tokens.append(allocator, .{ .type = .identifier, .text = text });
                continue;
            }

            // Unknown character - skip
            self.pos += 1;
        }

        try tokens.append(allocator, .{ .type = .eof, .text = "" });
        return tokens.toOwnedSlice(allocator);
    }

    fn skipWhitespaceExceptNewline(self: *Lexer) void {
        while (self.pos < self.source.len) {
            const c = self.source[self.pos];
            if (c == ' ' or c == '\t' or c == '\r') {
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
            .{ "let", TokenType.let },
            .{ "var", TokenType.@"var" },
            .{ "int", TokenType.int },
        };
        inline for (keywords) |kw| {
            if (std.mem.eql(u8, text, kw[0])) {
                return kw[1];
            }
        }
        return null;
    }

    fn isIdentifierStart(c: u8) bool {
        return (c >= 'a' and c <= 'z') or (c >= 'A' and c <= 'Z') or c == '_';
    }

    fn isIdentifierContinue(c: u8) bool {
        return isIdentifierStart(c) or (c >= '0' and c <= '9') or c >= 0x80;
    }

    fn utf8ByteLen(first_byte: u8) usize {
        if (first_byte < 0x80) return 1;
        if (first_byte < 0xE0) return 2;
        if (first_byte < 0xF0) return 3;
        return 4;
    }
};
