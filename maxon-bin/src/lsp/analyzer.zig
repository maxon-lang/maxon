const std = @import("std");
const compiler = @import("../compiler/0-compiler.zig");
const ast_to_ir = @import("../compiler/4-ast_to_ir.zig");
const types = @import("types.zig");

fn log(comptime fmt: []const u8, args: anytype) void {
    std.debug.print("[LSP:Analyzer] " ++ fmt ++ "\n", args);
}

pub const Document = struct {
    uri: []const u8,
    content: []const u8,
    version: i64,

    pub fn deinit(self: *Document, allocator: std.mem.Allocator) void {
        allocator.free(self.uri);
        allocator.free(self.content);
    }
};

pub const Analyzer = struct {
    allocator: std.mem.Allocator,
    documents: std.StringHashMapUnmanaged(Document) = .{},
    semantic_cache: std.StringHashMapUnmanaged(ast_to_ir.SemanticInfo) = .{},

    pub fn init(allocator: std.mem.Allocator) Analyzer {
        return .{ .allocator = allocator };
    }

    pub fn deinit(self: *Analyzer) void {
        var doc_it = self.documents.iterator();
        while (doc_it.next()) |entry| {
            var doc = entry.value_ptr.*;
            doc.deinit(self.allocator);
        }
        self.documents.deinit(self.allocator);

        var cache_it = self.semantic_cache.iterator();
        while (cache_it.next()) |entry| {
            var info = entry.value_ptr.*;
            info.deinit();
        }
        self.semantic_cache.deinit(self.allocator);
    }

    pub fn openDocument(self: *Analyzer, uri: []const u8, content: []const u8, version: i64) !void {
        const uri_copy = try self.allocator.dupe(u8, uri);
        errdefer self.allocator.free(uri_copy);

        const content_copy = try self.allocator.dupe(u8, content);
        errdefer self.allocator.free(content_copy);

        const doc = Document{ .uri = uri_copy, .content = content_copy, .version = version };
        try self.documents.put(self.allocator, uri_copy, doc);

        // Eagerly analyze to populate cache
        _ = self.getSemanticInfo(uri_copy) catch |err| {
            log("Semantic analysis failed: {}", .{err});
        };
    }

    pub fn updateDocument(self: *Analyzer, uri: []const u8, content: []const u8, version: i64) !void {
        if (self.documents.getPtr(uri)) |doc| {
            // Store old content in case analysis fails
            const old_content = doc.content;
            doc.content = try self.allocator.dupe(u8, content);
            doc.version = version;

            // Try to re-analyze - if successful, invalidate old cache
            const new_info = compiler.analyzeForLSP(doc.content, null, self.allocator) catch |err| {
                log("Semantic analysis failed: {} (keeping previous cache)", .{err});
                // Keep old cache, just free old content
                self.allocator.free(old_content);
                return;
            };

            // Analysis succeeded - replace old cache with new
            if (self.semantic_cache.fetchRemove(uri)) |kv| {
                var old_info = kv.value;
                old_info.deinit();
            }
            self.semantic_cache.put(self.allocator, uri, new_info) catch {
                // If we fail to cache the new info, clean it up
                var info_to_free = new_info;
                info_to_free.deinit();
            };
            self.allocator.free(old_content);
        }
    }

    pub fn closeDocument(self: *Analyzer, uri: []const u8) void {
        // Remove from semantic cache
        if (self.semantic_cache.fetchRemove(uri)) |kv| {
            var info = kv.value;
            info.deinit();
        }

        if (self.documents.fetchRemove(uri)) |kv| {
            var doc = kv.value;
            doc.deinit(self.allocator);
        }
    }

    fn getSemanticInfo(self: *Analyzer, uri: []const u8) !*ast_to_ir.SemanticInfo {
        if (self.semantic_cache.getPtr(uri)) |cached| {
            return cached;
        }

        const doc = self.documents.get(uri) orelse return error.DocumentNotFound;
        log("Running semantic analysis for {s}...", .{uri});

        var info = compiler.analyzeForLSP(doc.content, null, self.allocator) catch |err| {
            log("Semantic analysis error: {}", .{err});
            return err;
        };
        errdefer info.deinit();

        try self.semantic_cache.put(self.allocator, uri, info);
        log("Semantic analysis complete: {d} variables", .{info.variables.len});
        return self.semantic_cache.getPtr(uri).?;
    }

    pub fn inferTypeAtPosition(self: *Analyzer, doc_uri: []const u8, _: u32, _: u32, var_name: []const u8) ?[]const u8 {
        const info = self.getSemanticInfo(doc_uri) catch return null;

        // Find the variable by name
        if (info.findVariable(var_name)) |v| {
            return v.ty.getTypeName() orelse v.ty.toDisplayName();
        }
        return null;
    }

    pub fn findType(self: *Analyzer, doc_uri: []const u8, type_name: []const u8) ?ast_to_ir.TypeInfo {
        const info = self.getSemanticInfo(doc_uri) catch return null;
        return info.getType(type_name);
    }

    pub fn getCompletionsForVariable(self: *Analyzer, doc_uri: []const u8, var_name: []const u8) ![]types.CompletionItem {
        const info = self.getSemanticInfo(doc_uri) catch return &.{};
        const v = info.findVariable(var_name) orelse return &.{};
        return self.getCompletionsForType(info, v.ty);
    }

    fn getCompletionsForType(self: *Analyzer, info: *ast_to_ir.SemanticInfo, ty: ast_to_ir.ValueType) ![]types.CompletionItem {
        var items: std.ArrayListUnmanaged(types.CompletionItem) = .empty;

        // Get type name for method lookup
        // For arrays, build monomorphized name like "Array$int"
        var type_name_buf: [256]u8 = undefined;
        const type_name: []const u8 = switch (ty) {
            .primitive => |name| name,
            .struct_type => |name| name,
            .enum_type => |name| name,
            .array_type => |arr| blk: {
                // Build monomorphized array type name
                // Use Maxon type names (int, float) not IR type names (i64, f64)
                const elem_name = arr.element_struct_type orelse switch (arr.element_type) {
                    .i64 => "int",
                    .i32 => "int",
                    .i8 => "byte",
                    .f64 => "float",
                    .ptr => "ptr",
                    .void => "void",
                };
                break :blk std.fmt.bufPrint(&type_name_buf, "Array${s}", .{elem_name}) catch "Array";
            },
            .optional_type => return &.{}, // Optionals don't have methods
            .error_union_type => return &.{},
            .function_type => return &.{},
        };

        // Get fields for struct types
        if (info.getType(type_name)) |type_info| {
            switch (type_info) {
                .struct_type => |st| {
                    for (st.fields) |field| {
                        // Skip internal fields (starting with _ or __)
                        if (!std.mem.startsWith(u8, field.name, "__") and !std.mem.startsWith(u8, field.name, "_")) {
                            try items.append(self.allocator, .{
                                .label = field.name,
                                .kind = .field,
                                .detail = field.value_type.toDisplayName(),
                            });
                        }
                    }
                },
                else => {},
            }
        }

        // Look up methods from func_map (methods are registered as "Type$method")
        var prefix_buf: [256]u8 = undefined;
        const prefix = std.fmt.bufPrint(&prefix_buf, "{s}$", .{type_name}) catch return items.toOwnedSlice(self.allocator);

        var func_iter = info.functions.iterator();
        while (func_iter.next()) |entry| {
            const name = entry.key_ptr.*;
            if (std.mem.startsWith(u8, name, prefix)) {
                const method_name = name[prefix.len..];
                try items.append(self.allocator, .{
                    .label = method_name,
                    .kind = .method,
                    .detail = formatFuncSignature(entry.value_ptr.*),
                });
            }
        }

        // Return empty literal if no items found, otherwise return allocated slice
        if (items.items.len == 0) {
            items.deinit(self.allocator);
            return &.{};
        }
        return items.toOwnedSlice(self.allocator);
    }

    /// Result of a definition lookup
    pub const DefinitionLocation = struct {
        uri: []const u8,
        line: u32,
        character: u32,
    };

    /// Find the definition of the symbol at the given position
    pub fn findDefinition(self: *Analyzer, uri: []const u8, line: u32, character: u32) ?DefinitionLocation {
        const doc = self.documents.get(uri) orelse return null;

        // Get the identifier at position
        const word = getWordAtPosition(doc.content, line, character) orelse return null;

        // Get semantic info for AST-based lookups
        const info = self.getSemanticInfo(uri) catch return null;

        // 1. Check if it's a variable reference
        if (info.findVariable(word)) |v| {
            // Return definition location (convert from 1-based to 0-based)
            return .{
                .uri = uri,
                .line = if (v.decl_line > 0) v.decl_line - 1 else 0,
                .character = if (v.decl_column > 0) v.decl_column - 1 else 0,
            };
        }

        // 2. Check if it's a function call - use AST-based lookup
        if (info.functions.get(word)) |func_info| {
            if (func_info.decl_line > 0) {
                return .{
                    .uri = uri,
                    .line = func_info.decl_line - 1,
                    .character = if (func_info.decl_column > 0) func_info.decl_column - 1 else 0,
                };
            }
        }

        // 3. Check if it's a type reference - use AST-based lookup
        if (info.types.get(word)) |type_info| {
            switch (type_info) {
                .struct_type => |st| {
                    if (st.decl_line > 0) {
                        return .{
                            .uri = uri,
                            .line = st.decl_line - 1,
                            .character = if (st.decl_column > 0) st.decl_column - 1 else 0,
                        };
                    }
                },
                else => {},
            }
        }

        return null;
    }

    pub fn getMemberCompletions(self: *Analyzer, doc_uri: []const u8, type_name: []const u8) ![]types.CompletionItem {
        const info = self.getSemanticInfo(doc_uri) catch return &.{};

        const type_info = info.getType(type_name) orelse {
            log("Type not found: {s}", .{type_name});
            return &.{};
        };

        var items: std.ArrayListUnmanaged(types.CompletionItem) = .empty;

        switch (type_info) {
            .struct_type => |st| {
                for (st.fields) |field| {
                    // Skip internal fields (starting with _ or __)
                    if (!std.mem.startsWith(u8, field.name, "__") and !std.mem.startsWith(u8, field.name, "_")) {
                        try items.append(self.allocator, .{
                            .label = field.name,
                            .kind = .field,
                            .detail = field.value_type.toDisplayName(),
                        });
                    }
                }
            },
            else => {},
        }

        // Look up methods from func_map (methods are registered as "Type$method")
        var prefix_buf: [256]u8 = undefined;
        const prefix = std.fmt.bufPrint(&prefix_buf, "{s}$", .{type_name}) catch return items.toOwnedSlice(self.allocator);

        var func_iter = info.functions.iterator();
        while (func_iter.next()) |entry| {
            const name = entry.key_ptr.*;
            if (std.mem.startsWith(u8, name, prefix)) {
                const method_name = name[prefix.len..];
                try items.append(self.allocator, .{
                    .label = method_name,
                    .kind = .method,
                    .detail = formatFuncSignature(entry.value_ptr.*),
                });
            }
        }

        // Return empty literal if no items found, otherwise return allocated slice
        if (items.items.len == 0) {
            items.deinit(self.allocator);
            return &.{};
        }
        return items.toOwnedSlice(self.allocator);
    }

    /// Get all document symbols (functions, types, etc.) for outline view
    pub fn getDocumentSymbols(self: *Analyzer, uri: []const u8) ![]types.SymbolInformation {
        const doc = self.documents.get(uri) orelse return &.{};
        const info = self.getSemanticInfo(uri) catch return &.{};

        var symbols: std.ArrayListUnmanaged(types.SymbolInformation) = .empty;
        errdefer symbols.deinit(self.allocator);

        // Add functions
        var func_iter = info.functions.iterator();
        while (func_iter.next()) |entry| {
            const name = entry.key_ptr.*;
            const func_info = entry.value_ptr.*;

            // Skip methods (contain $) and intrinsics (start with __)
            if (std.mem.indexOf(u8, name, "$") != null) continue;
            if (std.mem.startsWith(u8, name, "__")) continue;

            // Only include if it has valid location
            if (func_info.decl_line > 0) {
                try symbols.append(self.allocator, .{
                    .name = name,
                    .kind = .function,
                    .location = .{
                        .uri = uri,
                        .range = .{
                            .start = .{
                                .line = func_info.decl_line - 1,
                                .character = if (func_info.decl_column > 0) func_info.decl_column - 1 else 0,
                            },
                            .end = .{
                                .line = func_info.decl_line - 1,
                                .character = if (func_info.decl_column > 0) func_info.decl_column - 1 else 0,
                            },
                        },
                    },
                });
            }
        }

        // Add types (structs)
        var type_iter = info.types.iterator();
        while (type_iter.next()) |entry| {
            const name = entry.key_ptr.*;
            const type_info = entry.value_ptr.*;

            // Skip internal types and monomorphized types (contain $)
            if (std.mem.indexOf(u8, name, "$") != null) continue;
            if (std.mem.startsWith(u8, name, "__")) continue;

            switch (type_info) {
                .struct_type => |st| {
                    if (st.decl_line > 0) {
                        try symbols.append(self.allocator, .{
                            .name = name,
                            .kind = .@"struct",
                            .location = .{
                                .uri = uri,
                                .range = .{
                                    .start = .{
                                        .line = st.decl_line - 1,
                                        .character = if (st.decl_column > 0) st.decl_column - 1 else 0,
                                    },
                                    .end = .{
                                        .line = st.decl_line - 1,
                                        .character = if (st.decl_column > 0) st.decl_column - 1 else 0,
                                    },
                                },
                            },
                        });
                    }
                },
                .enum_type => |et| {
                    if (et.decl_line > 0) {
                        try symbols.append(self.allocator, .{
                            .name = name,
                            .kind = .@"enum",
                            .location = .{
                                .uri = uri,
                                .range = .{
                                    .start = .{
                                        .line = et.decl_line - 1,
                                        .character = if (et.decl_column > 0) et.decl_column - 1 else 0,
                                    },
                                    .end = .{
                                        .line = et.decl_line - 1,
                                        .character = if (et.decl_column > 0) et.decl_column - 1 else 0,
                                    },
                                },
                            },
                        });
                    }
                },
                else => {},
            }
        }

        // Add interfaces
        var intf_iter = info.interfaces.iterator();
        while (intf_iter.next()) |entry| {
            const name = entry.key_ptr.*;
            const intf_info = entry.value_ptr.*;

            if (intf_info.decl_line > 0) {
                try symbols.append(self.allocator, .{
                    .name = name,
                    .kind = .interface,
                    .location = .{
                        .uri = uri,
                        .range = .{
                            .start = .{
                                .line = intf_info.decl_line - 1,
                                .character = if (intf_info.decl_column > 0) intf_info.decl_column - 1 else 0,
                            },
                            .end = .{
                                .line = intf_info.decl_line - 1,
                                .character = if (intf_info.decl_column > 0) intf_info.decl_column - 1 else 0,
                            },
                        },
                    },
                });
            }
        }

        // Use a text-based fallback if no symbols from semantic info
        if (symbols.items.len == 0) {
            // Search for functions and types in document
            try self.findSymbolsInDocument(doc.content, uri, &symbols);
        }

        if (symbols.items.len == 0) {
            return &.{};
        }
        return symbols.toOwnedSlice(self.allocator);
    }

    fn findSymbolsInDocument(self: *Analyzer, content: []const u8, uri: []const u8, symbols: *std.ArrayListUnmanaged(types.SymbolInformation)) !void {
        var lines = std.mem.splitScalar(u8, content, '\n');
        var line_num: u32 = 0;

        while (lines.next()) |line_content| : (line_num += 1) {
            const trimmed = std.mem.trimLeft(u8, line_content, " \t");

            // Check for function declarations
            if (std.mem.startsWith(u8, trimmed, "function ") or std.mem.startsWith(u8, trimmed, "export function ")) {
                const func_start = if (std.mem.startsWith(u8, trimmed, "export function "))
                    trimmed["export function ".len..]
                else
                    trimmed["function ".len..];

                const paren_pos = std.mem.indexOf(u8, func_start, "(");
                const func_name = if (paren_pos) |p| func_start[0..p] else func_start;
                const func_name_trimmed = std.mem.trim(u8, func_name, " \t");

                if (func_name_trimmed.len > 0) {
                    const keyword_pos = std.mem.indexOf(u8, line_content, "function ") orelse 0;
                    try symbols.append(self.allocator, .{
                        .name = func_name_trimmed,
                        .kind = .function,
                        .location = .{
                            .uri = uri,
                            .range = .{
                                .start = .{ .line = line_num, .character = @intCast(keyword_pos + "function ".len) },
                                .end = .{ .line = line_num, .character = @intCast(keyword_pos + "function ".len + func_name_trimmed.len) },
                            },
                        },
                    });
                }
            }
            // Check for type declarations
            else if (std.mem.startsWith(u8, trimmed, "type ") or std.mem.startsWith(u8, trimmed, "export type ")) {
                const type_start = if (std.mem.startsWith(u8, trimmed, "export type "))
                    trimmed["export type ".len..]
                else
                    trimmed["type ".len..];

                var end: usize = 0;
                while (end < type_start.len and (std.ascii.isAlphanumeric(type_start[end]) or type_start[end] == '_')) {
                    end += 1;
                }

                if (end > 0) {
                    const type_name = type_start[0..end];
                    const keyword_pos = if (std.mem.indexOf(u8, line_content, "export type ")) |pos|
                        pos + "export type ".len
                    else if (std.mem.indexOf(u8, line_content, "type ")) |pos|
                        pos + "type ".len
                    else
                        0;

                    try symbols.append(self.allocator, .{
                        .name = type_name,
                        .kind = .@"struct",
                        .location = .{
                            .uri = uri,
                            .range = .{
                                .start = .{ .line = line_num, .character = @intCast(keyword_pos) },
                                .end = .{ .line = line_num, .character = @intCast(keyword_pos + type_name.len) },
                            },
                        },
                    });
                }
            }
        }
    }

    /// Get a document by URI
    pub fn getDocument(self: *Analyzer, uri: []const u8) ?Document {
        return self.documents.get(uri);
    }

    /// Format a document and return the formatted text
    pub fn formatDocument(self: *Analyzer, uri: []const u8, tab_size: u32, insert_spaces: bool) ?[]const u8 {
        const doc = self.documents.get(uri) orelse return null;
        return formatContent(self.allocator, doc.content, tab_size, insert_spaces) catch null;
    }

    /// Get folding ranges for a document
    pub fn getFoldingRanges(self: *Analyzer, uri: []const u8) ![]types.FoldingRange {
        const doc = self.documents.get(uri) orelse return &.{};

        var ranges: std.ArrayListUnmanaged(types.FoldingRange) = .empty;
        try findFoldingRanges(doc.content, self.allocator, &ranges);

        if (ranges.items.len == 0) {
            return &.{};
        }
        return ranges.toOwnedSlice(self.allocator);
    }

    /// Get linked editing ranges for a position in a document
    pub fn getLinkedEditingRanges(self: *Analyzer, uri: []const u8, line: u32, character: u32) ?types.LinkedEditingRanges {
        const doc = self.documents.get(uri) orelse return null;
        return findLinkedEditingRanges(self.allocator, doc.content, line, character) catch null;
    }
};

