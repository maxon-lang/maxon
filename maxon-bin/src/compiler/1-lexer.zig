const std = @import("std");

pub const TokenType = enum {
    // Keywords
    function,
    returns,
    @"return",
    end,
    let,
    @"var",
    mod,
    type,
    @"enum",
    array,
    of,
    @"if",
    @"else",
    @"while",
    @"for",
    in,
    @"break",
    @"continue",
    true,
    false,
    @"and",
    @"or",
    nil,
    // Type system keywords
    uses,
    is,
    with,
    static,
    @"export",
    self,
    Self,
    interface,
    extends,
    from,
    to,

    // Types
    int,
    float,

    // Literals
    identifier,
    integer,
    float_literal,
    string, // for 'label' in end statements

    // Punctuation
    lparen,
    rparen,
    lbrace,
    rbrace,
    lbracket,
    rbracket,
    equals,
    equals_equals, // ==
    not_equals, // !=
    less_than, // <
    less_equals, // <=
    greater_than, // >
    greater_equals, // >=
    plus,
    minus,
    star,
    slash,
    comma,
    colon,
    dot,

    // Formatting
    newline,

    // Special
    eof,
};

pub const Token = struct {
    type: TokenType,
    text: []const u8,
    line: u32,
    column: u32,
};

pub const Lexer = struct {
    source: []const u8,
    pos: usize,
    line: u32,
    column: u32,

    pub fn init(source: []const u8) Lexer {
        return .{
            .source = source,
            .pos = 0,
            .line = 1,
            .column = 1,
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
                try tokens.append(allocator, .{ .type = .newline, .text = "\n", .line = self.line, .column = self.column });
                self.pos += 1;
                self.line += 1;
                self.column = 1;
                continue;
            }

            // Stop at fragment separator "---" at start of line
            if (c == '-' and self.column == 1 and self.pos + 2 < self.source.len and
                self.source[self.pos + 1] == '-' and self.source[self.pos + 2] == '-')
            {
                break;
            }

            // Skip comments
            if (c == '/' and self.pos + 1 < self.source.len and self.source[self.pos + 1] == '/') {
                while (self.pos < self.source.len and self.source[self.pos] != '\n') {
                    self.pos += 1;
                    self.column += 1;
                }
                continue;
            }

            // Single character tokens
            if (c == '(') {
                try tokens.append(allocator, .{ .type = .lparen, .text = "(", .line = self.line, .column = self.column });
                self.pos += 1;
                self.column += 1;
                continue;
            }
            if (c == ')') {
                try tokens.append(allocator, .{ .type = .rparen, .text = ")", .line = self.line, .column = self.column });
                self.pos += 1;
                self.column += 1;
                continue;
            }
            if (c == '=') {
                // Check for ==
                if (self.pos + 1 < self.source.len and self.source[self.pos + 1] == '=') {
                    try tokens.append(allocator, .{ .type = .equals_equals, .text = "==", .line = self.line, .column = self.column });
                    self.pos += 2;
                    self.column += 2;
                } else {
                    try tokens.append(allocator, .{ .type = .equals, .text = "=", .line = self.line, .column = self.column });
                    self.pos += 1;
                    self.column += 1;
                }
                continue;
            }
            if (c == '!') {
                // Check for !=
                if (self.pos + 1 < self.source.len and self.source[self.pos + 1] == '=') {
                    try tokens.append(allocator, .{ .type = .not_equals, .text = "!=", .line = self.line, .column = self.column });
                    self.pos += 2;
                    self.column += 2;
                    continue;
                }
                // Single ! not supported yet
            }
            if (c == '<') {
                // Check for <=
                if (self.pos + 1 < self.source.len and self.source[self.pos + 1] == '=') {
                    try tokens.append(allocator, .{ .type = .less_equals, .text = "<=", .line = self.line, .column = self.column });
                    self.pos += 2;
                    self.column += 2;
                } else {
                    try tokens.append(allocator, .{ .type = .less_than, .text = "<", .line = self.line, .column = self.column });
                    self.pos += 1;
                    self.column += 1;
                }
                continue;
            }
            if (c == '>') {
                // Check for >=
                if (self.pos + 1 < self.source.len and self.source[self.pos + 1] == '=') {
                    try tokens.append(allocator, .{ .type = .greater_equals, .text = ">=", .line = self.line, .column = self.column });
                    self.pos += 2;
                    self.column += 2;
                } else {
                    try tokens.append(allocator, .{ .type = .greater_than, .text = ">", .line = self.line, .column = self.column });
                    self.pos += 1;
                    self.column += 1;
                }
                continue;
            }
            if (c == '+') {
                try tokens.append(allocator, .{ .type = .plus, .text = "+", .line = self.line, .column = self.column });
                self.pos += 1;
                self.column += 1;
                continue;
            }
            if (c == '-') {
                try tokens.append(allocator, .{ .type = .minus, .text = "-", .line = self.line, .column = self.column });
                self.pos += 1;
                self.column += 1;
                continue;
            }
            if (c == '*') {
                try tokens.append(allocator, .{ .type = .star, .text = "*", .line = self.line, .column = self.column });
                self.pos += 1;
                self.column += 1;
                continue;
            }
            if (c == '/') {
                // Check if it's a comment (already handled above, but double-check)
                if (self.pos + 1 < self.source.len and self.source[self.pos + 1] == '/') {
                    while (self.pos < self.source.len and self.source[self.pos] != '\n') {
                        self.pos += 1;
                        self.column += 1;
                    }
                    continue;
                }
                try tokens.append(allocator, .{ .type = .slash, .text = "/", .line = self.line, .column = self.column });
                self.pos += 1;
                self.column += 1;
                continue;
            }

            // String literal (single quotes for end labels)
            if (c == '\'') {
                const start_col = self.column;
                const start = self.pos + 1;
                self.pos += 1;
                self.column += 1;
                // UTF-8: scan bytes until closing quote
                while (self.pos < self.source.len and self.source[self.pos] != '\'') {
                    self.pos += 1;
                    self.column += 1;
                }
                const text = self.source[start..self.pos];
                self.pos += 1; // skip closing quote
                self.column += 1;
                try tokens.append(allocator, .{ .type = .string, .text = text, .line = self.line, .column = start_col });
                continue;
            }

            // Comma
            if (c == ',') {
                try tokens.append(allocator, .{ .type = .comma, .text = ",", .line = self.line, .column = self.column });
                self.pos += 1;
                self.column += 1;
                continue;
            }

            // Colon
            if (c == ':') {
                try tokens.append(allocator, .{ .type = .colon, .text = ":", .line = self.line, .column = self.column });
                self.pos += 1;
                self.column += 1;
                continue;
            }

            // Dot
            if (c == '.') {
                try tokens.append(allocator, .{ .type = .dot, .text = ".", .line = self.line, .column = self.column });
                self.pos += 1;
                self.column += 1;
                continue;
            }

            // Braces
            if (c == '{') {
                try tokens.append(allocator, .{ .type = .lbrace, .text = "{", .line = self.line, .column = self.column });
                self.pos += 1;
                self.column += 1;
                continue;
            }
            if (c == '}') {
                try tokens.append(allocator, .{ .type = .rbrace, .text = "}", .line = self.line, .column = self.column });
                self.pos += 1;
                self.column += 1;
                continue;
            }

            // Brackets
            if (c == '[') {
                try tokens.append(allocator, .{ .type = .lbracket, .text = "[", .line = self.line, .column = self.column });
                self.pos += 1;
                self.column += 1;
                continue;
            }
            if (c == ']') {
                try tokens.append(allocator, .{ .type = .rbracket, .text = "]", .line = self.line, .column = self.column });
                self.pos += 1;
                self.column += 1;
                continue;
            }

            // Number literal (integer or float)
            if (c >= '0' and c <= '9') {
                const start = self.pos;
                const start_col = self.column;
                while (self.pos < self.source.len and self.source[self.pos] >= '0' and self.source[self.pos] <= '9') {
                    self.pos += 1;
                    self.column += 1;
                }
                // Check for decimal point
                if (self.pos < self.source.len and self.source[self.pos] == '.') {
                    self.pos += 1;
                    self.column += 1;
                    while (self.pos < self.source.len and self.source[self.pos] >= '0' and self.source[self.pos] <= '9') {
                        self.pos += 1;
                        self.column += 1;
                    }
                    try tokens.append(allocator, .{ .type = .float_literal, .text = self.source[start..self.pos], .line = self.line, .column = start_col });
                } else {
                    try tokens.append(allocator, .{ .type = .integer, .text = self.source[start..self.pos], .line = self.line, .column = start_col });
                }
                continue;
            }

            // Identifier or keyword
            // Start: ASCII letter, underscore, or UTF-8 continuation byte (for unicode identifiers)
            if (isIdentifierStart(c)) {
                const start = self.pos;
                const start_col = self.column;
                self.pos += 1;
                self.column += 1;
                while (self.pos < self.source.len and isIdentifierContinue(self.source[self.pos])) {
                    self.pos += 1;
                    self.column += 1;
                }
                const text = self.source[start..self.pos];
                const token_type = getKeyword(text) orelse .identifier;
                try tokens.append(allocator, .{ .type = token_type, .text = text, .line = self.line, .column = start_col });
                continue;
            }

            // UTF-8 multi-byte sequence (non-ASCII) - could be identifier
            if (c >= 0x80) {
                const start = self.pos;
                const start_col = self.column;
                // Skip UTF-8 sequence
                const byte_len = utf8ByteLen(c);
                self.pos += byte_len;
                self.column += 1; // UTF-8 multi-byte counts as 1 column
                // Continue while identifier characters
                while (self.pos < self.source.len and isIdentifierContinue(self.source[self.pos])) {
                    if (self.source[self.pos] >= 0x80) {
                        self.pos += utf8ByteLen(self.source[self.pos]);
                        self.column += 1;
                    } else {
                        self.pos += 1;
                        self.column += 1;
                    }
                }
                const text = self.source[start..self.pos];
                try tokens.append(allocator, .{ .type = .identifier, .text = text, .line = self.line, .column = start_col });
                continue;
            }

            // Unknown character - skip
            self.pos += 1;
            self.column += 1;
        }

        try tokens.append(allocator, .{ .type = .eof, .text = "", .line = self.line, .column = self.column });
        return tokens.toOwnedSlice(allocator);
    }

    fn skipWhitespaceExceptNewline(self: *Lexer) void {
        while (self.pos < self.source.len) {
            const c = self.source[self.pos];
            if (c == ' ' or c == '\t' or c == '\r') {
                self.pos += 1;
                self.column += 1;
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
            .{ "mod", TokenType.mod },
            .{ "type", TokenType.type },
            .{ "enum", TokenType.@"enum" },
            .{ "array", TokenType.array },
            .{ "of", TokenType.of },
            .{ "if", TokenType.@"if" },
            .{ "else", TokenType.@"else" },
            .{ "while", TokenType.@"while" },
            .{ "for", TokenType.@"for" },
            .{ "in", TokenType.in },
            .{ "break", TokenType.@"break" },
            .{ "continue", TokenType.@"continue" },
            .{ "true", TokenType.true },
            .{ "false", TokenType.false },
            .{ "and", TokenType.@"and" },
            .{ "or", TokenType.@"or" },
            .{ "nil", TokenType.nil },
            .{ "int", TokenType.int },
            .{ "float", TokenType.float },
            // Type system keywords
            .{ "uses", TokenType.uses },
            .{ "is", TokenType.is },
            .{ "with", TokenType.with },
            .{ "static", TokenType.static },
            .{ "export", TokenType.@"export" },
            .{ "self", TokenType.self },
            .{ "Self", TokenType.Self },
            .{ "interface", TokenType.interface },
            .{ "extends", TokenType.extends },
            .{ "from", TokenType.from },
            .{ "to", TokenType.to },
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
