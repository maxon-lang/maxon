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
    not,
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
    as, // type cast operator
    gives, // closure syntax: (x int) gives x * 2
    // Error handling keywords
    throws, // function throws error
    throw, // throw an error
    @"try", // try expression
    @"catch", // catch clause
    do, // do block for error handling
    // Match statement keywords
    match, // match statement/expression
    then, // case separator for statements
    fallthrough, // continue to next case body
    default, // default case

    // Types
    int,
    float,
    bool,
    byte,
    string,

    // Literals
    identifier,
    integer,
    float_literal,
    string_literal, // double-quoted string literals: "hello"
    string_interp, // string with interpolation: "hello {name}!"
    char_literal, // single-quoted literals: 'A' (used for both character literals and end labels)

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
    ampersand, // &
    pipe, // |
    caret, // ^
    left_shift, // <<
    right_shift, // >>

    // Formatting
    newline,

    // Special
    doc_comment, // /// or /** */ doc comments
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

            // Skip comments (but NOT doc comments - those are handled in '/' section)
            if (c == '/' and self.pos + 1 < self.source.len and self.source[self.pos + 1] == '/') {
                // Check if this is a doc comment (///) - if so, don't skip it here
                if (self.pos + 2 < self.source.len and self.source[self.pos + 2] == '/') {
                    // Fall through to '/' handling below which produces doc_comment tokens
                } else {
                    // Regular comment - skip
                    while (self.pos < self.source.len and self.source[self.pos] != '\n') {
                        self.pos += 1;
                        self.column += 1;
                    }
                    continue;
                }
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
                // Check for << or <=
                if (self.pos + 1 < self.source.len and self.source[self.pos + 1] == '<') {
                    try tokens.append(allocator, .{ .type = .left_shift, .text = "<<", .line = self.line, .column = self.column });
                    self.pos += 2;
                    self.column += 2;
                } else if (self.pos + 1 < self.source.len and self.source[self.pos + 1] == '=') {
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
                // Check for >> or >=
                if (self.pos + 1 < self.source.len and self.source[self.pos + 1] == '>') {
                    try tokens.append(allocator, .{ .type = .right_shift, .text = ">>", .line = self.line, .column = self.column });
                    self.pos += 2;
                    self.column += 2;
                } else if (self.pos + 1 < self.source.len and self.source[self.pos + 1] == '=') {
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
                // Check if it's a comment
                if (self.pos + 1 < self.source.len and self.source[self.pos + 1] == '/') {
                    // Check if it's a doc comment (///)
                    if (self.pos + 2 < self.source.len and self.source[self.pos + 2] == '/') {
                        const start_line = self.line;
                        const start_col = self.column;
                        // Skip the ///
                        self.pos += 3;
                        self.column += 3;
                        // Skip leading space if present
                        if (self.pos < self.source.len and self.source[self.pos] == ' ') {
                            self.pos += 1;
                            self.column += 1;
                        }
                        const text_start = self.pos;
                        // Collect the comment text until end of line
                        while (self.pos < self.source.len and self.source[self.pos] != '\n') {
                            self.pos += 1;
                            self.column += 1;
                        }
                        const text = self.source[text_start..self.pos];
                        try tokens.append(allocator, .{ .type = .doc_comment, .text = text, .line = start_line, .column = start_col });
                        continue;
                    }
                    // Regular comment - skip
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

            // Bitwise operators
            if (c == '&') {
                try tokens.append(allocator, .{ .type = .ampersand, .text = "&", .line = self.line, .column = self.column });
                self.pos += 1;
                self.column += 1;
                continue;
            }
            if (c == '|') {
                try tokens.append(allocator, .{ .type = .pipe, .text = "|", .line = self.line, .column = self.column });
                self.pos += 1;
                self.column += 1;
                continue;
            }
            if (c == '^') {
                try tokens.append(allocator, .{ .type = .caret, .text = "^", .line = self.line, .column = self.column });
                self.pos += 1;
                self.column += 1;
                continue;
            }

            // Character literal (single quotes) - used for both char literals and end labels
            // The parser distinguishes context (labels expect .char_literal after 'end')
            if (c == '\'') {
                const start_col = self.column;
                const start = self.pos + 1;
                self.pos += 1;
                self.column += 1;
                // UTF-8: scan bytes until closing quote, handling escape sequences
                while (self.pos < self.source.len and self.source[self.pos] != '\'') {
                    if (self.source[self.pos] == '\\' and self.pos + 1 < self.source.len) {
                        // Skip escape sequence
                        self.pos += 2;
                        self.column += 2;
                    } else {
                        self.pos += 1;
                        self.column += 1;
                    }
                }
                const text = self.source[start..self.pos];
                self.pos += 1; // skip closing quote
                self.column += 1;
                try tokens.append(allocator, .{ .type = .char_literal, .text = text, .line = self.line, .column = start_col });
                continue;
            }

            // Double-quoted string literal (may contain interpolation)
            if (c == '"') {
                const start_col = self.column;
                const start = self.pos + 1;
                self.pos += 1;
                self.column += 1;
                var has_interpolation = false;
                // UTF-8: scan bytes until closing quote, handling escape sequences
                while (self.pos < self.source.len and self.source[self.pos] != '"') {
                    if (self.source[self.pos] == '\\' and self.pos + 1 < self.source.len) {
                        // Skip escape sequence (including \{ and \})
                        self.pos += 2;
                        self.column += 2;
                    } else if (self.source[self.pos] == '{') {
                        // Unescaped { means interpolation
                        has_interpolation = true;
                        self.pos += 1;
                        self.column += 1;
                    } else {
                        self.pos += 1;
                        self.column += 1;
                    }
                }
                const text = self.source[start..self.pos];
                self.pos += 1; // skip closing quote
                self.column += 1;
                const token_type: TokenType = if (has_interpolation) .string_interp else .string_literal;
                try tokens.append(allocator, .{ .type = token_type, .text = text, .line = self.line, .column = start_col });
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

                // Check for hex literal (0x or 0X)
                if (c == '0' and self.pos + 1 < self.source.len and
                    (self.source[self.pos + 1] == 'x' or self.source[self.pos + 1] == 'X'))
                {
                    self.pos += 2; // skip 0x
                    self.column += 2;
                    while (self.pos < self.source.len and (isHexDigit(self.source[self.pos]) or self.source[self.pos] == '_')) {
                        self.pos += 1;
                        self.column += 1;
                    }
                    try tokens.append(allocator, .{ .type = .integer, .text = self.source[start..self.pos], .line = self.line, .column = start_col });
                    continue;
                }

                // Check for binary literal (0b or 0B)
                if (c == '0' and self.pos + 1 < self.source.len and
                    (self.source[self.pos + 1] == 'b' or self.source[self.pos + 1] == 'B'))
                {
                    self.pos += 2; // skip 0b
                    self.column += 2;
                    while (self.pos < self.source.len and (isBinaryDigit(self.source[self.pos]) or self.source[self.pos] == '_')) {
                        self.pos += 1;
                        self.column += 1;
                    }
                    try tokens.append(allocator, .{ .type = .integer, .text = self.source[start..self.pos], .line = self.line, .column = start_col });
                    continue;
                }

                // Check for octal literal (0o or 0O)
                if (c == '0' and self.pos + 1 < self.source.len and
                    (self.source[self.pos + 1] == 'o' or self.source[self.pos + 1] == 'O'))
                {
                    self.pos += 2; // skip 0o
                    self.column += 2;
                    while (self.pos < self.source.len and (isOctalDigit(self.source[self.pos]) or self.source[self.pos] == '_')) {
                        self.pos += 1;
                        self.column += 1;
                    }
                    try tokens.append(allocator, .{ .type = .integer, .text = self.source[start..self.pos], .line = self.line, .column = start_col });
                    continue;
                }

                // Decimal integer or float - allow underscores as separators
                while (self.pos < self.source.len and (isDecimalDigit(self.source[self.pos]) or self.source[self.pos] == '_')) {
                    self.pos += 1;
                    self.column += 1;
                }
                // Check for decimal point
                var is_float = false;
                if (self.pos < self.source.len and self.source[self.pos] == '.') {
                    is_float = true;
                    self.pos += 1;
                    self.column += 1;
                    while (self.pos < self.source.len and (isDecimalDigit(self.source[self.pos]) or self.source[self.pos] == '_')) {
                        self.pos += 1;
                        self.column += 1;
                    }
                }
                // Check for scientific notation (e.g., 1.5e10, 2.0e-3, 3.0E+5)
                if (self.pos < self.source.len and (self.source[self.pos] == 'e' or self.source[self.pos] == 'E')) {
                    is_float = true;
                    self.pos += 1;
                    self.column += 1;
                    // Optional sign
                    if (self.pos < self.source.len and (self.source[self.pos] == '+' or self.source[self.pos] == '-')) {
                        self.pos += 1;
                        self.column += 1;
                    }
                    // Exponent digits
                    while (self.pos < self.source.len and (isDecimalDigit(self.source[self.pos]) or self.source[self.pos] == '_')) {
                        self.pos += 1;
                        self.column += 1;
                    }
                }
                if (is_float) {
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

    pub const KeywordCategory = enum {
        control,
        other,
        logical,
        constant,
        type_keyword,
    };

    // Keyword map structure: { keyword_text, TokenType, KeywordCategory, help_text, can_have_block_label }
    pub const keyword_map = .{
        .{ "function", TokenType.function, KeywordCategory.other, "Declares a function. Functions contain executable code and can return values.\n\nExample:\n```maxon\nfunction add(a int, b int) returns int\n    return a + b\nend 'add'\n```", false },
        .{ "returns", TokenType.returns, KeywordCategory.other, "Specifies the return type of a function.", false },
        .{ "return", TokenType.@"return", KeywordCategory.control, "Returns a value from a function and exits the function.", false },
        .{ "end", TokenType.end, KeywordCategory.control, "Ends a block (function, type, if, for, while, etc.). Must be followed by the block's label in quotes.", true },
        .{ "let", TokenType.let, KeywordCategory.other, "Declares an immutable variable. The value cannot be changed after initialization.", false },
        .{ "var", TokenType.@"var", KeywordCategory.other, "Declares a mutable variable. The value can be changed after initialization.", false },
        .{ "mod", TokenType.mod, KeywordCategory.logical, "Modulo operator. Returns the remainder of division.", false },
        .{ "type", TokenType.type, KeywordCategory.other, "Declares a struct type with fields and methods.\n\nExample:\n```maxon\ntype Point\n    var x int\n    var y int\nend 'Point'\n```", false },
        .{ "enum", TokenType.@"enum", KeywordCategory.other, "Declares an enumeration type with a fixed set of cases.\n\nExample:\n```maxon\nenum Color\n    red\n    green\n    blue\nend 'Color'\n```", false },
        .{ "of", TokenType.of, KeywordCategory.type_keyword, "Used in array type declarations (array of int).", false },
        .{ "if", TokenType.@"if", KeywordCategory.control, "Conditional statement. Executes code if the condition is true.", true },
        .{ "else", TokenType.@"else", KeywordCategory.control, "Alternative branch in an if statement. Executed when the condition is false.", true },
        .{ "while", TokenType.@"while", KeywordCategory.control, "Loop that continues while the condition is true.", true },
        .{ "for", TokenType.@"for", KeywordCategory.control, "Loop that iterates over a range or collection.", true },
        .{ "in", TokenType.in, KeywordCategory.control, "Used in for loops to specify the range or collection to iterate over.", false },
        .{ "break", TokenType.@"break", KeywordCategory.control, "Exits the current loop immediately.", false },
        .{ "continue", TokenType.@"continue", KeywordCategory.control, "Skips the rest of the current loop iteration and continues with the next iteration.", false },
        .{ "true", TokenType.true, KeywordCategory.constant, "Boolean literal representing true.", false },
        .{ "false", TokenType.false, KeywordCategory.constant, "Boolean literal representing false.", false },
        .{ "and", TokenType.@"and", KeywordCategory.logical, "Logical AND operator. Returns true if both operands are true.", false },
        .{ "or", TokenType.@"or", KeywordCategory.logical, "Logical OR operator. Returns true if either operand is true.", true },
        .{ "not", TokenType.not, KeywordCategory.logical, "Logical NOT operator. Negates a boolean value.", false },
        .{ "nil", TokenType.nil, KeywordCategory.constant, "Represents the absence of a value for optional types.", false },
        .{ "int", TokenType.int, KeywordCategory.type_keyword, "Primitive integer type (64-bit signed).", false },
        .{ "float", TokenType.float, KeywordCategory.type_keyword, "Primitive floating-point type (64-bit double precision).", false },
        .{ "bool", TokenType.bool, KeywordCategory.type_keyword, "Primitive boolean type (true or false).", false },
        .{ "byte", TokenType.byte, KeywordCategory.type_keyword, "Primitive byte type (8-bit unsigned integer).", false },
        .{ "uses", TokenType.uses, KeywordCategory.other, "Declares associated types in an interface.", false },
        .{ "is", TokenType.is, KeywordCategory.logical, "Specifies that a type conforms to an interface.", false },
        .{ "with", TokenType.with, KeywordCategory.other, "Specifies interface conformance requirements.", false },
        .{ "static", TokenType.static, KeywordCategory.other, "Declares a static method that doesn't require an instance.", false },
        .{ "export", TokenType.@"export", KeywordCategory.other, "Makes a function or type visible to other modules.", false },
        .{ "self", TokenType.self, KeywordCategory.other, "Refers to the current instance in a method.", false },
        .{ "Self", TokenType.Self, KeywordCategory.other, "Refers to the current type in a method signature.", false },
        .{ "interface", TokenType.interface, KeywordCategory.other, "Declares an interface that types can conform to.\n\nExample:\n```maxon\ninterface Printable\n    function print() returns int\nend 'Printable'\n```", false },
        .{ "extends", TokenType.extends, KeywordCategory.other, "Indicates interface inheritance.", false },
        .{ "from", TokenType.from, KeywordCategory.other, "Used in range expressions (from X to Y).", false },
        .{ "to", TokenType.to, KeywordCategory.other, "Used in range expressions (from X to Y).", false },
        .{ "as", TokenType.as, KeywordCategory.logical, "Type cast operator. Converts a value to a different type.", false },
        .{ "gives", TokenType.gives, KeywordCategory.control, "Used in iterator expressions.", false },
        .{ "throws", TokenType.throws, KeywordCategory.other, "Indicates that a function may throw an error.", false },
        .{ "throw", TokenType.throw, KeywordCategory.control, "Throws an error that can be caught by a try-catch block.", false },
        .{ "try", TokenType.@"try", KeywordCategory.control, "Attempts an operation that may throw an error.", false },
        .{ "catch", TokenType.@"catch", KeywordCategory.control, "Handles errors thrown in a try block.", false },
        .{ "do", TokenType.do, KeywordCategory.control, "Used in do-while loops or do-catch blocks.", false },
        .{ "match", TokenType.match, KeywordCategory.control, "Pattern matching statement for enums and values.", false },
        .{ "then", TokenType.then, KeywordCategory.control, "Used in match expressions to separate pattern from result.", true },
        .{ "fallthrough", TokenType.fallthrough, KeywordCategory.control, "Falls through to the next case in a match statement.", false },
        .{ "default", TokenType.default, KeywordCategory.control, "Default case in a match statement.", false },
    };

    pub const OperatorCategory = enum {
        bitwise,
        comparison,
        arithmetic,
        assignment,
    };

    // Operator map structure: { operator_text, TokenType, OperatorCategory, help_text }
    pub const operator_map = .{
        .{ "<<", TokenType.left_shift, OperatorCategory.bitwise, "Bitwise left shift operator. Shifts bits to the left." },
        .{ ">>", TokenType.right_shift, OperatorCategory.bitwise, "Bitwise right shift operator. Shifts bits to the right." },
        .{ "&", TokenType.ampersand, OperatorCategory.bitwise, "Bitwise AND operator. Performs bitwise AND operation." },
        .{ "|", TokenType.pipe, OperatorCategory.bitwise, "Bitwise OR operator. Performs bitwise OR operation." },
        .{ "^", TokenType.caret, OperatorCategory.bitwise, "Bitwise XOR operator. Performs bitwise exclusive OR operation." },
        .{ "==", TokenType.equals_equals, OperatorCategory.comparison, "Equality operator. Returns true if operands are equal." },
        .{ "!=", TokenType.not_equals, OperatorCategory.comparison, "Inequality operator. Returns true if operands are not equal." },
        .{ ">=", TokenType.greater_equals, OperatorCategory.comparison, "Greater than or equal operator. Returns true if left operand is greater than or equal to right operand." },
        .{ ">", TokenType.greater_than, OperatorCategory.comparison, "Greater than operator. Returns true if left operand is greater than right operand." },
        .{ "<=", TokenType.less_equals, OperatorCategory.comparison, "Less than or equal operator. Returns true if left operand is less than or equal to right operand." },
        .{ "<", TokenType.less_than, OperatorCategory.comparison, "Less than operator. Returns true if left operand is less than right operand." },
        .{ "+", TokenType.plus, OperatorCategory.arithmetic, "Addition operator. Adds two numbers." },
        .{ "-", TokenType.minus, OperatorCategory.arithmetic, "Subtraction operator. Subtracts right operand from left operand." },
        .{ "*", TokenType.star, OperatorCategory.arithmetic, "Multiplication operator. Multiplies two numbers." },
        .{ "/", TokenType.slash, OperatorCategory.arithmetic, "Division operator. Divides left operand by right operand." },
        .{ "=", TokenType.equals, OperatorCategory.assignment, "Assignment operator. Assigns the value on the right to the variable on the left." },
    };

    fn getKeyword(text: []const u8) ?TokenType {
        const keywords = keyword_map;
        inline for (keywords) |kw| {
            if (std.mem.eql(u8, text, kw[0])) {
                return kw[1];
            }
        }
        return null;
    }

    fn tryMatchOperator(self: *const Lexer) ?struct { token_type: TokenType, text: []const u8 } {
        // Try to match operators from longest to shortest
        inline for (operator_map) |op| {
            const op_text = op[0];
            const token_type = op[1];
            if (self.pos + op_text.len <= self.source.len) {
                if (std.mem.eql(u8, self.source[self.pos .. self.pos + op_text.len], op_text)) {
                    return .{ .token_type = token_type, .text = op_text };
                }
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

    fn isHexDigit(c: u8) bool {
        return (c >= '0' and c <= '9') or (c >= 'a' and c <= 'f') or (c >= 'A' and c <= 'F');
    }

    fn isBinaryDigit(c: u8) bool {
        return c == '0' or c == '1';
    }

    fn isOctalDigit(c: u8) bool {
        return c >= '0' and c <= '7';
    }

    fn isDecimalDigit(c: u8) bool {
        return c >= '0' and c <= '9';
    }

    fn utf8ByteLen(first_byte: u8) usize {
        if (first_byte < 0x80) return 1;
        if (first_byte < 0xE0) return 2;
        if (first_byte < 0xF0) return 3;
        return 4;
    }
};