// ============================================================================
// Formatting Implementation
// ============================================================================

/// Format document content with proper indentation
fn formatContent(allocator: std.mem.Allocator, content: []const u8, tab_size: u32, insert_spaces: bool) ![]const u8 {
    var result: std.ArrayListUnmanaged(u8) = .empty;
    errdefer result.deinit(allocator);

    // Create indent string based on options
    var indent_buf: [16]u8 = undefined;
    const indent_str: []const u8 = if (insert_spaces) blk: {
        const size = @min(tab_size, indent_buf.len);
        @memset(indent_buf[0..size], ' ');
        break :blk indent_buf[0..size];
    } else "\t";

    // Collect all lines first for lookahead
    var all_lines: std.ArrayListUnmanaged([]const u8) = .empty;
    defer all_lines.deinit(allocator);

    var lines = std.mem.splitScalar(u8, content, '\n');
    while (lines.next()) |line| {
        try all_lines.append(allocator, line);
    }

    var depth: u32 = 0;
    var first_line = true;

    for (all_lines.items, 0..) |line, line_idx| {
        const trimmed = std.mem.trim(u8, line, " \t\r");

        // Add newline before all lines except the first
        if (!first_line) {
            try result.append(allocator, '\n');
        }
        first_line = false;

        // Empty lines - just leave empty (preserves blank lines)
        if (trimmed.len == 0) {
            continue;
        }

        // Check for block-ending keywords that reduce indent before the line
        const is_end_line = std.mem.startsWith(u8, trimmed, "end ") or std.mem.eql(u8, trimmed, "end");
        const is_else_line = std.mem.startsWith(u8, trimmed, "else") and
            (trimmed.len == 4 or trimmed[4] == ' ' or trimmed[4] == '\t');

        if (is_end_line and depth > 0) {
            depth -= 1;
        } else if (is_else_line and depth > 0) {
            depth -= 1; // Unindent for else, but will re-indent after
        }

        // Add indentation
        var i: u32 = 0;
        while (i < depth) : (i += 1) {
            try result.appendSlice(allocator, indent_str);
        }

        // Add the trimmed content
        try result.appendSlice(allocator, trimmed);

        // Check for block-starting constructs that increase indent for next line
        if (is_else_line) {
            depth += 1; // Re-indent after else
        } else if (startsBlockWithLookahead(trimmed, all_lines.items, line_idx)) {
            depth += 1;
        }
    }

    return result.toOwnedSlice(allocator);
}

