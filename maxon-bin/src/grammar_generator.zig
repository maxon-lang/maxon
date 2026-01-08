const std = @import("std");
const lexer = @import("compiler/1-lexer.zig");

/// Generate a TextMate grammar JSON for VS Code syntax highlighting
/// This generates only lexical patterns that the LSP server cannot handle.
/// The LSP server provides semantic tokens for function names, types, variables, etc.
pub fn generateGrammar(file: std.fs.File, allocator: std.mem.Allocator) !void {
    // Create keyword lists for each category
    const keyword_enum_info = @typeInfo(lexer.Lexer.KeywordCategory).@"enum";
    const keyword_category_count = keyword_enum_info.fields.len;
    var keyword_lists: [keyword_category_count]std.ArrayListUnmanaged([]const u8) = [_]std.ArrayListUnmanaged([]const u8){.empty} ** keyword_category_count;
    defer for (&keyword_lists) |*list| list.deinit(allocator);

    var block_label_keywords: std.ArrayListUnmanaged([]const u8) = .empty;
    defer block_label_keywords.deinit(allocator);

    // Create operator lists for each category
    const operator_enum_info = @typeInfo(lexer.Lexer.OperatorCategory).@"enum";
    const operator_category_count = operator_enum_info.fields.len;
    var operator_lists: [operator_category_count]std.ArrayListUnmanaged([]const u8) = [_]std.ArrayListUnmanaged([]const u8){.empty} ** operator_category_count;
    defer for (&operator_lists) |*list| list.deinit(allocator);

    // Extract keywords from lexer and categorize them
    inline for (lexer.Lexer.keyword_map) |entry| {
        const keyword_text = entry[0];
        const category = entry[2];
        const can_have_block_label = entry[4];

        try keyword_lists[@intFromEnum(category)].append(allocator, keyword_text);

        if (can_have_block_label) {
            try block_label_keywords.append(allocator, keyword_text);
        }
    }

    // Extract operators from lexer and categorize them
    inline for (lexer.Lexer.operator_map) |entry| {
        const operator_text = entry[0];
        const category = entry[2];
        try operator_lists[@intFromEnum(category)].append(allocator, operator_text);
    }

    const header =
        \\{
        \\    "$schema": "https://raw.githubusercontent.com/martinring/tmlanguage/master/tmlanguage.json",
        \\    "name": "Maxon",
        \\    "patterns": [
        \\        { "include": "#comments" },
        \\        { "include": "#block-labels" },
        \\        { "include": "#keywords" },
        \\        { "include": "#strings" },
        \\        { "include": "#characters" },
        \\        { "include": "#numbers" },
        \\        { "include": "#operators" }
        \\    ],
        \\    "repository": {
        \\        "comments": {
        \\            "patterns": [
        \\                { "name": "comment.line.double-slash.maxon", "match": "//.*$" },
        \\                { "name": "comment.block.maxon", "begin": "/\\*", "end": "\\*/" }
        \\            ]
        \\        },
        \\        "keywords": {
        \\            "patterns": [
        \\
    ;
    _ = try file.writeAll(header);

    // Generate keyword patterns dynamically by iterating through KeywordCategory enum
    var first_keyword_category = true;
    inline for (keyword_enum_info.fields, 0..) |field, category_idx| {
        const keywords = keyword_lists[category_idx].items;

        if (keywords.len > 0) {
            if (!first_keyword_category) {
                _ = try file.writeAll(",\n");
            }
            first_keyword_category = false;

            // Map category to TextMate scope name
            const scope_name = switch (@as(lexer.Lexer.KeywordCategory, @enumFromInt(field.value))) {
                .control => "keyword.control.maxon",
                .other => "keyword.other.maxon",
                .logical => "keyword.operator.logical.maxon",
                .constant => "constant.language.maxon",
                .type_keyword => "storage.type.maxon",
            };

            _ = try file.writeAll("                { \"name\": \"");
            _ = try file.writeAll(scope_name);
            _ = try file.writeAll("\", \"match\": \"\\\\b(");

            for (keywords, 0..) |kw, i| {
                _ = try file.writeAll(kw);
                if (i < keywords.len - 1) {
                    _ = try file.writeAll("|");
                }
            }

            _ = try file.writeAll(")\\\\b\" }");
        }
    }

    const footer1 =
        \\
        \\            ]
        \\        },
        \\        "block-labels": {
        \\            "patterns": [
        \\                {
        \\                    "comment": "Block labels like 'main' after end, if, then, while, for, or (guard-let), etc.",
        \\                    "match": "(\\b(?:
    ;
    _ = try file.writeAll(footer1);

    // Generate block label keywords pattern
    for (block_label_keywords.items, 0..) |kw, i| {
        _ = try file.writeAll(kw);
        if (i < block_label_keywords.items.len - 1) {
            _ = try file.writeAll("|");
        }
    }

    const footer2 =
        \\)\\b)\\s*('[^']*')",
        \\                    "captures": {
        \\                        "1": { "name": "keyword.control.maxon" },
        \\                        "2": { "name": "entity.name.label.maxon" }
        \\                    }
        \\                }
        \\            ]
        \\        },
        \\        "strings": {
        \\            "patterns": [
        \\                {
        \\                    "name": "string.quoted.double.maxon",
        \\                    "begin": "\"",
        \\                    "end": "\"",
        \\                    "patterns": [
        \\                        { "name": "constant.character.escape.maxon", "match": "\\\\." },
        \\                        {
        \\                            "name": "meta.interpolation.maxon",
        \\                            "begin": "\\{",
        \\                            "end": "\\}",
        \\                            "beginCaptures": { "0": { "name": "punctuation.section.interpolation.begin.maxon" } },
        \\                            "endCaptures": { "0": { "name": "punctuation.section.interpolation.end.maxon" } },
        \\                            "patterns": [
        \\                                { "include": "#keywords" },
        \\                                { "include": "#numbers" },
        \\                                { "include": "#operators" }
        \\                            ]
        \\                        }
        \\                    ]
        \\                }
        \\            ]
        \\        },
        \\        "characters": {
        \\            "patterns": [
        \\                {
        \\                    "comment": "Character literal with escape sequence",
        \\                    "name": "constant.character.maxon",
        \\                    "match": "'\\\\.'"
        \\                },
        \\                {
        \\                    "comment": "Single character literal",
        \\                    "name": "constant.character.maxon",
        \\                    "match": "'[^\\\\']'"
        \\                }
        \\            ]
        \\        },
        \\        "numbers": {
        \\            "patterns": [
        \\                { "name": "constant.numeric.hex.maxon", "match": "\\b0[xX][0-9a-fA-F](_?[0-9a-fA-F])*\\b" },
        \\                { "name": "constant.numeric.binary.maxon", "match": "\\b0[bB][01](_?[01])*\\b" },
        \\                { "name": "constant.numeric.octal.maxon", "match": "\\b0[oO][0-7](_?[0-7])*\\b" },
        \\                { "name": "constant.numeric.float.maxon", "match": "\\b[0-9](_?[0-9])*\\.[0-9](_?[0-9])*([eE][+-]?[0-9](_?[0-9])*)?\\b" },
        \\                { "name": "constant.numeric.byte.maxon", "match": "\\b[0-9](_?[0-9])*b\\b" },
        \\                { "name": "constant.numeric.integer.maxon", "match": "\\b[0-9](_?[0-9])*\\b" }
        \\            ]
        \\        },
        \\        "operators": {
        \\            "patterns": [
        \\
    ;
    _ = try file.writeAll(footer2);

    // Generate operator patterns dynamically by iterating through OperatorCategory enum
    var first_operator_category = true;
    inline for (operator_enum_info.fields, 0..) |field, category_idx| {
        const category = @as(lexer.Lexer.OperatorCategory, @enumFromInt(field.value));
        const operators = operator_lists[category_idx].items;

        if (operators.len > 0) {
            if (!first_operator_category) {
                _ = try file.writeAll(",\n");
            }
            first_operator_category = false;

            _ = try file.writeAll("                { \"name\": \"keyword.operator.");
            _ = try file.writeAll(field.name);
            _ = try file.writeAll(".maxon\", \"match\": \"");

            switch (category) {
                .arithmetic => {
                    // Arithmetic uses character class
                    _ = try file.writeAll("[");
                    for (operators) |op| {
                        for (op) |c| {
                            if (c == '-' or c == '\\') {
                                _ = try file.writeAll("\\\\");
                            }
                            _ = try file.writeAll(&[_]u8{c});
                        }
                    }
                    _ = try file.writeAll("]");
                },
                .bitwise, .assignment, .comparison => {
                    for (operators, 0..) |op, i| {
                        for (op) |c| {
                            if (c == '|' or c == '^') {
                                _ = try file.writeAll("\\\\");
                            }
                            _ = try file.writeAll(&[_]u8{c});
                        }
                        if (i < operators.len - 1) {
                            _ = try file.writeAll("|");
                        }
                    }
                },
            }

            _ = try file.writeAll("\" }");
        }
    }

    const footer3 =
        \\
        \\            ]
        \\        }
        \\    },
        \\    "scopeName": "source.maxon"
        \\}
        \\
    ;
    _ = try file.writeAll(footer3);
}
