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
};

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