/// Check if a line starts a block (increases indent), with lookahead to handle edge cases
fn startsBlockWithLookahead(trimmed: []const u8, all_lines: []const []const u8, current_idx: usize) bool {
    // Handle interface (starts block but ends with interface name)
    if (std.mem.startsWith(u8, trimmed, "interface ")) return true;

    // Handle type declarations
    if (std.mem.startsWith(u8, trimmed, "type ")) return true;
    if (std.mem.startsWith(u8, trimmed, "export type ")) return true;

    // Handle function declarations - but NOT interface method signatures
    // Interface method signatures are followed immediately by 'end' or another signature
    if (std.mem.startsWith(u8, trimmed, "function ") or std.mem.startsWith(u8, trimmed, "export function ")) {
        // Check if this is a method signature (no body) by looking at next non-empty line
        if (current_idx + 1 < all_lines.len) {
            // Find next non-empty line
            var next_idx = current_idx + 1;
            while (next_idx < all_lines.len) {
                const next_trimmed = std.mem.trim(u8, all_lines[next_idx], " \t\r");
                if (next_trimmed.len > 0) {
                    // If next line is 'end' or another function signature, this is a signature without body
                    if (std.mem.startsWith(u8, next_trimmed, "end ") or
                        std.mem.startsWith(u8, next_trimmed, "function "))
                    {
                        return false; // Interface method signature, not a block
                    }
                    break;
                }
                next_idx += 1;
            }
        }
        return true;
    }

    // Handle control flow with labels ('label' at end)
    // Check for patterns like: if ... 'label', for ... 'label', while ... 'label', etc.
    if (trimmed.len > 0 and trimmed[trimmed.len - 1] == '\'') {
        // This line ends with a label, likely a block start
        if (std.mem.startsWith(u8, trimmed, "if ")) return true;
        if (std.mem.startsWith(u8, trimmed, "for ")) return true;
        if (std.mem.startsWith(u8, trimmed, "while ")) return true;
        if (std.mem.startsWith(u8, trimmed, "match ")) return true;
    }

    return false;
}

