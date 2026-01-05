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