/// Simple block detection for folding ranges (doesn't need lookahead)
fn startsBlock(trimmed: []const u8) bool {
    if (std.mem.startsWith(u8, trimmed, "interface ")) return true;
    if (std.mem.startsWith(u8, trimmed, "type ")) return true;
    if (std.mem.startsWith(u8, trimmed, "export type ")) return true;
    if (std.mem.startsWith(u8, trimmed, "function ")) return true;
    if (std.mem.startsWith(u8, trimmed, "export function ")) return true;

    if (trimmed.len > 0 and trimmed[trimmed.len - 1] == '\'') {
        if (std.mem.startsWith(u8, trimmed, "if ")) return true;
        if (std.mem.startsWith(u8, trimmed, "for ")) return true;
        if (std.mem.startsWith(u8, trimmed, "while ")) return true;
        if (std.mem.startsWith(u8, trimmed, "match ")) return true;
    }

    return false;
}

// ============================================================================
// Folding Range Implementation
// ============================================================================

/// Find folding ranges in document content
fn findFoldingRanges(content: []const u8, allocator: std.mem.Allocator, ranges: *std.ArrayListUnmanaged(types.FoldingRange)) !void {
    var lines = std.mem.splitScalar(u8, content, '\n');
    var line_num: u32 = 0;

    // Stack to track block starts
    var block_stack: std.ArrayListUnmanaged(u32) = .empty;
    defer block_stack.deinit(allocator);

    while (lines.next()) |line| : (line_num += 1) {
        const trimmed = std.mem.trim(u8, line, " \t\r");
        if (trimmed.len == 0) continue;

        // Check for block starts
        if (startsBlock(trimmed)) {
            try block_stack.append(allocator, line_num);
        }
        // Check for block ends
        else if (std.mem.startsWith(u8, trimmed, "end ") or std.mem.eql(u8, trimmed, "end")) {
            if (block_stack.items.len > 0) {
                const start_line = block_stack.items[block_stack.items.len - 1];
                block_stack.items.len -= 1;
                try ranges.append(allocator, .{
                    .startLine = start_line,
                    .endLine = line_num,
                });
            }
        }
    }
}

// ============================================================================
// Linked Editing Range Implementation
// ============================================================================

/// Find linked editing ranges at a position
fn findLinkedEditingRanges(allocator: std.mem.Allocator, content: []const u8, line: u32, character: u32) !?types.LinkedEditingRanges {
    // Get all lines
    var lines_list: std.ArrayListUnmanaged([]const u8) = .empty;
    defer lines_list.deinit(allocator);

    var lines_iter = std.mem.splitScalar(u8, content, '\n');
    while (lines_iter.next()) |l| {
        try lines_list.append(allocator, l);
    }

    if (line >= lines_list.items.len) return null;

    const current_line = lines_list.items[line];

    // Get the word at the cursor position
    const word = getWordAtPositionInLine(current_line, character) orelse return null;

    // Determine context: are we in a function name, block label, or method name?
    var result_ranges: std.ArrayListUnmanaged(types.Range) = .empty;
    errdefer result_ranges.deinit(allocator);

    // Case 1: Check if we're on a function declaration line
    const trimmed_current = std.mem.trim(u8, current_line, " \t\r");
    if (std.mem.startsWith(u8, trimmed_current, "function ") or std.mem.startsWith(u8, trimmed_current, "export function ")) {
        // Extract function name
        const func_start_idx = std.mem.indexOf(u8, trimmed_current, "function ").? + "function ".len;
        const func_name_end = std.mem.indexOfAny(u8, trimmed_current[func_start_idx..], "(") orelse (trimmed_current.len - func_start_idx);
        const func_name = std.mem.trim(u8, trimmed_current[func_start_idx..][0..func_name_end], " \t");

        if (std.mem.eql(u8, word, func_name)) {
            // Find function name position in original line
            const name_start = std.mem.indexOf(u8, current_line, func_name).?;
            try result_ranges.append(allocator, .{
                .start = .{ .line = line, .character = @intCast(name_start) },
                .end = .{ .line = line, .character = @intCast(name_start + func_name.len) },
            });

            // Find the matching end label
            try findMatchingEndLabel(allocator, lines_list.items, line, func_name, &result_ranges);
        }
    }

    // Case 2: Check if we're in a block label (like 'loop' at end of for/if/etc.)
    if (result_ranges.items.len == 0) {
        // Check if cursor is in a label (inside quotes at end of line)
        if (findLabelAtPosition(current_line, character)) |label_info| {
            try result_ranges.append(allocator, .{
                .start = .{ .line = line, .character = @intCast(label_info.start) },
                .end = .{ .line = line, .character = @intCast(label_info.end) },
            });

            // Find the matching start/end label
            try findMatchingLabel(allocator, lines_list.items, line, label_info.label, &result_ranges);
        }
    }

    // Case 3: Check if we're in a method implementation (Interface.method)
    if (result_ranges.items.len == 0) {
        if (std.mem.indexOf(u8, trimmed_current, "function ")) |func_idx| {
            const after_func = trimmed_current[func_idx + "function ".len ..];
            if (std.mem.indexOf(u8, after_func, ".")) |dot_idx| {
                const method_start = dot_idx + 1;
                const method_end = std.mem.indexOfAny(u8, after_func[method_start..], "(") orelse (after_func.len - method_start);
                const method_name = after_func[method_start..][0..method_end];

                if (std.mem.eql(u8, word, method_name)) {
                    // Find method name position in trimmed then convert to original
                    const pos_in_trimmed = func_idx + "function ".len + method_start;
                    const pos_in_line = findPositionInOriginal(current_line, trimmed_current, pos_in_trimmed);

                    try result_ranges.append(allocator, .{
                        .start = .{ .line = line, .character = @intCast(pos_in_line) },
                        .end = .{ .line = line, .character = @intCast(pos_in_line + method_name.len) },
                    });

                    // Find matching end label
                    try findMatchingEndLabel(allocator, lines_list.items, line, method_name, &result_ranges);
                }
            }
        }
    }

    if (result_ranges.items.len < 2) {
        result_ranges.deinit(allocator);
        return null;
    }

    return types.LinkedEditingRanges{
        .ranges = try result_ranges.toOwnedSlice(allocator),
    };
}

fn getWordAtPositionInLine(line: []const u8, character: u32) ?[]const u8 {
    if (character >= line.len) return null;

    var start: usize = character;
    while (start > 0) {
        const c = line[start - 1];
        if (!std.ascii.isAlphanumeric(c) and c != '_') break;
        start -= 1;
    }

    var end: usize = character;
    while (end < line.len) {
        const c = line[end];
        if (!std.ascii.isAlphanumeric(c) and c != '_') break;
        end += 1;
    }

    if (start < end) {
        return line[start..end];
    }
    return null;
}

const LabelInfo = struct {
    label: []const u8,
    start: usize,
    end: usize,
};

fn findLabelAtPosition(line: []const u8, character: u32) ?LabelInfo {
    // Look for 'label' pattern - find quote before and after cursor
    var idx: usize = 0;
    while (idx < line.len) {
        if (line[idx] == '\'') {
            const label_start = idx + 1;
            // Find closing quote
            var end_idx = label_start;
            while (end_idx < line.len and line[end_idx] != '\'') {
                end_idx += 1;
            }
            if (end_idx < line.len and end_idx > label_start) {
                // Check if cursor is within this label
                if (character >= label_start and character <= end_idx) {
                    return .{
                        .label = line[label_start..end_idx],
                        .start = label_start,
                        .end = end_idx,
                    };
                }
            }
            idx = end_idx + 1;
        } else {
            idx += 1;
        }
    }
    return null;
}

fn findMatchingEndLabel(allocator: std.mem.Allocator, lines: []const []const u8, start_line: u32, label: []const u8, ranges: *std.ArrayListUnmanaged(types.Range)) !void {
    // Search forward from start_line for "end 'label'"
    var depth: i32 = 1;
    var line_idx = start_line + 1;

    while (line_idx < lines.len) : (line_idx += 1) {
        const trimmed = std.mem.trim(u8, lines[line_idx], " \t\r");

        // Track depth with block constructs
        if (startsBlock(trimmed)) {
            depth += 1;
        } else if (std.mem.startsWith(u8, trimmed, "end ")) {
            depth -= 1;
            if (depth == 0) {
                // This is our matching end - check if it has the right label
                if (std.mem.indexOf(u8, trimmed, "'")) |quote_start| {
                    const label_start = quote_start + 1;
                    if (std.mem.indexOf(u8, trimmed[label_start..], "'")) |quote_end| {
                        const end_label = trimmed[label_start..][0..quote_end];
                        if (std.mem.eql(u8, end_label, label)) {
                            // Find position in original line
                            const orig_line = lines[line_idx];
                            if (std.mem.indexOf(u8, orig_line, "'")) |orig_quote| {
                                try ranges.append(allocator, .{
                                    .start = .{ .line = line_idx, .character = @intCast(orig_quote + 1) },
                                    .end = .{ .line = line_idx, .character = @intCast(orig_quote + 1 + label.len) },
                                });
                            }
                        }
                    }
                }
                break;
            }
        }
    }
}

fn findMatchingLabel(allocator: std.mem.Allocator, lines: []const []const u8, current_line: u32, label: []const u8, ranges: *std.ArrayListUnmanaged(types.Range)) !void {
    // Check if current line is an "end" line - search backwards for start
    const trimmed_current = std.mem.trim(u8, lines[current_line], " \t\r");

    if (std.mem.startsWith(u8, trimmed_current, "end ")) {
        // Search backwards for the matching block start
        var depth: i32 = 1;
        var line_idx: i32 = @as(i32, @intCast(current_line)) - 1;

        while (line_idx >= 0) : (line_idx -= 1) {
            const idx: usize = @intCast(line_idx);
            const trimmed = std.mem.trim(u8, lines[idx], " \t\r");

            if (std.mem.startsWith(u8, trimmed, "end ") or std.mem.eql(u8, trimmed, "end")) {
                depth += 1;
            } else if (startsBlock(trimmed)) {
                depth -= 1;
                if (depth == 0) {
                    // Found our matching start - check for label
                    if (std.mem.indexOf(u8, trimmed, "'")) |quote_start| {
                        const label_start = quote_start + 1;
                        if (std.mem.indexOf(u8, trimmed[label_start..], "'")) |quote_end| {
                            const start_label = trimmed[label_start..][0..quote_end];
                            if (std.mem.eql(u8, start_label, label)) {
                                const orig_line = lines[idx];
                                if (std.mem.indexOf(u8, orig_line, "'")) |orig_quote| {
                                    try ranges.append(allocator, .{
                                        .start = .{ .line = @intCast(idx), .character = @intCast(orig_quote + 1) },
                                        .end = .{ .line = @intCast(idx), .character = @intCast(orig_quote + 1 + label.len) },
                                    });
                                }
                            }
                        }
                    }
                    break;
                }
            }
        }
    } else {
        // Current line is a block start - search forward
        try findMatchingEndLabel(allocator, lines, current_line, label, ranges);
    }
}

fn findPositionInOriginal(original: []const u8, trimmed: []const u8, pos_in_trimmed: usize) usize {
    // Find where the trimmed content starts in original
    const trim_start = std.mem.indexOf(u8, original, trimmed) orelse 0;
    return trim_start + pos_in_trimmed;
}

fn formatFuncSignature(func: ast_to_ir.FuncInfo) []const u8 {
    if (func.return_type_name) |rt| {
        return rt;
    }
    return func.return_type.toMaxonName();
}

/// Get the identifier word at the given position in content
fn getWordAtPosition(content: []const u8, line: u32, character: u32) ?[]const u8 {
    // Find the specified line
    var lines = std.mem.splitScalar(u8, content, '\n');
    var current_line: u32 = 0;

    while (lines.next()) |line_content| : (current_line += 1) {
        if (current_line == line) {
            // Found the line, now extract the word at character position
            if (character >= line_content.len) return null;

            // Find the start of the identifier (scan backwards)
            var start: usize = character;
            while (start > 0) {
                const c = line_content[start - 1];
                if (!std.ascii.isAlphanumeric(c) and c != '_') break;
                start -= 1;
            }

            // Find the end of the identifier (scan forwards)
            var end: usize = character;
            while (end < line_content.len) {
                const c = line_content[end];
                if (!std.ascii.isAlphanumeric(c) and c != '_') break;
                end += 1;
            }

            if (start < end) {
                return line_content[start..end];
            }
            return null;
        }
    }

    return null;
}
